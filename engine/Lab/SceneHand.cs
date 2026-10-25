using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>관찰 장면 'hand' (docs/UI2.md §8 · U2): 디딤호 (6명 · 로봇 2 · 항해 중)에 함장의 손 갈래마다 하나씩 (따로 띄운 쌍둥이 · 10분) — 원인 사슬 첫 줄 · 첫 사실 · 주컴퓨터가 안 때 · 누가 갔나 · 그 자리 그림.</summary>
public static partial class Program
{
    private static void HandScene(string dir)
    {
        var base0 = HandShip(18);
        base0.Run(30);
        var aim = HandTargets(base0);
        var L = base0.Layout;
        var painter = new Painter(L);
        var list = new (string Name, HandOrder O)[]
        {
            ("fire", Order(HandKind.Fire, aim.Bed, 1)), ("hole", Order(HandKind.Hole, aim.Hull, 1)), ("power", Order(HandKind.PowerCut, aim.Power)), ("data", Order(HandKind.DataCut, aim.Data)),
            ("fault", Order(HandKind.Fault, aim.O2Gen, part: (byte)ShipEngine.Part.Seal)), ("gravity", Order(HandKind.Gravity, -1)), ("water", Order(HandKind.WaterLeak, aim.Water, 1)),
            ("coolant", Order(HandKind.CoolantLeak, aim.Coolant, 2)), ("door", Order(HandKind.DoorJam, aim.Door)), ("o2drift", Order(HandKind.O2Drift, -1, 2)), ("blast", Order(HandKind.Blast, aim.Bed, 1)),
            ("shove", Order(HandKind.Shove, -1, obj: aim.Prop, x: 0, y: 8)), ("rock", Order(HandKind.Rock, aim.Hull, 1)), ("cloud", Order(HandKind.Cloud, aim.Hull)), ("flare", Order(HandKind.Flare, -1)),
        };
        foreach (var (name, o) in list)
        {
            var s = base0.Clone(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>();
            var before = new HashSet<int>(sm.Incidents.Where(x => x.Active != 0).Select(x => x.Id));
            long t0 = s.Tick, knew = -1;
            string know = "", who = "";
            s.Get<Hand>().Enqueue(o);
            for (long k = 0; k < Time.Minutes(10); k++)
            {
                s.Step();
                foreach (var x in sm.Incidents)
                {
                    if (x.Active == 0 || before.Contains(x.Id)) continue;
                    if (knew < 0) { knew = s.Tick; know = $"{sm.RoomName(x.Room)} {ShipMind.KindName(x.Kind)}"; }
                    if (who.Length == 0) foreach (int r in new[] { x.RobotA, x.RobotB, x.Person }) if (r >= 0 && r < m.Agents.Count) { who = m.NameOf(r); break; }
                }
            }
            var done = s.Get<Hand>().Done[0];
            string root = done.Cause != 0 ? s.Causes.Text(done.Cause) : "헛손 — " + Hand.Why(s, o);
            var first = s.Facts.Since(0).FirstOrDefault(f => f.Tick >= t0 && f.Cause != 0 && done.Cause != 0 && s.Causes.Root(f.Cause) == done.Cause);
            Console.WriteLine($"  hand {name}: {root} · 첫 사실 {(first.Cause != 0 ? $"{first.Kind} {Time.Clock(first.Tick)}" : "없음")} · 주컴퓨터가 안 때 {(knew >= 0 ? $"{(knew - t0) * Time.SecondsPerTick:0}초 뒤 ({know})" : "모른다")} · 간 이 {(who.Length > 0 ? who : "없음")}");
            int c = s.Causes[done.Cause].Cell >= 0 ? s.Causes[done.Cause].Cell : aim.Bed;
            int w = 22, h = 14, x0 = Math.Clamp(L.X(c) - w / 2, 0, Math.Max(0, L.W - w)), y0 = Math.Clamp(L.Y(c) - h / 2, 0, Math.Max(0, L.H - h));
            var img = painter.Frame(s, x0, y0, w, h, 0);
            Directory.CreateDirectory(dir);
            Png.WriteRgba(Path.Combine(dir, $"hand-{name}.png"), img.W, img.H, img.Px);
        }
        Console.WriteLine($"  hand: {dir}/hand-*.png ({list.Length}장 — 손 뒤 10분)");
    }
}
