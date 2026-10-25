using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>관찰 장면 — 물체 (docs/PHYSICS.md §10 '관찰 장면' · PH5): 관전 화면의 한 틱 늦은 시계로 틱 사이를 메운다.</summary>
public static partial class Program
{
    /// <summary>
    /// props-track (PH5 — 함장이 기다린 '틱 사이를 매끄럽게'): 중력이 꺼진 부엌 외판 0.3m² 감압의 첫 틱을 관전 화면이 틱 사이 f 0.25 · 0.5 · 0.75 세 장으로 그린다 —
    /// 떠 있는 물건이 순간이동하지 않고 그 틱 안의 길 (틱 안 기록의 열쇠틀 · 에르미트)대로 구멍 쪽으로 떠가며 돌고 · 우주로 나가는 것은 나간 때까지 그려진다.
    /// </summary>
    private static void PropsTrackScene(string dir)
    {
        const string art = "\n##############\n#....ss......#\n#..........tt#\n#..........tt#\n#............#\n#.....f......#\n######D#######\n#............#\n##############";
        var lay = Worlds.Ascii(art, new Dictionary<char, string> { ['t'] = "Table", ['s'] = "Stove", ['f'] = "Fridge" });
        var s = Worlds.Full(lay, 5);
        var st = s.Get<Structure>(); var f = s.Get<Field>();
        int At(int x, int y) => Worlds.At(lay, x, y);
        st.RequestDoor(lay.DoorAt[At(6, 6)], 0);
        (string name, double x, double y, double th)[] put =
        {
            ("식량 상자", 1.5, 2.08, 0), ("공구 상자", 1.6, 2.95, 0.4), ("책", 2.5, 1.7, 0.8), ("컵", 2.45, 3.4, 0), ("수리 꾸러미", 2.6, 2.5, 1.0),
            ("접시", 3.4, 3.4, 0), ("물병", 2.6, 4.5, 0), ("식량 상자", 4.2, 5.0, -0.2), ("소화기", 9.0, 4.4, 0), ("컵", 8.2, 3.2, 0),
        };
        foreach (var (name, x, y, th) in put)
        {
            int id = Worlds.PutItem(s, name, lay.Index((int)x + 1, (int)y + 1));
            int i = st.IndexOf(id); var p = st.Props[i]; p.X = x + 1; p.Y = y + 1; p.Th = th; st.Props[i] = p;
        }
        Array.Fill(f.Grav, 0f);   // 중력이 꺼진 배 — 바닥 마찰이 없어 감압 바람에 밀린 것이 틱 내내 떠간다 (틱 사이 메움이 가장 잘 보인다)
        var show = new LiveShow(s, "시험호") { SpeedOverride = 1, Follow = false };
        show.Ears.Enabled = false;
        show.CamX = 7.5; show.CamY = 4.6; show.ViewW = 14;
        for (int k = 0; k < 3; k++) show.Advance(1.0);
        int hole = At(0, 2);
        st.DamageCause[hole] = s.Causes.Add(CauseKind.Setup, "부엌 외판에 파편이 박혔다", s.Tick); f.SetHole(hole, 0.3f);
        show.Advance(1.0);   // 감압 첫 틱을 돌린다 — 그리는 때는 그 틱 처음 (한 틱 늦은 시계)
        var tr = st.Track!;
        int moving = st.Props.Count(p => tr.Of(MotionTrack.WhoProp, p.Id).Length > 1) + tr.Gone.Count;
        var shots = new List<Img>();
        foreach (double fr in new[] { 0.25, 0.5, 0.75 })
        {
            show.Advance(0.25);
            var lf = show.Render();
            // 부엌 구멍 쪽 (도면 x 1.5 ~ 10.5 · y 1.5 ~ 7 m)만 두 배로
            int D = lf.Dot, sx = (int)(lf.SrcX + (1.5 - lf.CamLeft) * D), sy = (int)(lf.SrcY + (1.5 - lf.CamTop) * D), sw = 9 * D, sh = (int)(5.5 * D);
            var cut = new Img(sw, sh);
            for (int y = 0; y < sh; y++) for (int x = 0; x < sw; x++) cut.Set(x, y, lf.Img.Get(Math.Clamp(sx + x, 0, lf.Img.W - 1), Math.Clamp(sy + y, 0, lf.Img.H - 1)));
            shots.Add(cut.Scale(2));
            show.Recycle(lf.Img);
            string where = string.Join(" · ", Enumerable.Range(1, 10).Where(id => tr.Of(MotionTrack.WhoProp, id).Length > 1).Select(id => { var d = show.DrawnProp(id); return double.IsNaN(d.x) ? "나감" : $"({d.x:0.00},{d.y:0.00} · {d.th:0.00})"; }));
            Console.WriteLine($"  props-track f {fr:0.00}: 그린 자리 {where}");
        }
        const int gap = 6;
        var sheet = new Img(shots.Sum(i => i.W) + gap * (shots.Count - 1), shots.Max(i => i.H));
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#1c2027"));
        int ox = 0;
        foreach (var im in shots) { sheet.Draw(im, ox, 0); ox += im.W + gap; }
        Directory.CreateDirectory(dir);
        Png.WriteRgba(Path.Combine(dir, "ph5_track.png"), sheet.W, sheet.H, sheet.Px);
        Console.WriteLine($"  props-track: {dir}/ph5_track.png ({sheet.W}×{sheet.H}) — 무중력 부엌 0.3m² 감압 첫 틱 f 0.25 · 0.5 · 0.75 (움직인 물체 {moving} · 열쇠틀 {tr.Keys.Count} · 우주로 {tr.Gone.Count})");
    }
}
