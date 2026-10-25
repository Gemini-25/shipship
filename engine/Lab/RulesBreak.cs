using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>
/// 깨짐 규칙 (PH8 — docs/PHYSICS.md §5 · §6-5 · §10 묶음 '깨짐'): 유리컵 0.75m는 대개 깨지고 0.3m는 드물다 · 고분자는 깨지지 않고 강철은 찌그러진다 ·
/// 깨진 조각 색 = 원래 · 담긴 물은 그 칸 바닥으로 (원인 = 깨짐) · 쨍그랑이 깨지는 장에 · 떨군 공구 상자는 '쾅' 믿음 · '큰 소리' 사건을 만들지 않는다 · 폭발 곁 소화기 · 탁자 (PH10)는 이음매에서 갈라진다.
/// </summary>
public static partial class Program
{
    // 침실 (침대 e · 배전반 p · 전지 b) · 긴 식당
    private const string BreakShip = @"
##############
#e...........#
#............#
#pb..........#
##############";

    private static void BreakRules()
    {
        if (!Do("깨짐")) return;
        Console.WriteLine("\n[깨짐 — 유리 · 고분자 · 강철 · 조각 색 · 쏟음 · 소리 · 이음매]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var t = Tables.Default;
        int Item(string name) => t.People.Items.Keys.ToList().IndexOf(name);

        // ── 유리컵: 0.75m (탁자 높이)에서 떨어지면 대개 깨지고 · 0.3m는 드물다 (시드 40) ──
        {
            double Share(float h)
            {
                int n = 0;
                for (ulong seed = 1; seed <= 40; seed++)
                {
                    var s = PropWorld(1, 100 + seed);
                    var st = s.Get<Structure>();
                    st.PutItem(Item("컵"), 0, s.Layout.Index(6, 4), h);
                    s.Step(); s.Step();
                    if (st.Shattered > 0) n++;
                }
                return n / 40.0;
            }
            double hi = Share(0.75f), lo = Share(0.3f);
            Check("깨짐: 강철 바닥에 떨어진 유리컵 — 0.75m (탁자 높이)는 대개 깨지고 · 0.3m는 드물다 (시드 40)", hi > 0.6 && lo < 0.2, $"0.75m 깨진 몫 {hi:0.00} · 0.3m {lo:0.00}");
        }

        // ── 고분자는 깨지지 않는다 (금이 쌓인다) · 강철은 찌그러진다 ──
        {
            (long broke, float hurt, uint dent) Throw(string name, double v)
            {
                var s = PropWorld(0, 4);
                var st = s.Get<Structure>();
                int id = Slide(s, name, 9, 4.5, v, 0);
                for (int k = 0; k < 2; k++) s.Step();
                int i = st.IndexOf(id);
                return (st.Shattered, i >= 0 ? st.Props[i].Hurt : -1, i >= 0 ? st.Props[i].Dent0 : 0);
            }
            var poly = Throw("식량 상자", 10); var steel = Throw("기계 부품", 40);
            Check("깨짐: 벽에 부딪힌 고분자 식량 상자 (10m/s)는 깨지지 않고 맞음이 쌓인다 · 강철 기계 부품 (40m/s)은 깨지지 않고 찌그러진다",
                poly.broke == 0 && poly.hurt > 0 && poly.dent == 0 && steel.broke == 0 && steel.dent != 0,
                $"고분자: 깨짐 {poly.broke} · 맞음 {poly.hurt:0.0}J/kg · 찌그러짐 {poly.dent:x} / 강철: 깨짐 {steel.broke} · 찌그러짐 반지름 {(steel.dent >> 16) & 0xFF}도트");
        }

        // ── 깨진 조각의 색은 원래 그 물건의 색이다 (원래 그림을 잘라 그린다) ──
        {
            var s = PropWorld(0, 6);
            var st = s.Get<Structure>();
            Slide(s, "접시", 9, 4.5, 12, 0);
            for (int k = 0; k < 2; k++) s.Step();
            // 원래 접시 색 (#e0e0dc)의 면 단 여섯 (0 외곽 ~ 5 빛 — DotStack.Ramp) 밖의 색이 조각에 없다 · 깨진 면은 빛 단 (반짝임)
            var baseCol = Rgba.Hex("#e0e0dc");
            var ramp = new[] { 0.30f, 0.50f, 0.68f, 0.82f, 1f, 1.22f }.Select(k => baseCol.Scale(k)).ToHashSet();
            var shine = baseCol.Scale(1.22f);
            static IEnumerable<Rgba> Colors(Img img) { for (int i = 0; i < img.Px.Length; i += 4) if (img.Px[i + 3] > 0) yield return new Rgba(img.Px[i], img.Px[i + 1], img.Px[i + 2], img.Px[i + 3]); }
            bool wholeOk = Colors(DotStack.Draw(DotForge.Make(t, "접시"), 0).Img).All(ramp.Contains);
            var pieces = st.Props.Where(p => p.Kind == 2 && p.Piece > 0).ToList();
            int same = pieces.Count(p => Colors(DotStack.Draw(st.PropForm(p), 5).Img).All(ramp.Contains));
            int shiny = pieces.Count(p => Colors(DotStack.Draw(st.PropForm(p), 5).Img).Contains(shine));
            Check("깨짐: 깨진 조각의 색 = 원래 접시의 색 (원래 그림을 가면으로 잘라 그린다 · 금 간 면은 반짝인다)",
                st.Shattered >= 1 && pieces.Count >= 2 && wholeOk && same == pieces.Count && shiny > 0, $"깨짐 {st.Shattered} · 조각 {pieces.Count} · 원래 색 단 안 {same} · 반짝이는 조각 {shiny}");
        }

        // ── 담긴 물 · 소리: 공기가 있는 방에서 0.75m 떨어진 물 든 컵이 깨지면 그 칸 바닥이 젖고 (원인 = 깨짐) · 쨍그랑이 깨지는 장에 난다 ──
        {
            var legend = new Dictionary<char, string> { ['e'] = "Bed", ['p'] = "PowerPanel", ['b'] = "Battery" };
            var lay = Worlds.Ascii(BreakShip, legend);
            Sim? s = null; Fact fact = default;
            for (ulong seed = 1; seed <= 10 && fact.Kind != FactKind.Shattered; seed++)
            {
                s = Worlds.Full(lay, seed);
                s.Get<Structure>().PutItem(Item("컵"), 0, Worlds.At(lay, 8, 1), 0.75f, 11);
                for (int k = 0; k < 3 && fact.Kind != FactKind.Shattered; k++) { s.Step(); fact = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Shattered); }
            }
            var f = s!.Get<Field>(); var snd = s.Get<Sound>();
            var wet = Enumerable.Range(0, lay.N).Where(c => f.Stored(f.T.sWater, c) > 1).ToList();
            double water = wet.Sum(c => f.Stored(f.T.sWater, c));
            var crash = snd.LogSince(0).Where(e => e.Kind == SoundKind.Shatter).ToList();
            bool inFrame = crash.Any(e => e.Tick == fact.Tick && Math.Abs(e.Sub - fact.Sub) <= 64);
            if (Debug) foreach (var e in snd.LogSince(0)) Console.WriteLine($"      소리 {e.Kind} {e.Db:0}dB 틱 {e.Tick} · 때 {e.Sub} (깨짐 {fact.Tick} · {fact.Sub})");
            Check("깨짐: 물 든 유리컵 (11몰)이 깨지면 그 칸 바닥이 젖고 고인 물의 원인 = 깨짐 · 쨍그랑이 깨지는 장 (그 틱 · 두 장 안)에 난다",
                fact.Kind == FactKind.Shattered && water > 9 && wet.All(c => f.WetCause[c] == fact.Cause) && inFrame && s.Causes.Story(fact.Cause).Contains("깨졌다"),
                $"깨짐 {(fact.Kind == FactKind.Shattered ? $"틱 {fact.Tick}" : "없음")} · 바닥 물 {water:0.0}몰 ({wet.Count}칸) · 쨍그랑 {crash.Count} ({(crash.Count > 0 ? $"{crash[0].Db:0}dB · 때 {crash[0].Sub}/{fact.Sub}" : "-")}) · \"{(fact.Kind == FactKind.Shattered ? s.Causes.Story(fact.Cause) : "")}\"");
        }

        // ── 떨군 공구 상자 (1m · 49J)는 덜그럭 (생활 소리) — 마음의 '쾅' 믿음 · 주컴퓨터 '큰 소리' 사건을 만들지 않는다 ──
        {
            var legend = new Dictionary<char, string> { ['e'] = "Bed", ['p'] = "PowerPanel", ['b'] = "Battery" };
            var lay = Worlds.Ascii(BreakShip, legend);
            var s = Worlds.Full(lay, 3);
            var m = s.Get<Mind>(); var sm = s.Get<ShipMind>(); var snd = s.Get<Sound>();
            int p = m.AddPerson(s, Worlds.At(lay, 4, 2));
            s.Run(2);
            long mic0 = sm.MicHeard, log0 = snd.Logged;
            s.Get<Structure>().PutItem(Item("공구 상자"), 0, Worlds.At(lay, 7, 2), 1.0f);
            s.Run(3);
            var knock = snd.LogSince(log0).Where(e => e.Kind == SoundKind.Knock).ToList();
            bool bang = m[p].Beliefs.Any(b => b.Kind == BeliefKind.Bang) || snd.LogSince(log0).Any(e => e.Kind == SoundKind.Bang);
            Check("깨짐: 1m에서 떨군 공구 상자 (5kg · 49J)는 덜그럭 (70dB 남짓 — 생활 소리)으로 들리고 · 마음의 '쾅' 믿음도 · 주컴퓨터 '큰 소리' 사건도 만들지 않는다",
                knock.Count > 0 && knock.Max(e => e.Db) is > 60 and < 80 && !bang && sm.MicHeard == mic0,
                $"덜그럭 {knock.Count} ({(knock.Count > 0 ? $"{knock.Max(e => e.Db):0}dB" : "-")}) · 쾅 {bang} · 마이크 {sm.MicHeard - mic0}");
        }

        // ── 폭발 곁 소화기는 이음매 (노즐)에서 갈라진다 — 강철 몸은 그대로 ──
        {
            var s = PropWorld(1, 8);
            var st = s.Get<Structure>();
            int id = Slide(s, "소화기", 8.5, 4.5, 0, 0);
            s.Step();
            st.Blast(s, s.Layout.Index(7, 4), 2e6, s.Causes.Add(CauseKind.Setup, "배터리가 터졌다", s.Tick));
            for (int k = 0; k < 3; k++) s.Step();
            var pieces = st.Props.Where(q => q.Kind == 2).ToList();
            int parts = pieces.Select(q => st.Masks[q.Piece - 1].Part).Distinct().Count();
            var fact = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Shattered);
            Check("깨짐: 폭발 곁 소화기가 벽에 부딪히면 이음매 (노즐)에서 두 조각으로 갈라진다 — 부위가 다르다 · 원인 사슬이 폭발까지",
                st.IndexOf(id) < 0 && pieces.Count == 2 && parts == 2 && fact.Kind == FactKind.Shattered && s.Causes.Story(fact.Cause).Contains("배터리가 터졌다"),
                $"조각 {pieces.Count} · 부위 {parts} · \"{(fact.Kind == FactKind.Shattered ? s.Causes.Story(fact.Cause) : "-")}\"");
        }

        // ── 폭발 곁 탁자는 이음매에서 다리가 갈라진다 (PH10 — 가구가 물체가 되어 PH8의 소화기 대신 원래 뜻으로): 폭풍에 밀린 탁자가 벽에 부딪혀 다리 (부위 1)와 윗판 (부위 0)이 갈린다 ──
        {
            const string art = "\n##########\n#........#\n#.....tt.#\n#.....tt.#\n#........#\n##########";
            var s = PropWorld(1, 8, art, new Dictionary<char, string> { ['t'] = "Table" });
            var st = s.Get<Structure>(); var L = s.Layout;
            int tx = L.Furniture[0].Cells.Min(L.X), ty = L.Furniture[0].Cells.Min(L.Y);
            s.Step();
            st.Blast(s, L.Index(tx - 2, ty), 6e6, s.Causes.Add(CauseKind.Setup, "배터리가 터졌다", s.Tick));
            for (int k = 0; k < 4; k++) s.Step();
            var legs = st.Props.Where(q => q.Kind == 2 && (q.Flags & Structure.FurnPieceFlag) != 0 && q.What == 0).ToList();
            int parts = legs.Select(q => st.Masks[q.Piece - 1].Part).Distinct().Count();
            var fact = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Shattered);
            string story = fact.Kind == FactKind.Shattered ? s.Causes.Story(fact.Cause) : "-";
            Check("깨짐: 폭발 곁 탁자가 밀려 벽에 부딪히면 이음매에서 다리와 윗판이 갈라진다 — 조각은 탁자 꼴 (가구 조각) · 원인 사슬이 폭발까지",
                st.FurnPlaced[0] == 0 && legs.Count >= 2 && parts == 2 && story.Contains("배터리가 터졌다"),
                $"조각 {legs.Count} · 부위 {parts} · \"{story}\"");
        }

        // ── 칸 가루는 넓은 보기 (16 · 8도트)에도 보인다 (검토가 찾음 — 예전엔 32도트에만: 굽는 덩어리는 칸 덮개 앞에서 멈추고 · 장마다 덮개는 가루를 읽지 않았다) ──
        {
            var s = PropWorld(1, 1, BreakShip, new Dictionary<char, string> { ['e'] = "Bed", ['p'] = "PowerPanel", ['b'] = "Battery" });
            var st = s.Get<Structure>(); var lay = s.Layout;
            var p = new Painter(lay);
            byte glass = (byte)t.Materials.FindIndex(m => m.Id == "glass");
            var grit = Enumerable.Range(0, lay.N).Where(c => lay.Kind[c] == CellKind.Floor && lay.FurnitureAt[c] < 0).Take(18).ToList();
            var sizes = new[] { 32, 16, 8 };
            Img Draw(int D) => D == 32 ? p.Frame(s, 0, 0, lay.W, lay.H, 7) : p.FrameFar(s, 0, 0, lay.W, lay.H, 7, D);
            var before = sizes.Select(Draw).ToArray();
            foreach (int c in grit) { st.Grit[c] = 0.12f; st.GritMat[c] = glass; }
            var after = sizes.Select(Draw).ToArray();
            bool Changed(Img a, Img b, int D, int c)
            {
                for (int y = lay.Y(c) * D; y < lay.Y(c) * D + D; y++)
                    for (int x = lay.X(c) * D; x < lay.X(c) * D + D; x++)
                        for (int q = 0; q < 4; q++) if (a.Px[(y * a.W + x) * 4 + q] != b.Px[(y * b.W + x) * 4 + q]) return true;
                return false;
            }
            if (Debug) { Directory.CreateDirectory("build/shots"); for (int i = 0; i < sizes.Length; i++) Png.WriteRgba($"build/shots/grit-{sizes[i]}.png", after[i].W, after[i].H, after[i].Px); }
            var shown = sizes.Select((D, i) => grit.Count(c => Changed(before[i], after[i], D, c))).ToArray();
            var still = sizes.Select((D, i) => Enumerable.Range(0, lay.N).Count(c => !grit.Contains(c) && Changed(before[i], after[i], D, c))).ToArray();
            Check("깨짐: 칸 가루 (깨진 유리)는 넓은 보기 16 · 8도트에도 보인다 — 가루 칸마다 그림이 바뀌고 · 가루 없는 칸은 그대로 (32도트처럼)",
                glass < 255 && grit.Count == 18 && shown.All(n => n == grit.Count) && still.All(n => n == 0),
                $"가루 칸 {grit.Count} · 바뀐 칸 32 · 16 · 8도트 {string.Join(" · ", shown)} · 가루 없이 바뀐 칸 {string.Join(" · ", still)}");
        }
    }
}
