using System.Text.RegularExpressions;
using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>
/// 물건 살펴보기 (docs/UI2.md §3 · §8 V1 ~ V4 — 묶음 '살펴보기'): 누르기 차례 (사람 먼저 · 작은 발자국 · 가구 · 문 · 벽 · 바닥 · 화면 10픽셀 여유) ·
/// 올림 = 누름 (떨어져 나간 조각 위 칸도) · 카드 줄 (세계에서 읽기만) · 화면만 (쌍둥이 세계와 찍기 바이트가 같다).
/// </summary>
public static partial class Program
{
    private static readonly Regex LookUnits = new(@"\d[\d.,]*\s*(kPa|mL|mm|cm|kg|kW|m/s|ppm|g|m)(?![A-Za-z])", RegexOptions.Compiled);

    /// <summary>카드 글에 개발 말이 없다 (로마자는 숫자 뒤 단위만 · 못 채운 자리 · NaN · null 없음).</summary>
    private static string? LookBad(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "빈 줄";
        if (Regex.IsMatch(s, @"NaN|null|[{}_]|Infinity|∞")) return "개발 말";
        return Regex.IsMatch(LookUnits.Replace(s, ""), "[A-Za-z]") ? "로마자" : null;
    }

    private static void LookRules()
    {
        if (!Do("살펴보기")) return;
        Console.WriteLine("\n[살펴보기 — 누르기 차례 · 올림 = 누름 · 카드 줄 · 화면만]");
        var show = LiveShow.Crewed("Didim", 21);
        show.FrameBudget = 1000; show.SpeedOverride = 1;
        for (int i = 0; i < 3; i++) show.Advance(1);
        var w = show.Sim; var L = w.Layout; var st = w.Get<Structure>();
        var names = w.Tables.People.Items.Keys.ToList();
        int cupWhat = names.IndexOf("컵"), plateWhat = names.IndexOf("접시");
        var table = L.Furniture.First(f => f.Type == "Table" && st.Props.Any(p => p.What == cupWhat && p.On == f.Id + 1));
        var cup = st.Props.First(p => p.What == cupWhat && p.On == table.Id + 1);
        double p32 = LiveShow.PadFor(48), p16 = LiveShow.PadFor(21), p8 = LiveShow.PadFor(15);
        // 컵이 그려진 자리 (발자국 가운데를 높이 z/2칸만큼 위로 — 3/4 시점: 1층 = 1도트)
        (double x, double y) Raised(Prop p) { var (x, y, _) = show.DrawnProp(p.Id); return (x, y - p.Z * 0.5); }
        var (ux, uy) = Raised(cup);
        double cupHalf = st.PropForm(cup).W * DotForm.Dot / 2;
        var none = Array.Empty<AgentSpot>();
        AgentSpot[] Spot(double x, double y, double d, bool robot = false) => new[] { new AgentSpot(0, x + d, y + 0.15, robot) };   // 사람 그림은 칸 위쪽으로 0.15 솟는다 (Nearest)
        // 탁자 칸 안 · 위에 놓인 것에서 먼 점
        var onTable = st.Props.Where(p => p.On == table.Id + 1).Select(Raised).ToList();
        (double x, double y) bare = (double.NaN, 0);
        foreach (int c in table.Cells)
            for (double fy = 0.1; fy < 1 && double.IsNaN(bare.x); fy += 0.1)
                for (double fx = 0.1; fx < 1 && double.IsNaN(bare.x); fx += 0.1)
                {
                    double x = L.X(c) + fx, y = L.Y(c) + fy;
                    if (onTable.All(q => Math.Abs(q.x - x) + Math.Abs(q.y - y) > 0.9)) bare = (x, y);
                }
        // 빈 바닥 칸 (가구 · 물체에서 두 칸 넘게) · 벽 (곁에 가구 없음) · 문 · 바깥
        int floor = -1, wall = -1, outside = -1;
        for (int c = 0; c < L.N; c++)
        {
            int x = L.X(c), y = L.Y(c);
            bool clear = true;
            for (int dy = -2; dy <= 2 && clear; dy++) for (int dx = -2; dx <= 2 && clear; dx++) if (L.In(x + dx, y + dy) && L.FurnitureAt[L.Index(x + dx, y + dy)] >= 0) clear = false;
            if (clear) clear = st.Props.All(p => Math.Abs(p.X - (x + 0.5)) + Math.Abs(p.Y - (y + 0.5)) > 2.5);
            if (floor < 0 && clear && L.Kind[c] == CellKind.Floor && L.Room[c] >= 0) floor = c;
            if (wall < 0 && clear && L.Kind[c] == CellKind.Wall && L.DoorAt[c] < 0) wall = c;
            if (outside < 0 && L.Kind[c] == CellKind.Outside && x > 0 && y > 0) outside = c;
        }
        int door = L.Doors.FindIndex(d => d.Kind == "normal");
        (double, double) Mid(int c) => (L.X(c) + 0.5, L.Y(c) + 0.5);

        // ── V1 누르기 차례 ──
        {
            var bad = new List<string>();
            void Want(string what, PickKey got, PickKind kind, int id) { if (got.Kind != kind || got.Id != id) bad.Add($"{what}: {got.Kind} {got.Id} (바람 {kind} {id})"); }
            Want("사람 0.3칸 · 컵 위", show.PickAt(ux, uy, p32, Spot(ux, uy, 0.3)), PickKind.Agent, 0);
            Want("사람 0.6칸 · 컵 상자 안", show.PickAt(ux, uy, p32, Spot(ux, uy, 0.6)), PickKind.Prop, cup.Id);
            var (fx0, fy0) = Mid(floor);
            Want("사람 0.6칸 · 빈 바닥", show.PickAt(fx0, fy0, p32, Spot(fx0, fy0, 0.6)), PickKind.Agent, 0);
            var both = new[] { new AgentSpot(1, fx0 + 0.3, fy0 + 0.15, true), new AgentSpot(0, fx0 - 0.5, fy0 + 0.15, false) };
            Want("로봇 0.3 · 사람 0.5", show.PickAt(fx0, fy0, p32, both), PickKind.Agent, 0);
            if (!double.IsNaN(bare.x)) Want("컵 밖 탁자 칸", show.PickAt(bare.x, bare.y, p32, none), PickKind.Furniture, table.Id); else bad.Add("탁자에 빈 자리가 없다");
            Want("8도트 여유에서도 컵 (발자국이 작은 것)", show.PickAt(ux, uy, p8, none), PickKind.Prop, cup.Id);
            if (door >= 0) Want("문", show.PickAt(Mid(L.Doors[door].Cell).Item1, Mid(L.Doors[door].Cell).Item2, p32, none), PickKind.Door, door);
            Want("벽", show.PickAt(Mid(wall).Item1, Mid(wall).Item2, p32, none), PickKind.Wall, wall);
            Want("바닥", show.PickAt(fx0, fy0, p32, none), PickKind.Floor, floor);
            if (outside >= 0) Want("바깥", show.PickAt(Mid(outside).Item1, Mid(outside).Item2, p32, none), PickKind.None, 0);
            // 화면 10픽셀 여유: 컵 상자 밖 0.3칸 — 32도트 (0.21칸)는 컵이 아니고 · 16도트 (0.47칸)는 컵
            var near32 = show.PickAt(ux + cupHalf + 0.3, uy, p32, none); var near16 = show.PickAt(ux + cupHalf + 0.3, uy, p16, none);
            if (near32 == new PickKey(PickKind.Prop, cup.Id) || near16 != new PickKey(PickKind.Prop, cup.Id)) bad.Add($"여유: 32도트 {near32} · 16도트 {near16}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < 400; k++) show.PickAt(ux + (k % 20) * 0.3 - 3, uy + (k / 20) * 0.3 - 3, p32);
            double pickUs = sw.Elapsed.TotalMilliseconds * 1000 / 400;
            sw.Restart();
            show.Inspect(new PickKey(PickKind.Furniture, table.Id));
            for (int k = 0; k < 200; k++) show.LookCard();
            double cardUs = sw.Elapsed.TotalMilliseconds * 1000 / 200;
            show.LookClose();
            Check("살펴보기 V1 누르기 차례: 사람 0.45칸 안은 늘 먼저 · 0.45 ~ 0.9칸은 물체 · 가구 상자 밖일 때만 · 사람과 로봇이 겹치면 사람 · 탁자 위 컵 > 탁자 · 문 · 벽 · 바닥 · 바깥은 없음 · 여유는 화면 10픽셀 (32도트 0.21칸 · 16도트 0.47칸 · 8도트 0.68칸)",
                bad.Count == 0 && Math.Abs(p32 - 0.21) < 0.01 && Math.Abs(p16 - 0.476) < 0.01 && Math.Abs(p8 - 0.667) < 0.01, bad.Count > 0 ? string.Join(" / ", bad.Take(6)) : $"여유 {p32:0.00} · {p16:0.00} · {p8:0.00} · 한 점 {pickUs:0}µs · 탁자 카드 {cardUs:0}µs (디딤호 물체 {st.Props.Count})");
        }

        // ── V2 올림 = 누름 (세 점 · 떨어져 나간 조각 위 칸) ──
        {
            var bad = new List<string>();
            var pts = new List<(string, double, double, PickKind)> { ("컵", ux, uy, PickKind.Prop), ("문", Mid(L.Doors[door].Cell).Item1, Mid(L.Doors[door].Cell).Item2, PickKind.Door) };
            if (!double.IsNaN(bare.x)) pts.Add(("탁자", bare.x, bare.y, PickKind.Furniture));
            bool outlined = true;
            foreach (var (what, x, y, kind) in pts)
            {
                show.SetHoverAt(x, y, p32);
                var f = show.Render(); show.Recycle(f.Img);
                var hover = f.HoverLook;
                show.Inspect(hover); var a = show.Look;
                show.LookClose(); show.InspectAt(x, y, p32); var b = show.Look;
                var g = show.Render(); show.Recycle(g.Img);
                outlined &= f.Outlines.Any(o => !o.Picked) && g.Outlines.Any(o => o.Picked) && g.CardIs == CardKind.Thing && g.Card.Length >= 3;
                if (hover.Kind != kind || a != hover || b != hover || f.HoverLookName.Length == 0) bad.Add($"{what}: 올림 {hover} '{f.HoverLookName}' · 누름 {a} · 그 자리 {b}");
            }
            show.LookClose();
            // 떨어져 나간 조각 위: 그려진 자리를 거꾸로 돌려 그 몸의 칸을 찾는다
            var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var s = Worlds.Full(lay, 2);
            var sst = s.Get<Structure>();
            s.Run(25);
            for (int y = 2; y <= 4; y++) sst.Destroy(s, Worlds.At(lay, 10, y), 0);
            sst.Destroy(s, Worlds.At(lay, 20, 3), 0);
            s.Run(30);
            var frag = new LiveShow(s, "아령") { SpeedOverride = 1, Follow = false };
            int inFrag = Worlds.At(lay, 16, 3);
            var (qx, qy) = frag.CellDrawn(inFrag);
            int under = frag.CellUnder(qx + 0.1, qy - 0.1), left = frag.CellUnder(lay.X(inFrag) + 0.5, lay.Y(inFrag) + 0.5);
            var fp = frag.PickAt(qx + 0.1, qy - 0.1, p32, Array.Empty<AgentSpot>());
            bool moved = sst.BodyOf(inFrag) == 1 && Math.Abs(qx - (lay.X(inFrag) + 0.5)) + Math.Abs(qy - (lay.Y(inFrag) + 0.5)) > 2;
            if (!moved || under != inFrag || left == inFrag || fp.Kind != PickKind.Floor || fp.Id != inFrag) bad.Add($"조각: 몸 {sst.BodyOf(inFrag)} · 그려진 칸 {under} (바람 {inFrag}) · 옛 자리 {left} · 누름 {fp}");
            Check("살펴보기 V2 올림 = 누름: 컵 · 문 · 탁자에서 올린 열쇠로 살펴본 것 = 그 자리로 다시 셈한 것 · 올린 것은 흰 테두리 · 고른 것은 노랑 테두리와 물건 카드 · 떨어져 나간 조각 위는 그려진 자세를 거꾸로 돌려 그 몸의 칸",
                bad.Count == 0 && outlined, bad.Count > 0 ? string.Join(" / ", bad) : $"테두리 {outlined}");
        }

        // ── V3 카드 줄 (세계에서 읽기만 — 차림은 시험 세계에 직접) ──
        {
            var bad = new List<string>();
            string[] CardOf(PickKey k) { show.Inspect(k); var f = show.Render(); show.Recycle(f.Img); return f.CardIs == CardKind.Thing ? f.Card : Array.Empty<string>(); }
            bool Has(string[] card, params string[] words) => words.All(x => card.Any(l => l.Contains(x)));
            void Lint(string what, string[] card) { foreach (var l in card) if (LookBad(l) is { } why) bad.Add($"{what} '{l}' {why}"); }
            // 컵: 물 200mL · 깨짐 피로 30% · 식탁 위
            int ci = st.IndexOf(cup.Id);
            var cp = st.Props[ci]; cp.Liquid = 200f / 18; st.Props[ci] = cp;
            cp.Hurt = 1; cp.Hurt = (float)(0.3 / st.HurtShare(cp)); st.Props[ci] = cp;   // 깨짐 피로 30%
            var cupCard = CardOf(new PickKey(PickKind.Prop, cup.Id));
            string tableName = w.Tables.Equip(table.Type).Name;
            if (cupCard.Length < 3 || cupCard[0] != "컵" || !cupCard[1].Contains("유리") || !Has(cupCard, "kg", "물 200mL", "깨짐 피로 30%", tableName + " 위")) bad.Add("컵: " + string.Join(" | ", cupCard));
            Lint("컵", cupCard);
            // 문: 잠김 · 낌 · 기압 차
            int dc = L.Doors[door].Cell;
            st.DoorLock[door] = w.Tick + Time.Minutes(5); st.DoorJam[door] = 1;
            st.DamageCause[dc] = w.Causes.Add(CauseKind.Setup, "파편이 문틀을 찌그러뜨렸다", w.Tick, dc);
            var doorCard = CardOf(new PickKey(PickKind.Door, door));
            if (doorCard.Length < 3 || !doorCard[0].Contains("↔") || !Has(doorCard, "열림", "주컴퓨터가 잠갔다", "끼었다", "파편이 문틀을 찌그러뜨렸다", "기압 차")) bad.Add("문: " + string.Join(" | ", doorCard));
            Lint("문", doorCard);
            st.DoorJam[door] = 0; st.DoorLock[door] = 0;
            // 벽: 금 · 피로 · 부식 · 구멍
            int hull = Enumerable.Range(0, L.N).First(c => L.Kind[c] == CellKind.Wall && L.Hull[c] && L.DoorAt[c] < 0);
            st.Crack[hull] = 0.03f; st.CrackCause[hull] = w.Causes.Add(CauseKind.Setup, "만들 때의 흠", 0, hull);
            st.Fatigue[hull] = 0.4f; st.Corrode[hull] = 0.1f;
            var fd = w.Get<Field>(); fd.Hole[hull] = 0.002f;
            var wallCard = CardOf(new PickKey(PickKind.Wall, hull));
            if (wallCard.Length < 3 || !wallCard[0].Contains("외판") || !wallCard[1].Contains("10mm") || !Has(wallCard, "금 6cm", "만들 때의 흠", "피로 40%", "부식 10%", "구멍")) bad.Add("벽: " + string.Join(" | ", wallCard));
            Lint("벽", wallCard);
            fd.Hole[hull] = 0; st.Crack[hull] = 0;
            // 고인 물: 원인 글
            int wet = floor;
            int wc = w.Causes.Add(CauseKind.Leak, "물관 이음매가 샜다", w.Tick, wet);
            fd.AddStore(wet, w.Tables.sWater, 555, wc);
            var wetCard = CardOf(new PickKey(PickKind.Floor, wet));
            if (wetCard.Length < 3 || !wetCard[0].StartsWith("고인 물") || !Has(wetCard, "10mm", "물관 이음매가 샜다", "기압", "주컴퓨터")) bad.Add("고인 물: " + string.Join(" | ", wetCard));
            Lint("고인 물", wetCard);
            // 설비: 전기 · 낡음 · 고장 까닭 (함장이 일으킨 고장)
            var net = w.Get<Network>();
            var dev = net.Devices.First(d => d.Furniture >= 0 && d.Has(Part.Electric) && d.Def.Role == "o2gen");
            w.Get<Hand>().Enqueue(new HandOrder { Kind = HandKind.Fault, Size = 1, Cell = dev.Cell, Part = (byte)Part.Electric });
            for (int i = 0; i < 40 && !net.IsBroken(dev.Id, Part.Electric); i++) show.Advance(1);   // 숨은 한계를 넘긴 낡음 — 1분 안에 고장
            var devCard = CardOf(new PickKey(PickKind.Furniture, dev.Furniture));
            if (devCard.Length < 4 || !Has(devCard, "전기", "낡음", "고장", "함장", "조절 센서")) bad.Add("설비: " + string.Join(" | ", devCard));
            Lint("설비", devCard);
            show.LookClose();
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") foreach (var cd in new[] { cupCard, doorCard, wallCard, wetCard, devCard }) Console.WriteLine("    " + string.Join("\n    ", cd) + "\n");
            Check("살펴보기 V3 카드 줄: 컵 (유리 · 무게 · 물 200mL · 깨짐 피로 · 식탁 위) · 문 (열림 · 주컴퓨터가 잠갔다 · 끼었다와 그 까닭 · 기압 차) · 외판 (강철 10mm · 금 · 만들 때의 흠 · 피로 · 부식 · 구멍) · 고인 물 (깊이 · 샌 까닭 · 방 한 줄 · 주컴퓨터가 아는 이 방) · 설비 (전기 · 낡음 · 고장 까닭 '함장' · 산소 조절 센서) · 개발 말 0 (로마자는 단위만)",
                bad.Count == 0, string.Join(" / ", bad.Take(4)));
        }

        // ── 카드의 [사고 ›] (U3에서 미룸 · §5.4): 사고 판을 열고 그 물건에 맞는 첫 갈래를 그 자리에 겨눈다 · 겨눔 미리 보기가 되는 칸 · 바깥 칸은 옅게 ──
        {
            show.Inspect(new PickKey(PickKind.Door, door));
            var f1 = show.Render(); show.Recycle(f1.Img);
            bool from = f1.LookHandOk && show.HandFrom();
            var f2 = show.Render(); show.Recycle(f2.Img);
            bool doorAim = from && show.Panel == PanelKind.Hand && f2.Armed == HandKind.DoorJam && f2.Aim.Ok && f2.Aim.Cell == L.Doors[door].Cell;
            show.Disarm();
            show.Inspect(new PickKey(PickKind.Prop, cup.Id));
            var f3 = show.Render(); show.Recycle(f3.Img);
            bool shove = f3.LookHandOk && show.HandFrom() && show.Armed == HandKind.Shove;
            show.Disarm(); show.ClosePanel();
            // 손이 밀려 있으면 (기다리는 손 가득) 옅게 · 까닭
            var hd = show.Sim.Get<Hand>();
            show.Inspect(new PickKey(PickKind.Door, door));
            for (int i = 0; i < Hand.MaxWaiting; i++) hd.Waiting.Add(new HandOrder { Kind = HandKind.O2Drift, Cell = -1, Seq = 9000 + i });
            var f4 = show.Render(); show.Recycle(f4.Img);
            bool wallOk = !f4.LookHandOk && f4.LookHandWhy.StartsWith("손이 밀려") && !show.HandFrom() && show.Armed == HandKind.None;
            hd.Waiting.Clear(); show.LookClose();
            Check("살펴보기 [사고 ›]: 문 카드에서 누르면 사고 판이 열리고 '문 끼임'을 그 문에 겨눈다 (겨눔 미리 보기가 되는 칸) · 컵은 '밀어 던지기' · 손이 밀려 있으면 옅게 · 까닭 (누르면 알림만)",
                doorAim && shove && wallOk, $"문 {doorAim} (판 {show.Panel} · 겨눔 {f2.Armed} · 미리 보기 {f2.Aim.Ok} '{f2.Aim.Why}') · 컵 {shove} · 밀림 {wallOk} '{f4.LookHandWhy}'");
        }

        // ── V4 화면만: 살펴보기 · 올림을 켠 화면과 쌍둥이의 찍기 바이트가 같다 ──
        {
            var a = LiveShow.Crewed("Didim", 33);
            var bs = new Sim(a.Sim.Setup); bs.Load(a.Sim.Save());   // 쌍둥이 (같은 찍기에서)
            var b = new LiveShow(bs, a.ShipName);
            a.FrameBudget = b.FrameBudget = 1000; a.SpeedOverride = b.SpeedOverride = 1;
            var al = a.Sim.Layout;
            int k = 0;
            for (int i = 0; i < 12; i++)
            {
                int c = (i * 977 + 13) % al.N;
                a.SetHoverAt(al.X(c) + 0.5, al.Y(c) + 0.5, p16);
                if (i % 3 == 0) a.InspectAt(al.X(c) + 0.3, al.Y(c) + 0.7, p32);
                var f = a.Render(); a.Recycle(f.Img); k += f.Card.Length;
                a.Advance(1); b.Advance(1);
            }
            bool same = a.Sim.Tick == b.Sim.Tick && a.Sim.Save().AsSpan().SequenceEqual(b.Sim.Save());
            Check("살펴보기 V4 화면만: 올림 · 살펴보기 · 카드를 켠 화면과 켜지 않은 쌍둥이 세계의 찍기 바이트가 같다 (살펴보기는 세계를 읽기만 한다)",
                same && k > 0, $"틱 {a.Sim.Tick}/{b.Sim.Tick} · 같음 {same} · 카드 줄 {k}");
        }
    }
}
