using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>
/// 5단 A1 관찰 장면 '화장실 그림' — 새뜰호(40명 · 로봇 10)의 아침 06:00~09:00: 쓰는 동안 닫힌 샤워 커튼 · 흐르는 물줄기 · 변기의 '사용 금지' 불 ·
/// 넘친 변기 발치의 물 · 뜨거운 물 위로 오르는 김 (5단 A2)이 그림에 실제로 나오는가 (1분마다 세계를 보고 · 처음 몇 장을 찍는다 — 재미 · 빈도는 재지 않는다).
/// 차림 (원인 마디 Setup): 새벽에 변기 하나가 막혀 모르고 쓴 이가 넘쳤다 (막힘 · 발치의 물) · 어젯밤 늦게까지 정비한 여섯이 씻지 못하고 잤다 (더러움).
/// A2 검토: 끝 줄에 안개 칸 · 거울 김 서림을 화장실 안 · 밖으로 나눠 센다 (1분마다 본 가운데 가장 많을 때 — 화장실 밖의 안개가 그림에 드러나게 · 세계 습도가 1을 넘는 칸도).
/// </summary>
public static partial class Program
{
    private static void WashViewScene(string dir)
    {
        ulong seed = ulong.TryParse(Environment.GetEnvironmentVariable("SEED"), out ulong sd) ? sd : 7;
        var s = ShipWorld.Crewed("Saetteul", seed, 40, 10);
        while (Time.HourOfDay(s.Tick) < 6) s.Run(25);
        var L = s.Layout; var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var f = s.Get<Field>();
        var toilets = L.Furniture.Where(fu => fu.Type == "Toilet" && fu.Cells.Length > 0).ToList();
        var wash = L.Furniture.Where(fu => fu.Type is "Toilet" or "Sink" or "Shower" && fu.Cells.Length > 0).ToList();
        var showers = wash.Where(fu => fu.Type == "Shower").Select(fu => (fu, dev: net.Devices.FirstOrDefault(d => d.Furniture == fu.Id)?.Id ?? -1)).ToList();
        var clogged = toilets[0];
        int cause = s.Causes.Add(CauseKind.Setup, "새벽에 막힌 변기를 모르고 써서 넘쳤다 — 발치에 물이 고였다", s.Tick, clogged.Cells[0]);
        life.Clog(clogged.Id, cause); life.Bowl[clogged.Id] = 0.3;
        f.AddStore(clogged.Cells[0], f.T.sWater, 25);
        s.Causes.Add(CauseKind.Setup, "어젯밤 늦게까지 기관실 정비를 한 여섯은 씻지 못하고 잤다", s.Tick);
        foreach (var a in m.Agents.Where(a => a.IsPerson).Take(6)) life[a.C.Body].Dirt = 0.7f;   // 일어나면 씻으러 간다 (언제 · 어디서는 제 판단)

        var painter = new Painter(L);
        var page = new System.Text.StringBuilder();
        int k = 0, looks = 0;
        var seen = new int[6]; var shots = new int[6];   // 커튼 닫힘 · 샤워 물줄기 · 사용 금지 불 · 발치 물 · 손 씻는 물줄기 · 김 (뜨거운 물)
        string[] what = { "커튼 닫힌 샤워 칸", "샤워 물줄기", "'사용 금지' 불 켜진 변기", "설비 발치의 물", "손 씻는 물줄기", "김이 오르는 샤워 칸" };
        void Shot(int cell, string text, int D = 32)
        {
            int vw = D == 32 ? 29 : 58, vh = D == 32 ? 19 : 38;
            int x0 = Math.Clamp(L.X(cell) - vw / 2, 0, Math.Max(0, L.W - vw)), y0 = Math.Clamp(L.Y(cell) - vh / 2, 0, Math.Max(0, L.H - vh));
            var img = D == 32 ? painter.Frame(s, x0, y0, vw, vh, k) : painter.FrameFar(s, x0, y0, vw, vh, k, D);
            Png.WriteRgba(Path.Combine(dir, $"{k:000}.png"), img.W, img.H, img.Px);
            page.Append($"<figure><img src=\"{k:000}.png\"><figcaption>{Time.Clock(s.Tick)} · {System.Net.WebUtility.HtmlEncode(text)}</figcaption></figure>");
            Console.WriteLine($"    {Time.Clock(s.Tick)} {text}");
            k++;
        }
        Shot(clogged.Cells[0], "06:00 — 새벽에 넘친 변기 (막힘 · 발치에 고인 물)");
        // 안개 · 거울 김 서림 (그림과 같은 문턱 — 바닥 칸과 열린 문 칸 · 세면대 칸): 화장실 안 · 밖으로 나눠 1분마다 세고 가장 많을 때를 남긴다
        bool InWash(int c) => L.Room[c] >= 0 && L.Rooms[L.Room[c]].Type == "Washroom";
        var sinks = wash.Where(fu => fu.Type == "Sink").ToList();
        int fogIn = 0, fogOut = 0, mirIn = 0, mirOut = 0, over1 = 0; double rhMax = 0;
        void CountAir()
        {
            int fi = 0, fo = 0, mi = 0, mo = 0, o1 = 0;
            for (int c = 0; c < L.N; c++)
            {
                if (L.Kind[c] is not (CellKind.Floor or CellKind.Door)) continue;
                if (L.Kind[c] == CellKind.Door && L.DoorAt[c] >= 0 && f.DoorOpen[L.DoorAt[c]] < 0.05f) continue;
                double rh = f.RelHumidity(c);
                rhMax = Math.Max(rhMax, rh);
                if (rh > 1) o1++;
                if (rh > Painter.FogRh) { if (InWash(c)) fi++; else fo++; }
            }
            foreach (var fu in sinks) if (f.RelHumidity(fu.Cells[0]) > Painter.MirrorRh) { if (InWash(fu.Cells[0])) mi++; else mo++; }
            fogIn = Math.Max(fogIn, fi); fogOut = Math.Max(fogOut, fo); mirIn = Math.Max(mirIn, mi); mirOut = Math.Max(mirOut, mo); over1 = Math.Max(over1, o1);
        }
        long end = s.Tick + Time.Hours(3);
        while (s.Tick < end)
        {
            s.Step();
            if (s.Tick % 25 != 0) continue;   // 1분마다 세계를 본다
            looks++;
            CountAir();
            var at = new int[] { -1, -1, -1, -1, -1, -1 };
            foreach (var a in m.Agents)
            {
                if (!a.IsPerson || a.C.StepIndex >= a.Plan.Count) continue;
                var st = a.Plan[a.C.StepIndex];
                if (st.Kind != StepKind.Use || st.Target < 0 || st.Target >= L.Furniture.Count) continue;
                if ((UseKind)st.Arg == UseKind.Shower && L.Furniture[st.Target].Cells[0] == a.C.Cell) at[0] = a.C.Cell;
                if ((UseKind)st.Arg == UseKind.Hands && L.Furniture[st.Target].Type == "Sink" && net.WaterAt(st.Target) && net.WaterTank > 0) at[4] = L.Furniture[st.Target].Cells[0];
            }
            foreach (var (fu, dev) in showers) if (dev >= 0 && net.ShowerRunning(dev)) { at[1] = fu.Cells[0]; if (net.Working(dev)) at[5] = fu.Cells[0]; }   // 온수기가 돌면 김
            foreach (var fu in toilets) if (m.WashSign[fu.Id] != 0) at[2] = fu.Cells[0];
            foreach (var fu in wash) if (f.Stored(f.T.sWater, fu.Cells[0]) > 2) at[3] = fu.Cells[0];
            for (int i = 0; i < 6; i++)
            {
                if (at[i] < 0) continue;
                seen[i]++;
                if (shots[i] < 2) { shots[i]++; Shot(at[i], what[i]); }
            }
        }
        Shot(clogged.Cells[0], $"09:00 넓은 보기 16도트 — 막힌 변기 {(life.Clogged[clogged.Id] == 0 ? "뚫렸다" : "그대로")} · 발치 물 {f.Stored(f.T.sWater, clogged.Cells[0]):0.0}몰", 16);
        File.WriteAllText(Path.Combine(dir, "index.html"), $"<!doctype html><meta charset=utf-8><title>washview</title><style>body{{background:#0b0d12;color:#cfd6e4;font:13px sans-serif;display:flex;flex-wrap:wrap;gap:8px}}figure{{margin:0;width:464px}}img{{width:464px;image-rendering:pixelated}}</style>{page}");
        Console.WriteLine($"  washview: 1분마다 {looks}번 본 가운데 커튼 닫힌 장 {seen[0]} · 물줄기 장 {seen[1]} · 불 켜진 변기 {seen[2]} · 발치 물 {seen[3]} · 김 장 {seen[5]} (손 씻는 물줄기 {seen[4]}) · {k}장 → {dir}");
        Console.WriteLine($"  washview: 가장 많을 때 안개 칸 (습도 {Painter.FogRh}) 화장실 안 {fogIn} · 밖 {fogOut} · 거울 김 서림 (세면대 {sinks.Count}) 안 {mirIn} · 밖 {mirOut} · 습도 1을 넘는 칸 {over1} · 가장 높은 습도 {rhMax:0.00}");
    }
}
