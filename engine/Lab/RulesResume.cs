using System.Text.Json.Nodes;
using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>
/// 이어 보기 규칙 (docs/OBSERVER.md §16 S6 · S7 — 묶음 '이어 보기'): 최근 셋을 돌려 쓴다 · 쓰다 멈춘 임시 파일은 읽지 않는다 · 가장 새것이 깨졌으면 그다음 ·
/// 판이 다르면 세계를 읽지 않고 '이어 볼 수 없다' · 모르는 마디는 건너뛰고 없는 마디는 빈 채로.
/// </summary>
public static partial class Program
{
    private static void ResumeRules()
    {
        if (!Do("이어 보기")) return;
        Console.WriteLine("\n[이어 보기 — 돌려 쓰는 저장 셋 · 판 · 마디]");
        string dir = TempVoyages();
        try
        {
            if (Environment.GetEnvironmentVariable("SAVE_BENCH") is { Length: > 0 } ship)
            {
                var big = LiveShow.Crewed(ship, 5); big.FastForward(60);
                for (int k = 0; k < 4; k++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew(); var bb = big.SaveBundle(SaveKind.Daily); double ms = sw.Elapsed.TotalMilliseconds;
                    sw.Restart(); var bytes = bb.ToBytes(); double packMs = sw.Elapsed.TotalMilliseconds;
                    sw.Restart(); var back = LiveShow.Resume(VoyageBundle.FromBytes(bytes, out _)!); double resMs = sw.Elapsed.TotalMilliseconds;
                    Console.WriteLine($"    (저장 재기) {ship} 묶음 짓기 {ms:0.0}ms · 세계 {bb.World.Length / 1e6:0.00}MB · 연출 {bb.Record!.Length / 1e3:0}KB (미리 보기 세계 {(big.Dir.Ahead != null ? "있음" : "없음")}) · 손 {bb.Hands!.Length}B · 화면 {bb.Screen!.Length / 1e3:0}KB · 압축 {packMs:0}ms → {bytes.Length / 1e6:0.00}MB · 이어 보기 {resMs:0}ms");
                    big.FastForward(20);
                }
            }
            var show = new LiveShow(HandShip(53), "디딤호");
            var store = new VoyageStore(dir);
            // S6: 저장 넷 → 셋이 남고 가장 오래된 것이 지워진다 · 쓰다 멈춘 임시 파일은 읽지 않는다 · 가장 새것이 깨졌으면 그다음
            {
                var ticks = new List<long>(); VoyageMeta? meta = null;
                for (int i = 0; i < 4; i++) { show.FastForward(3); meta = store.Write(show.SaveBundle(SaveKind.Daily)); ticks.Add(show.Sim.Tick); }
                string folder = store.FolderOf(meta!);
                var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "save-*.bin").Select(Path.GetFileName).OrderBy(x => x).ToList() : new();
                var read = store.Read(meta!).Select(x => x.bundle?.Meta.Tick ?? -1).ToList();
                var latest = store.Latest();
                bool three = files.Count == 3 && read.SequenceEqual(new[] { ticks[3], ticks[2], ticks[1] }) && latest?.Tick == ticks[3];
                // 쓰다 멈춘 임시 파일 (다음 칸 이름에 반쯤 쓴 바이트) — 읽지 않는다
                string next = store.Rotate(meta!);
                var half = show.SaveBundle(SaveKind.Daily).ToBytes();
                if (Directory.Exists(folder)) File.WriteAllBytes(Path.Combine(folder, $"save-{next}.bin.tmp"), half.AsSpan(0, half.Length / 2).ToArray());
                var r1 = latest != null ? LiveShow.Resume(store, latest, out _) : null;
                // 가장 새것을 깨뜨린다 (끝을 자른다) — 그다음 것으로 이어 본다
                string newest = latest != null ? Path.Combine(folder, $"save-{latest.Slot}.bin") : "";
                if (File.Exists(newest)) { var raw = File.ReadAllBytes(newest); File.WriteAllBytes(newest, raw.AsSpan(0, raw.Length * 2 / 3).ToArray()); }
                var r2 = latest != null ? LiveShow.Resume(store, latest, out _) : null;
                Check("이어 보기 S6: 저장 넷 → 돌려 쓰는 셋이 남고 가장 오래된 것이 지워진다 · 쓰다 멈춘 임시 파일은 읽지 않는다 · 가장 새것이 깨졌으면 그다음 것으로 이어 본다",
                    three && r1?.Sim.Tick == ticks[3] && r2?.Sim.Tick == ticks[2],
                    $"남은 파일 {string.Join(" ", files)} · 읽은 차례 {string.Join(" ", read)} (쓴 틱 {string.Join(" ", ticks)}) · 카드 {latest?.Tick} · 임시 파일 곁 {r1?.Sim.Tick} · 깨진 뒤 {r2?.Sim.Tick}");
            }
            // S6 (다섯 항해): 항해 폴더는 다섯까지 — 여섯째 항해를 쓰면 가장 오래된 항해가 지워진다 (번호 창이 미리 알린다) · 남은 항해는 그대로
            {
                string d2 = dir + "-five"; var five = new VoyageStore(d2);
                try
                {
                    var b0 = show.SaveBundle(SaveKind.Quit); var metas = new List<string>(); string? warned = null;
                    for (int i = 0; i < 6; i++)
                    {
                        b0.Meta.Voyage = 5000 + i;
                        if (i == 5) warned = five.WouldPrune(b0.Meta.Folder)?.Folder;
                        metas.Add(five.Write(b0).Folder);
                        System.Threading.Thread.Sleep(15);   // 카드의 쓴 때가 갈리게
                    }
                    var left = five.List().Select(m => m.Folder).ToList();
                    Check("이어 보기 S6 (다섯 항해): 여섯째 항해를 쓰면 가장 오래된 항해가 지워지고 (미리 알 수 있다) 나머지 다섯은 그대로 · 목록은 최근 것부터",
                        left.Count == 5 && !left.Contains(metas[0]) && warned == metas[0] && left[0] == metas[5], $"남은 {string.Join(" ", left)} · 미리 알림 {warned}");
                }
                finally { try { if (Directory.Exists(d2)) Directory.Delete(d2, true); } catch (IOException) { } }
            }
            // S7: 판이 다르면 '이어 볼 수 없다' 글 · 세계를 읽지 않는다 · 예외 없음 / 모르는 마디는 건너뛰고 없는 마디는 빈 채로
            {
                var meta = store.Write(show.SaveBundle(SaveKind.Quit));
                string card = Path.Combine(store.FolderOf(meta), "card.json");
                bool threw = false; string why = "", listed = ""; LiveShow? old = null; int reads0 = store.BinReads, reads1 = -1;
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(card))!;
                    node["WorldVersion"] = Sim.SaveVersion - 1;
                    File.WriteAllText(card, node.ToJsonString());
                    var m = store.List().FirstOrDefault(x => x.Folder == meta.Folder);
                    listed = m != null ? VoyageStore.Why(m) : "목록에 없다";
                    old = m != null ? LiveShow.Resume(store, m, out why) : null;
                    reads1 = store.BinReads;
                }
                catch (Exception e) { threw = true; why = e.Message; }
                // 모르는 마디 · 없는 마디
                var bundle = show.SaveBundle(SaveKind.Quit);
                bundle.Other["뒤 판의 마디"] = new byte[] { 1, 2, 3 };
                bundle.Screen = null;
                var back = VoyageBundle.FromBytes(bundle.ToBytes(), out string bwhy);
                LiveShow? fresh = null; bool threw2 = false;
                try { fresh = back != null ? LiveShow.Resume(back) : null; } catch (Exception e) { threw2 = true; bwhy = e.Message; }
                bool known = back != null && back.Other.TryGetValue("뒤 판의 마디", out var ob) && ob.Length == 3 && back.Screen == null && back.Record != null && back.Hands != null;
                bool text = why.Contains("앞선 판") && why.Contains("이어 볼 수 없다") && listed == why && TextLint.Bad(why) == null;
                Check("이어 보기 S7: 카드의 세계 판 ≠ 지금 판 → '이 항해는 앞선 판에서 남겼다 — 지금 판에서는 이어 볼 수 없다' · 세계를 읽지 않는다 · 예외 없음 / 모르는 마디는 건너뛰고 · 없는 마디는 빈 채로 이어 본다",
                    !threw && old == null && text && reads1 == reads0 && known && !threw2 && fresh != null && fresh.Sim.Tick == show.Sim.Tick,
                    $"글 '{why}' · 목록 글 같음 {listed == why} · 세계 읽기 {reads1 - reads0} · 예외 {threw}/{threw2} · 모르는 마디 남음 {known} · 이어 봄 {fresh?.Sim.Tick}/{show.Sim.Tick} {bwhy}");
            }
        }
        finally { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { } }
    }

    /// <summary>
    /// 항해 번호 (docs/OBSERVER.md §16 S4 · S5 · M5 — 묶음 '항해 번호'): 같은 번호 = 같은 항해 · 다른 번호 = 다른 사람들 · 뽑기 1000 ~ 99999 · 확인용 띄우기 18 ·
    /// 번호 창 · 숫자판 · '지난번까지' 카드 · 처음 화면 카드의 자리표 (여섯 창 × 사람 × 글 100 · 150% — 겹침 0 · 밖 0 · 누를 자리 크기 · 뒤로 · 글자).
    /// </summary>
    private static void VoyageNumberRules()
    {
        if (!Do("항해 번호")) return;
        Console.WriteLine("\n[항해 번호 — 같은 번호 같은 항해 · 뽑기 · 번호 창 · 이어 보기 카드]");
        // S4: 같은 번호 = 같은 사람 이름 · 300틱 바이트 / 다른 번호 = 첫 사람이 다르다 · 위 줄에 '항해 n'
        {
            var a = LiveShow.Crewed("Didim", 4127); var b = LiveShow.Crewed("Didim", 4127); var c = LiveShow.Crewed("Didim", 4128);
            string Names(LiveShow x) { var m = x.Sim.Get<Mind>(); return string.Join(" ", m.Agents.Where(g => g.IsPerson).Select(g => m.NameOf(g.Id))); }
            string na = Names(a), nb = Names(b), nc = Names(c);
            a.Sim.Run(300); b.Sim.Run(300);
            bool same = a.Sim.Save().AsSpan().SequenceEqual(b.Sim.Save());
            // 다른 번호: 첫 사람의 사람됨 (성격 다섯 축 · 인생 목표 · 솜씨)이 다르다 — 이름은 차례대로 붙는다 (이름표 · 지문 그대로)
            string Who(LiveShow x) { var g = x.Sim.Get<Mind>().Agents.First(q => q.IsPerson).C; return $"{g.Open:0.00}/{g.Consc:0.00}/{g.Extra:0.00}/{g.Agree:0.00}/{g.Neuro:0.00}/{g.Aim}/{g.SkRepair:0.00}"; }
            string wa = Who(a), wc = Who(c);
            bool first = wa != wc;
            string top = a.Status();
            Check("항해 번호 S4: 같은 배 · 같은 번호 (4127)면 같은 사람들 · 300틱 찍기 바이트가 같다 / 다른 번호 (4128)면 첫 사람이 다르다 (사람됨 · 목표 · 솜씨 — 이름은 차례대로) · 위 줄에 '항해 4127'",
                na == nb && same && first && a.Voyage == 4127 && top.Contains("항해 4127"), $"4127 '{na}' · 다시 '{nb}' · 바이트 같음 {same} · 첫 사람 4127 {wa} ↔ 4128 {wc} · 위 줄 '{top}'");
        }
        // S5: 뽑기 1000 ~ 99999 · 넣은 번호 읽기 · 확인용 띄우기는 18 · --voyage · --seed 가 먼저
        {
            var draws = Enumerable.Range(0, 3000).Select(_ => VoyageNumber.Draw()).ToList();
            bool range = draws.All(x => x >= VoyageNumber.Min && x <= VoyageNumber.Max) && draws.Distinct().Count() > 2500;
            bool parse = VoyageNumber.Parse("4127") == 4127 && VoyageNumber.Parse("04127") == 4127 && VoyageNumber.Parse("999") == null && VoyageNumber.Parse("100000") == null && VoyageNumber.Parse("41a7") == null && VoyageNumber.Parse("") == null;
            ulong check = VoyageNumber.ForLaunch(new[] { "--new", "--for=6" }), shot = VoyageNumber.ForLaunch(new[] { "--ui=newvoyage", "--shot=v2" }), skip = VoyageNumber.ForLaunch(new[] { "--skip=2", "--shots=x" });
            ulong voy = VoyageNumber.ForLaunch(new[] { "--for=6", "--voyage=4127" }), seed = VoyageNumber.ForLaunch(new[] { "--seed=5" });
            var plain = Enumerable.Range(0, 20).Select(_ => VoyageNumber.ForLaunch(new[] { "--new" })).ToList();
            bool launch = check == 18 && shot == 18 && skip == 18 && voy == 4127 && seed == 5 && plain.All(x => x >= 1000 && x <= 99999) && plain.Distinct().Count() > 1;
            Check("항해 번호 S5: 켤 때마다 1000 ~ 99999에서 새로 뽑는다 (세계 밖) · 넣은 번호는 그 범위의 숫자만 · 확인용 띄우기 (--for · --shots · --ui · --skip)는 18 · --voyage= · --seed= 가 먼저",
                range && parse && launch, $"뽑기 {draws.Min()}~{draws.Max()} (다른 값 {draws.Distinct().Count()}/3000) · 읽기 {parse} · 확인용 {check}/{shot}/{skip} · --voyage {voy} · --seed {seed} · 그냥 {string.Join(" ", plain.Take(4))}");
        }
        // M5: 번호 창 · 숫자판 · '지난번까지' 카드 · 처음 화면 카드 — 여섯 창 × 사람 3 · 12 · 40 · 100 × 글 100 · 150% — 겹침 0 · 밖 0 · 누를 자리 크기 · 뒤로 · 글자
        {
            var hud = new Hud();
            string[] ships = Layout.ShipKeys().Select(k => Layout.Ship(k, Tables.Default).Name + " · 6명").ToArray();
            var sizes = new[] { (1920, 1080), (1920, 1010), (1600, 900), (1366, 768), (1280, 720), (2560, 1440) };
            var bad = new List<string>(); var small = new List<string>(); var labels = new List<string>(); int layouts = 0;
            void Sizes(string tag, IEnumerable<UiHit> hits, float minW = 32, float minH = 32)
            {
                foreach (var x in hits) if (x.R.W + 0.01f < minW || x.R.H + 0.01f < minH) small.Add($"{tag}: {x.Act}/{x.Key} {x.R.W:0}×{x.R.H:0}");
            }
            void Apart(string tag, List<UiHit> hits)
            {
                for (int i = 0; i < hits.Count; i++) for (int j = i + 1; j < hits.Count; j++) if (hits[i].R.Intersects(hits[j].R)) bad.Add($"{tag}: {hits[i].Act}/{hits[i].Key} × {hits[j].Act}/{hits[j].Key}");
            }
            bool Inside(RectF inner, RectF outer) => inner.X >= outer.X - 0.5f && inner.Y >= outer.Y - 0.5f && inner.Right <= outer.Right + 0.5f && inner.Bottom <= outer.Bottom + 0.5f;
            foreach (var (pw, ph) in sizes)
                foreach (int n in new[] { 3, 12, 40, 100 })
                    foreach (float scale in new[] { 1f, 1.5f })
                    {
                        Hud.Measure ms = (t, px, b) => Hud.Approx(t, px, b) * scale;
                        Hud.MeasureLines ml = (t, px, w, k) => Hud.ApproxLines(t, (int)(px * scale), w, k);
                        string tag = $"{pw}×{ph} 사람 {n} 글 {scale * 100:0}%";
                        // 번호 창 (게임 메뉴 [새 항해 ›])
                        {
                            var f = FakeFrame(n, true); var st = new HudState { Ask = HudAsk.NewVoyage, AskNumber = "4127", AskShipAt = 2 };
                            HudLayout.Build(f, st, pw, ph, ms, ml, hud, ships); layouts++;
                            var win = hud.Area("창 새 항해");
                            var hits = hud.Hits.ToList();
                            int keys = hits.Count(x => x.Act == UiAct.VoyageKey), arrows = hits.Count(x => x.Act == UiAct.VoyageShip);
                            bool yes = hits.Any(x => x.Act == UiAct.AskYes && x.Enabled), no = hits.Any(x => x.Act == UiAct.AskNo);
                            if (win.Empty || keys != 12 || arrows != 2 || !yes || !no || !hud.Modal || hits.Count != keys + arrows + 2) bad.Add($"{tag} 번호 창: 창 {!win.Empty} · 숫자판 {keys} · 배 {arrows} · 띄우기 {yes} · 그만두기 {no} · 모달 {hud.Modal} · 누를 자리 {hits.Count}");
                            if (!Inside(win, new RectF(0, 0, hud.W, hud.H))) bad.Add($"{tag} 번호 창 밖 ({win.X:0},{win.Y:0} {win.W:0}×{win.H:0} / {hud.W:0}×{hud.H:0})");
                            foreach (var x in hits) if (!Inside(x.R, win)) bad.Add($"{tag} 번호 창: {x.Act}/{x.Key} 창 밖");
                            Apart(tag + " 번호 창", hits); Sizes(tag + " 번호 창", hits, 40, 40);
                            foreach (var x in hits.Where(x => x.Act == UiAct.VoyageKey)) { string lb = HudLayout.VoyageKeyLabel((int)x.Key); labels.Add(lb); if (ms(lb, 18, true) + 8 > x.R.W) bad.Add($"{tag} 숫자판 '{lb}' 글이 넘친다"); }
                            if (hud.At(4, 4).Kind == RouteKind.World) bad.Add($"{tag} 번호 창: 창 밖 누름이 세계로 샌다");
                            var off = new HudState { Ask = HudAsk.NewVoyage, AskNumber = "99" };
                            HudLayout.Build(f, off, pw, ph, ms, ml, hud, ships);
                            var y2 = hud.Hits.FirstOrDefault(x => x.Act == UiAct.AskYes);
                            if (y2 == null || y2.Enabled || y2.Why.Length == 0) bad.Add($"{tag} 번호 창: 틀린 번호 '99'에 [띄우기]가 켜졌다 · 까닭 '{y2?.Why}'");
                        }
                        // '지난번까지' 카드 (이어 본 첫 장)
                        {
                            var f = FakeFrame(n, true); f.Banner = ""; f.Replaying = false; f.Replay = "";
                            f.ResumeCard = new[] { "지난번까지 — 디딤호 · 항해 4127 · 43일째", "41일째 — 정지우가 불을 껐다", "42일째 — 조용한 하루", "43일째 — 김하늘과 정지우가 짝이 되었다", "산 이 6명 가운데 5명", "사진은 옮겨 오지 못했다" };
                            HudLayout.Build(f, new HudState(), pw, ph, ms, ml, hud, ships); layouts++;
                            var card = hud.Area("지난번까지"); var go = hud.Hits.FirstOrDefault(x => x.Act == UiAct.ResumeGo);
                            if (card.Empty || go == null || !Inside(go.R, card)) bad.Add($"{tag} 지난번까지: 카드 {!card.Empty} · [계속] {go != null}");
                            else Sizes(tag + " 지난번까지", new[] { go });
                            foreach (var o in hud.Overlaps()) bad.Add($"{tag} 지난번까지: {o}");
                            foreach (var o in hud.Outside()) bad.Add($"{tag} 지난번까지: 밖 {o}");
                        }
                        if (n != 3) continue;
                        // 처음 화면: 카드 있음 · 판이 다름 · 없음 × 다른 항해 목록 · 번호 창
                        var meta = new VoyageMeta { Ship = "Didim", ShipName = "디딤호", Voyage = 4127, Day = 43, Aboard = 6, Alive = 5, Yesterday = "43일째 — 정지우가 불을 껐다", SavedAt = "2026-10-26 21:40", WorldVersion = Sim.SaveVersion };
                        var five = Enumerable.Range(0, 5).Select(i => new VoyageMeta { Ship = "Didim", ShipName = "디딤호", Voyage = 4127 + i, Day = 3 + i, Aboard = 6, Alive = 6, SavedAt = $"2026-10-2{i} 21:40", WorldVersion = Sim.SaveVersion }).ToArray();
                        foreach (int mode in new[] { 0, 1, 2, 3, 4 })
                        {
                            var v = new StartView { Latest = mode == 2 ? null : meta, Why = mode == 1 ? VoyageStore.VersionText(Sim.SaveVersion - 1) : "", Voyages = five, ListOpen = mode == 3 };
                            var st = new HudState { Ask = mode == 4 ? HudAsk.NewVoyage : HudAsk.None, AskNumber = "4127" };
                            StartLayout.Build(v, st, pw, ph, ms, hud, ships); layouts++;
                            string mt = $"{tag} 처음 화면 {(mode switch { 0 => "카드", 1 => "판 다름", 2 => "카드 없음", 3 => "다른 항해", _ => "번호 창" })}";
                            var acts = hud.Hits.Select(x => x.Act).ToHashSet();
                            bool want = mode switch
                            {
                                0 => acts.IsSupersetOf(new[] { UiAct.StartResume, UiAct.StartOther, UiAct.StartNew, UiAct.StartChoice, UiAct.StartQuit }),
                                1 => acts.IsSupersetOf(new[] { UiAct.StartRenew, UiAct.StartDelete, UiAct.StartChoice }) && !acts.Contains(UiAct.StartResume),
                                2 => acts.IsSupersetOf(new[] { UiAct.StartChoice, UiAct.StartQuit, UiAct.StartNew }) && !acts.Contains(UiAct.StartResume),
                                3 => hud.Hits.Count(x => x.Act == UiAct.StartPick) == 5,
                                _ => hud.Modal && hud.Hits.Count(x => x.Act == UiAct.VoyageKey) == 12,
                            };
                            if (!want) bad.Add($"{mt}: 단추 [{string.Join(" ", hud.Hits.Select(x => x.Act).Distinct())}]");
                            foreach (var o in hud.Overlaps()) bad.Add($"{mt}: {o}");
                            foreach (var o in hud.Outside()) bad.Add($"{mt}: 밖 {o}");
                            var main = hud.Hits.Where(x => mode == 3 ? x.Act == UiAct.StartPick : mode == 4 ? x.Act is UiAct.VoyageKey or UiAct.VoyageShip or UiAct.AskYes or UiAct.AskNo : x.Act != UiAct.StartChoice).ToList();
                            Apart(mt, main); Sizes(mt, main.Where(x => x.Act != UiAct.StartChoice));
                            if (mode is 0 or 1)
                            {
                                var card = hud.Area("이어 보기 카드");
                                foreach (var x in hud.Hits.Where(x => x.Act is UiAct.StartResume or UiAct.StartOther or UiAct.StartNew or UiAct.StartRenew or UiAct.StartDelete)) if (!Inside(x.R, card)) bad.Add($"{mt}: {x.Act} 카드 밖");
                            }
                        }
                    }
            // 숫자판 차례 · 뒤로 차례
            var ks = new HudState { Ask = HudAsk.NewVoyage, AskNumber = "" };
            foreach (int d in new[] { 0, 4, 1, 2, 7 }) ks.VoyageKey(d);
            string typed = ks.AskNumber; bool ok4 = ks.AskNumberOk;
            ks.VoyageKey(9); ks.VoyageKey(5); string five5 = ks.AskNumber;
            for (int i = 0; i < 6; i++) ks.VoyageKey(10);
            bool empty = ks.AskNumber == "" && !ks.AskNumberOk;
            ks.VoyageKey(11); bool drawn = ks.AskNumberOk;
            var back = ks.Back(FakeFrame(3, true));
            labels.AddRange(new[] { VoyageCard.Title(new VoyageMeta { ShipName = "디딤호", Voyage = 4127, Day = 2, Aboard = 6, Alive = 6 }), VoyageStore.VersionText(1), VoyageStore.BrokenText });
            var glyph = labels.Where(t => t.Any(c => Hud.Missing.Contains(c))).ToList();
            Check("항해 번호 M5: 번호 창 (배 ‹ › · 번호 · 숫자판 열둘 · [그만두기] [띄우기] — 틀린 번호면 옅게 · 까닭) · '지난번까지' 카드 [계속] · 처음 화면 (카드 · 판 다름 · 카드 없음 · 다른 항해 목록 · 번호 창) — 여섯 창 × 사람 3 · 12 · 40 · 100 × 글 100 · 150%: 겹침 0 · 밖 0 · 누를 자리 크기 · 숫자판 차례 (앞 0을 떼고 다섯 자리까지) · 뒤로는 창부터 · 글자",
                bad.Count == 0 && small.Count == 0 && typed == "4127" && ok4 && five5 == "41279" && empty && drawn && back == BackStep.CloseAsk && ks.Ask == HudAsk.None && glyph.Count == 0,
                $"자리표 {layouts}가지 · 어긋남 {bad.Count} {string.Join(" | ", bad.Take(3))} · 작은 자리 {small.Count} {string.Join(" | ", small.Take(2))} · 숫자판 '{typed}' {ok4} → '{five5}' · 지움 {empty} · 뽑기 {drawn} · 뒤로 {back} · 글자 {glyph.Count}");
        }
    }
}
