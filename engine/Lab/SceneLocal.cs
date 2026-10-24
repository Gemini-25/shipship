using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>③ 국소 현상 관찰 장면 — (가) 불 난 방의 연기층과 몸을 낮춘 사람 · (나) 외판 구멍에 빨려 붙어 구멍을 막은 사람 · 상자.</summary>
public static partial class Program
{
    private static void LocalScene(string dir)
    {
        var t = Tables.Default;
        var page = new System.Text.StringBuilder();
        int k = 0;
        void Frame(Sim s, Painter painter, int x0, int y0, int w, int h, string text)
        {
            var img = painter.Frame(s, x0, y0, w, h, k);
            Png.WriteRgba(Path.Combine(dir, $"{k:000}.png"), img.W, img.H, img.Px);
            page.Append($"<figure><img src=\"{k:000}.png\"><figcaption>{Time.Clock(s.Tick)}<br>{System.Net.WebUtility.HtmlEncode(text)}</figcaption></figure>");
            Console.WriteLine($"    {Time.Clock(s.Tick)} {text}");
            k++;
        }

        // ── (가) 불 난 방: 오른쪽 방 의자에 불 — 연기층이 서고 · 그 방 사람은 몸을 낮춘다 ──
        {
            var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 6);
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, At(17, 3));
            s.Run(5);
            var painter = new Painter(lay);
            int fire = At(15, 1);
            s.Get<Chemistry>().Ignite(fire, 3e6, s.Causes.Add(CauseKind.Setup, "의자 밑 불씨", s.Tick, fire));
            string Line()
            {
                int c = m[p].C.Cell, probe = At(15, 2);
                return $"불 곁 칸 층 {f.Strat[probe]:0.00} · 위 {f.HeadTemp(probe, false) - 273.15:0}℃ CO {f.HeadPpm(t.gCO, probe, false):0}ppm / 아래 {f.HeadTemp(probe, true) - 273.15:0}℃ CO {f.HeadPpm(t.gCO, probe, true):0}ppm · {m.NameOf(p)} {(life[m[p].C.Body].Low != 0 ? "몸을 낮춤" : "서 있음")} ({lay.X(c)},{lay.Y(c)}) · COHb {life[m[p].C.Body].COHb * 100:0.0}% · {m.Thought(p)}";
            }
            Frame(s, painter, 10, 0, 15, 8, "불이 붙었다 — " + Line());
            for (int i = 0; i < 8; i++) { s.Run(8); Frame(s, painter, 10, 0, 15, 8, Line()); }
        }

        // ── (나) 외판 0.075m² 구멍: 곁의 이가 빨려 붙어 막는다 · 의자 곁 이는 붙잡는다 · 0.06m² 구멍 곁 식량 상자가 막는다 ──
        {
            var lay = Worlds.Ascii(LocalRooms.Replace("#.....D", "#.....P").Replace("#a....#", "#.....#"), Worlds.Seats);
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 1);
            var m = s.Get<Mind>(); var st = s.Get<Structure>(); var f = s.Get<Field>(); var life = s.Get<Life>();
            s.Run(3);
            int p = m.AddPerson(s, At(1, 2));
            var painter = new Painter(lay);
            int cause = s.Causes.Add(CauseKind.Setup, "화물칸 외판에 파편이 박혔다", s.Tick);
            st.DamageCause[At(0, 2)] = cause; f.SetHole(At(0, 2), 0.075f);
            string Line() => $"화물칸 {f.Pressure(At(3, 2)):0.0}kPa · 구멍 0.075m² 막음 {f.PlugOf(At(0, 2)):0.000}m² · {m.NameOf(p)} {(m[p].C.PinAt != 0 ? "구멍에 빨려 붙음" : "떨어짐")} · 타박 {life[m[p].C.Body].Blunt:0.00} · 산소 포화 {life[m[p].C.Body].SpO2 * 100:0}%";
            Frame(s, painter, 0, 0, 15, 5, "파편이 박혔다 — " + Line());
            s.Step();
            var pin = s.Facts.Since(0).Where(x => x.Kind == FactKind.Hurt).Select(x => s.Causes.Story(x.Cause)).LastOrDefault() ?? "";
            Frame(s, painter, 0, 0, 15, 5, pin + " — " + Line());
            for (int i = 0; i < 6; i++) { s.Run(Time.Minutes(1)); Frame(s, painter, 0, 0, 15, 5, Line()); }
        }
        File.WriteAllText(Path.Combine(dir, "index.html"), $"<!doctype html><meta charset=utf-8><title>local</title><style>body{{background:#0b0d12;color:#cfd6e4;font:13px sans-serif;display:flex;flex-wrap:wrap;gap:8px}}figure{{margin:0;width:480px}}img{{width:480px;image-rendering:pixelated}}</style>{page}");
        Console.WriteLine($"  local: {k}장 → {dir}");
    }
}
