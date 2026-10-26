using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>
/// 관찰 장면 '컵' (docs/STAGE5.md C10b · `Lab --scene cup`): 식당 탁자 위 컵 하나 · 목마른 이 — 컵을 집어 배식기에서 마시고 제자리에 내려놓는다.
/// 세 장: 처음 (탁자 위 컵) · 컵을 든 채 배식기에서 마시는 중 (탁자가 비었다) · 다 마시고 제자리에 둔 뒤.
/// </summary>
public static partial class Program
{
    private static void CupScene(string dir)
    {
        const string art = @"
###########
#.........#
#..t...m..#
#pb.......#
###########";
        var lay = Worlds.Ascii(art, new Dictionary<char, string> { ['t'] = "Table", ['m'] = "MealDispenser", ['p'] = "PowerPanel", ['b'] = "Battery" });
        lay.Rooms[lay.Room[Worlds.At(lay, 1, 1)]].Type = "Mess";
        var s = Worlds.Full(lay, 3);
        var m = s.Get<Mind>(); var st = s.Get<Structure>();
        int cupItem = m.ItemNames.ToList().IndexOf("컵");
        int id = st.PutItem(cupItem, 0, Worlds.At(lay, 3, 2), 0, 5);
        { int i = st.IndexOf(id); var pr = st.Props[i]; pr.Flags |= Structure.DecorFlag; pr.Asleep = 1; st.Props[i] = pr; }
        int p = m.AddPerson(s, Worlds.At(lay, 1, 1));
        s.Run(2);
        const int zoom = 2, gap = 6;
        var painter = new Painter(lay);
        int fw = lay.W * Painter.T * zoom, fh = lay.H * Painter.T * zoom;
        var sheet = new Img(3 * (fw + gap) - gap, fh);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        void Shot(int k, string what)
        {
            sheet.Draw(painter.Frame(s, 0, 0, lay.W, lay.H, k).Scale(zoom), k * (fw + gap), 0);
            var cups = st.Props.Where(x => x.Kind == 0 && x.What == cupItem).ToList();
            Console.WriteLine($"  cup {k}: {what} — {Time.Clock(s.Tick)} · 탁자 위 컵 {cups.Count(x => x.On > 0)} · 든 컵 {(m[p].C.Holding == cupItem ? $"{m[p].C.HoldCharge:0.0}몰" : "없음")} · 걸음 {(m[p].C.StepIndex < m[p].Plan.Count ? m[p].Plan[m[p].C.StepIndex].Kind.ToString() : "-")}");
        }
        Shot(0, "처음");
        long d0 = m.Drinks;
        for (int k = 0; k < (int)Time.Minutes(20) && m.Drinks == d0; k++)
        {
            ref var c = ref m[p].C; c.Thirst = 1; c.Hunger = 0; c.Sleepy = 0; c.Lonely = 0; c.Bored = 0;
            var pl = m[p].Plan;
            bool drinking = c.StepIndex < pl.Count && pl[c.StepIndex].Kind == StepKind.Use && (UseKind)pl[c.StepIndex].Arg == UseKind.Drink && s.Tick > c.StepStart + 20;
            if (drinking) break;
            s.Step();
        }
        Shot(1, "컵을 들고 마시는 중");
        for (int k = 0; k < (int)Time.Minutes(5); k++) { ref var c = ref m[p].C; c.Thirst = 0; c.Hunger = 0; c.Sleepy = 0; c.Lonely = 0; c.Bored = 0; s.Step(); }
        Shot(2, "제자리에 둔 뒤");
        Console.WriteLine($"  cup: 집음 {m.CupsTaken} · 제자리 {m.CupsBack} · 마심 {m.Drinks - d0}");
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "c10b_cup.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  cup: {dir}/c10b_cup.png ({sheet.W}×{sheet.H}) — 처음 · 컵을 들고 마시는 중 (탁자가 비었다) · 제자리에 둔 뒤");
    }
}
