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

    /// <summary>
    /// 장면 금고 (docs/OBSERVER.md §4 · §16 K3 ~ K5 — 묶음 '장면 금고'): 하루 다섯 (무게 순) · 이레 · 0시 세계 이레 · 넘치면 하루 셋 · 그래도 넘치면 가벼운 것부터 ·
    /// 시작이 당겨지면 더 앞의 찍기로 · 줄 표 ● (고리 · 금고) ◐ (닻 + 감기 어림 초) ○ (사진만) · 화면만 (금고를 켠 화면과 끈 화면의 세계 바이트가 같다).
    /// </summary>
    private static void VaultRules()
    {
        if (!Do("장면 금고")) return;
        Console.WriteLine("\n[장면 금고 — 하루 다섯 · 이레 · 바이트 한도 · 줄 표 · 화면만]");
        if (Environment.GetEnvironmentVariable("VAULT_BENCH") is { Length: > 0 } ship)
        {
            // 재기 (판정하지 않는다): 그 배의 찍기 — 금고 복사 (작업 스레드) · 압축 (다른 일꾼) · 풀기 · 하이라이트 하나의 압축 크기
            var s = LiveShow.Crewed(ship, 5); s.FastForward(30);
            var hb = new Highlight { Tick = s.Sim.Tick, StartTick = s.Sim.Tick, Last = s.Sim.Tick, Score = 100 };
            var snap = s.Dir.RingFor(hb)!.Value;
            var lend = new SceneVault();
            lend.Pin(new Highlight { Tick = hb.Tick, StartTick = hb.StartTick, Score = 1 }, snap, s.Dir); lend.WaitPacked();   // 데우기 (첫 일꾼 · 첫 압축)
            var sw1 = System.Diagnostics.Stopwatch.StartNew(); lend.Pin(hb, snap, s.Dir); double pin = sw1.Elapsed.TotalMilliseconds * 1000;
            lend.WaitPacked(); Thread.Sleep(20); bool returned = !s.Dir.Lent.ContainsKey(snap.snap);
            var vb = new SceneVault { PackLater = false };
            var sw2 = System.Diagnostics.Stopwatch.StartNew(); vb.Pin(hb, snap); double pack = sw2.Elapsed.TotalMilliseconds;
            sw2.Restart(); var back = vb.Raw(vb.Scenes[0]); double unpack = sw2.Elapsed.TotalMilliseconds;
            Console.WriteLine($"    (금고 재기) {ship} 찍기 {snap.len / 1e6:0.00}MB · 금고에 넣기 (고리 칸을 빌린다) {pin:0}µs · 돌려받음 {returned} · 복사 + 압축 {pack:0}ms → {vb.Bytes / 1e6:0.00}MB · 풀기 {unpack:0}ms · 이레 서른다섯 장면 + 0시 세계 일곱 ≈ {vb.Bytes * 42 / 1e6:0}MB");
        }
        // K3: 금고 셈 — 아흐레 · 하루 일곱 장면 (점수 갈래) · 0시 세계 아홉 → 하루 다섯 (무게 순) · 이레 · 0시 세계 일곱 / 바이트 한도 → 하루 셋 → 가벼운 것부터 / 시작이 당겨지면 앞의 찍기 / 풀면 같은 바이트
        {
            static byte[] Snap(int seed, int n) { var b = new byte[n]; var r = new Random(seed); for (int i = 0; i < n; i++) b[i] = (byte)(i % 7 == 0 ? r.Next(256) : i % 13); return b; }
            var v = new SceneVault { PackLater = false };
            var all = new List<Highlight>();
            for (int d = 0; d < 9; d++)
            {
                for (int k = 0; k < 7; k++)
                {
                    long t = d * Time.TicksPerDay + 1000 + k * 100;
                    var h = new Highlight { Tick = t, StartTick = t - 30, Last = t, Score = 80 + (k * 37 + d * 11) % 100, Title = $"{d}/{k}" };
                    all.Add(h);
                    var raw = Snap(d * 10 + k, 4000);
                    v.Pin(h, (t - 40, raw, raw.Length));
                }
                v.DayWorld(d * Time.TicksPerDay, Snap(900 + d, 4000));
                v.Trim(d);
            }
            var byDay = v.Scenes.GroupBy(e => e.Day).ToDictionary(g => g.Key, g => g.Select(e => e.H!).ToList());
            bool five = byDay.Count == 7 && byDay.Keys.Min() == 2 && byDay.All(kv => kv.Value.Count == 5
                && kv.Value.Min(h => h.Score) >= all.Where(h => (int)(h.Tick / Time.TicksPerDay) == kv.Key).OrderByDescending(h => h.Score).Skip(4).First().Score);
            bool days = v.DayWorlds.Count == 7 && v.DayWorlds.Min(e => e.Day) == 2;
            bool roundTrip = false, keptOld = false, moved = false;
            if (v.Scenes.Count > 0)
            {
                var first = v.Scenes[0];
                roundTrip = v.Raw(first).AsSpan().SequenceEqual(Snap((first.Day * 10) + int.Parse(first.H!.Title.Split('/')[1]), 4000)) && first.Packed;
                // 시작이 당겨졌다 (더 앞의 찍기) · 뒤의 찍기는 바꾸지 않는다
                var hx = first.H!; var earlier = Snap(4242, 4000); var later = Snap(4343, 4000);
                long t0 = first.Tick;
                v.Pin(hx, (t0 + 10, later, later.Length)); keptOld = v.Scenes.First(e => e.H == hx).Tick == t0;
                v.Pin(hx, (t0 - 50, earlier, earlier.Length)); moved = v.Scenes.First(e => e.H == hx).Tick == t0 - 50 && v.Raw(v.Scenes.First(e => e.H == hx)).AsSpan().SequenceEqual(earlier);
            }
            // 바이트 한도: 하루 셋 → 그래도 넘치면 가벼운 것부터
            var tight = new SceneVault { PackLater = false };
            foreach (var h in all.Where(h => h.Tick / Time.TicksPerDay >= 2)) { var raw = Snap((int)h.Tick, 4000); tight.Pin(h, (h.StartTick - 10, raw, raw.Length)); }
            long one = tight.Scenes.Count > 0 ? tight.Bytes / tight.Scenes.Count : 1;
            tight.MaxBytes = one * 24; tight.Trim(8);
            var tday = tight.Scenes.GroupBy(e => e.Day).Select(g => g.Count()).ToList();
            bool three = tday.All(c => c <= 3) && tight.Bytes <= tight.MaxBytes;
            float lightKept = tight.Scenes.Count > 0 ? tight.Scenes.Min(e => e.H!.Score) : 0, heavyGone = all.Where(h => h.Tick / Time.TicksPerDay >= 2 && !tight.Has(h)).Select(h => h.Score).DefaultIfEmpty(0).Max();
            Check("장면 금고 K3: 아흐레 × 하루 일곱 → 하루 다섯 (무게 순) · 이레 (앞의 둘째 날까지 놓는다) · 0시 세계도 이레 · 풀면 같은 바이트 (다른 일꾼이 압축) / 시작이 당겨지면 더 앞의 찍기로 (뒤의 찍기로는 바꾸지 않는다) / 바이트 한도를 넘으면 하루 셋 → 그래도 넘치면 가벼운 것부터",
                five && days && roundTrip && keptOld && moved && three && tight.Scenes.Count > 0,
                $"날 {string.Join(" ", byDay.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value.Count}"))} · 0시 세계 {v.DayWorlds.Count} (첫 {(v.DayWorlds.Count > 0 ? v.DayWorlds.Min(e => e.Day) : -1)}) · 풀기 {roundTrip} · 뒤 찍기 그대로 {keptOld} · 앞 찍기로 {moved} · 한도 {tight.Bytes}/{tight.MaxBytes} 날마다 {string.Join(",", tday)} · 남은 가장 가벼운 {lightKept} / 놓은 가장 무거운 {heavyGone}");
        }
        // K4: 장면 판 줄 표 — ● 고리 · ● 금고 · ◐ 닻 (0시 세계) + 감기 어림 초 · ○ 사진만 (▶ 옅게 · 까닭) / 기록 판 장의 장면도 같은 표
        {
            var show = new LiveShow(HandShip(75), "디딤호");
            show.Advance(0.2); show.Paused = true;
            long now = show.Sim.Tick;
            var ring = new Highlight { Tick = now, StartTick = now, Last = now, Score = 100, Title = "고리 장면" };
            var kept = new Highlight { Tick = -500, StartTick = -520, Last = -500, Score = 100, Title = "금고 장면" };
            var wound = new Highlight { Tick = -300, StartTick = -320, Last = -300, Score = 100, Title = "감을 장면" };
            var photo = new Highlight { Tick = -900, StartTick = -950, Last = -900, Score = 100, Title = "사진만 남은 장면" };
            var bytes = show.Sim.Save();
            show.Vault.Pin(kept, (-530, bytes, bytes.Length));
            show.Vault.DayWorld(-400, bytes);
            show.Dir.Highlights.AddRange(new[] { photo, kept, wound, ring });
            var ch = new Chapter { Day = 0, Title = "1일째 — 장면 넷" }; ch.Scenes.AddRange(new[] { photo, kept, wound, ring }); show.Dir.Chapters.Add(ch);
            show.TogglePanel(PanelKind.Highlights);
            var rows = show.PanelRows();
            PanelRow Row(Highlight h) => rows.FirstOrDefault(r => r.Main.Contains(h.Title));
            var (r1, r2, r3, r4) = (Row(ring), Row(kept), Row(wound), Row(photo));
            bool marks = r1.Mark == 1 && r1.ActOn && r2.Mark == 1 && r2.ActOn && r3.Mark == 2 && r3.ActOn && (r3.Sub ?? "").Contains("초 감는다") && r4.Mark == 3 && !r4.ActOn && r4.Why.Contains("사진");
            var w3 = show.WayOf(wound, out double sec3);
            show.ClosePanel(); show.TogglePanel(PanelKind.Record); show.PanelClick(LiveShow.KeyChapter(0));
            var rec = show.PanelRows().Where(r => r.Act.Length > 0).Select(r => r.Mark).ToList();
            bool record = rec.SequenceEqual(new byte[] { 3, 1, 2, 1 });
            // 감기: ◐ 장면을 누르면 다른 일꾼이 감고 (산 세계는 멈춘다) '감는 중' 띠 · 끝나면 다시보기 / 감는 중 띠 자리 — 여섯 창 × 글 100 · 150% (겹침 · 밖 0 · [그만] 띠 안)
            show.ClosePanel();
            long d0 = show.Sim.Tick;
            show.Vault.DayWorld(d0, show.Sim.Save());
            show.FastForward(40);
            var later = new Highlight { Tick = d0 + 30, StartTick = d0 + 20, Last = d0 + 30, Score = 100, Title = "0시 세계 뒤 장면" };
            show.Dir.Highlights.Add(later); show.Dir.Ring.Clear();
            long liveAt = show.Sim.Tick;
            bool begun = show.WayOf(later, out _) == SceneWay.Rewind && show.StartReplay(later);
            var fw = show.Render();
            bool band = begun && show.Rewinding && fw.Rewind.Contains("감는 중") && !fw.Replaying;
            show.WaitRewind(60_000); show.Advance(0.01);
            bool replay = show.Replaying && show.ReplayOf == later && show.World.Tick == later.StartTick && show.Sim.Tick == liveAt;
            show.StopReplay();
            var hud = new Hud(); var bad = new List<string>();
            foreach (var (pw, ph) in new[] { (1920, 1080), (1920, 1010), (1600, 900), (1366, 768), (1280, 720), (2560, 1440) })
                foreach (float scale in new[] { 1f, 1.5f })
                {
                    var f = FakeFrame(12, true); f.Rewind = "감는 중 — 약 12초 (40%) · 3일 07:04 식당 불 → 박서연이 쓰러졌다"; f.Replaying = false; f.Replay = "";
                    HudLayout.Build(f, new HudState(), pw, ph, (t, px, b) => Hud.Approx(t, px, b) * scale, (t, px, w, k) => Hud.ApproxLines(t, (int)(px * scale), w, k), hud);
                    var area = hud.Area("감기 띠"); var stop = hud.Hits.FirstOrDefault(x => x.Act == UiAct.RewindStop);
                    if (area.Empty || stop == null || !InsideR(stop.R, area) || stop.R.W < 32 || stop.R.H < 32) bad.Add($"{pw}×{ph} {scale * 100:0}%: 띠 {area} · [그만] {stop?.R}");
                    foreach (var o in hud.Overlaps()) bad.Add($"{pw}×{ph}: {o}");
                    foreach (var o in hud.Outside()) bad.Add($"{pw}×{ph}: 밖 {o}");
                }
            Check("장면 금고 K4: 장면 판 줄 표 — 고리 안 ● · 금고에 든 것 ● · 0시 세계에서 감을 것 ◐ ('약 n초 감는다') · 닻이 없는 것 ○ (▶ 옅게 · '사진만') / 기록 판 장의 장면도 같은 표 / ◐를 누르면 '감는 중' 띠 (산 세계는 멈춘다) → 다시보기 · 띠 자리 (여섯 창 × 글 둘 — 겹침 · 밖 0)",
                marks && w3 == SceneWay.Rewind && sec3 >= 0 && record && band && replay && bad.Count == 0,
                $"표 {r1.Mark}/{r2.Mark}/{r3.Mark}/{r4.Mark} · ▶ {r1.ActOn}/{r2.ActOn}/{r3.ActOn}/{r4.ActOn} · ◐ '{r3.Sub}' ({sec3:0.0}초) · ○ '{r4.Why}' · 기록 판 {string.Join(",", rec)} · 감기 띠 {band} '{fw.Rewind}' · 다시보기 {replay} (산 세계 {liveAt}→{show.Sim.Tick}) · 자리 {bad.Count} {string.Join(" | ", bad.Take(2))}");
        }
        // K5: 화면만 — 금고를 켠 화면 (장면마다 찍기 복사 · 0시 세계)과 끈 화면을 불씨 하나 낀 30분 나란히: 세계 바이트 · 연출 기록이 같다
        {
            var a = new LiveShow(HandShip(76), "디딤호"); var b = new LiveShow(HandShip(76), "디딤호");
            b.Vault.On = false;
            foreach (var x in new[] { a, b }) { var aim = HandTargets(x.Sim); x.FastForward(20); x.Sim.Get<Hand>().Enqueue(Order(HandKind.Fire, aim.Bed, 2)); x.FastForward(Time.Minutes(15)); }
            a.Vault.DayWorld(a.Sim.Tick, a.Sim.Save());
            a.FastForward(Time.Minutes(15)); b.FastForward(Time.Minutes(15));
            bool world = a.Sim.Save().AsSpan().SequenceEqual(b.Sim.Save());
            // 하이라이트 · 장 (연출 기록 바이트 통째는 아니다 — 미리 보기 세계를 다른 코어에서 받는 틱이 벽시계라 부하에 따라 갈린다 · 금고와 상관없이)
            bool record = HighlightsOf(a.Dir) == HighlightsOf(b.Dir) && a.Dir.ChapterNow(a.Sim).Title == b.Dir.ChapterNow(b.Sim).Title && a.Dir.Chapters.Count == b.Dir.Chapters.Count;
            // 저장 왕복: 곳간에 쓰면 금고 찍기는 vault/ 파일로 · 이어 보면 금고가 그대로 (같은 장면 · 같은 바이트 · 같은 길)
            string dir = TempVoyages(); bool kept = false; int files = 0;
            try
            {
                var store = new VoyageStore(dir);
                a.Vault.WaitPacked();
                var meta = store.Write(a.SaveBundle(SaveKind.Quit));
                files = Directory.Exists(Path.Combine(store.FolderOf(meta), VoyageStore.VaultFolder)) ? Directory.GetFiles(Path.Combine(store.FolderOf(meta), VoyageStore.VaultFolder)).Length : 0;
                var back = LiveShow.Resume(store, store.Latest() ?? meta, out _);
                kept = back != null && back.Vault.Scenes.Count == a.Vault.Scenes.Count && back.Vault.DayWorlds.Count == a.Vault.DayWorlds.Count
                    && back.Vault.Scenes.Zip(a.Vault.Scenes).All(p => p.First.Tick == p.Second.Tick && p.First.H!.Title == p.Second.H!.Title && back.Vault.Raw(p.First).AsSpan().SequenceEqual(a.Vault.Raw(p.Second)))
                    && a.Dir.Highlights.Select((h, i) => a.WayOf(h, out _) == back.WayOf(back.Dir.Highlights[i], out _)).All(x => x);
            }
            finally { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { } }
            Check("장면 금고 K5: 화면만 — 금고를 켠 화면 (장면 찍기 복사 · 0시 세계)과 끈 화면이 불씨 하나 낀 30분 뒤 세계 바이트 · 하이라이트 · 장이 같다 · 곳간에 쓰면 금고는 vault/ 파일로 — 이어 보면 같은 장면 · 같은 바이트 · 같은 길",
                world && record && a.Vault.Scenes.Count > 0 && b.Vault.Scenes.Count == 0 && kept && files == a.Vault.Scenes.Select(e => e.File).Distinct().Count() + a.Vault.DayWorlds.Count,
                $"세계 같음 {world} · 하이라이트 · 장 같음 {record} · 금고 {a.Vault.Scenes.Count}/{b.Vault.Scenes.Count} · 0시 세계 {a.Vault.DayWorlds.Count} · 하이라이트 {a.Dir.Highlights.Count} · 파일 {files} · 이어 봄 {kept}");
        }
    }
}
