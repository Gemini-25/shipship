using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ShipSim.Core;
using ShipSim.View;

// v16.5b 기술과 설비가 티 나는 그림 점검 (Godot 없이):
//   표(View/TechLookTable.cs — 이 시험이 함께 빌드한다)와 그림 소스(View/TechVisuals*.cs · FixtureArtTier.cs · TechLook.cs · TechMoments.cs)를 읽어
//   ① 미감 세트 3단계가 빠짐없이 매핑되고 서로 다르다 · 실제 배가 기술을 익히면 초기 → 중간 → 고급으로 넘어간다
//   ② TechWeb 의 Visual 열쇠 111개가 모두 표에 있고 그림 함수(case)에 연결된다 · 움직이는 것은 움직임 함수에도 · 그림이 서로 겹치지 않는다
//   ③④ 모든 FurnitureType 이 단계 II~IV 부품 · Mk.1 손질 · Mk.3 마감을 가진다 (단계마다 · 종류마다 다르다) · 부품 30종 모두 그려진다
//   ⑤ 개조 칸 — 실제 칸막이 · 모듈 설치 기록 시각을 읽어 새것 → 낡음 ⑥ 설치 · 업그레이드 순간 — 실제 이력 줄을 읽는다
//   + 그리기는 Core 를 바꾸지 않는다 (지문이 같다 · 소스에 상태 쓰기가 없다) · 화면 연결 훅이 있다.
public static partial class Program
{
    private static string? TechLookViewDir() => FixArtViewDir();

    /// <summary>case "열쇠": 로 시작해 다음 case/함수 끝까지 (함수 이름별).</summary>
    private static Dictionary<string, Dictionary<string, string>> TechLookCases(string src)
    {
        var res = new Dictionary<string, Dictionary<string, string>>();
        foreach (Match m in Regex.Matches(src, @"private\s+(?:static\s+)?void\s+(Vis\w+)\s*\(([^)]*)\)"))
        {
            int open = src.IndexOf('{', m.Index + m.Length);
            if (open < 0) continue;
            int depth = 0, end = open;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}' && --depth == 0) { end = i; break; }
            }
            var body = src.Substring(open, end - open + 1);
            var cases = new Dictionary<string, string>();
            var ms = Regex.Matches(body, "case \"([a-z]+\\.[a-z]+)\":");
            for (int k = 0; k < ms.Count; k++)
            {
                int s0 = ms[k].Index + ms[k].Length;
                int s1 = k + 1 < ms.Count ? ms[k + 1].Index : body.Length;
                cases[ms[k].Groups[1].Value] = body.Substring(s0, s1 - s0);
            }
            res[m.Groups[1].Value] = cases;
        }
        return res;
    }

    private static int RunTechLookCheck(int seed)
    {
        _fails = 0;
        Console.WriteLine($"기술 · 설비가 티 나는 그림 점검 (v16.5b) · 시드 {seed}\n");
        var dir = TechLookViewDir();
        Check("소스 — game/src/View 를 찾았다", dir != null, dir ?? "없음");
        if (dir == null) { Console.WriteLine($"\n✘ {_fails}개 실패"); return 1; }
        string Read(string pat) => string.Join("\n", Directory.GetFiles(dir, pat).OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText));
        var visSrc = Read("TechVisuals*.cs");
        var tierSrc = File.ReadAllText(Path.Combine(dir, "FixtureArtTier.cs"));
        var lookSrc = File.ReadAllText(Path.Combine(dir, "TechLook.cs")) + File.ReadAllText(Path.Combine(dir, "TechMoments.cs"));

        // ═══════════ ① 미감 세트 ═══════════
        var sets = TechLookTable.Sets;
        Check("세트 — 3단계(초기 · 중간 · 고급)가 하나씩 차례로", sets.Length == 3 && sets.Select((s, i) => (int)s.Level == i).All(b => b), string.Join(" · ", sets.Select(s => s.Name)));
        bool distinct = sets.Select(s => s.Frame).Distinct().Count() == 3 && sets.Select(s => s.Wires).Distinct().Count() == 3 && sets.Select(s => s.Lamp).Distinct().Count() == 3
            && sets.Select(s => s.Wall).Distinct().Count() == 3 && sets.Select(s => s.Light).Distinct().Count() == 3 && sets.Select(s => s.Trim).Distinct().Count() == 3 && sets.Select(s => s.Panel).Distinct().Count() == 3;
        Check("세트 — 테두리 · 배선 · 조명 방식 · 벽 · 빛 · 장식 · 패널 색이 단계마다 다르다", distinct,
            string.Join(" / ", sets.Select(s => $"{s.Frame}·{s.Wires}·{s.Lamp}·#{s.Light:x8}")));
        bool trend = sets[0].Peel > sets[1].Peel && sets[1].Peel >= sets[2].Peel && sets[0].Gloss < sets[1].Gloss && sets[1].Gloss < sets[2].Gloss
            && sets[0].RivetStep > 0f && sets[2].RivetStep == 0f && sets[0].Glow < sets[2].Glow && sets[0].Grime > sets[2].Grime && sets[0].Corner < sets[2].Corner;
        Check("세트 — 무늬 매개변수가 기술을 따라 간다 (벗겨짐 · 때 ↓ · 광택 · 빛 · 둥글기 ↑ · 고급은 리벳 없음)", trend,
            string.Join(" / ", sets.Select(s => $"벗겨짐 {s.Peel} 광택 {s.Gloss} 리벳 {s.RivetStep}")));
        var levels = Enumerable.Range(0, 60).Select(TechLookTable.LevelFor).Distinct().ToList();
        Check("세트 — 점수 → 단계 매핑에 빠진 단계가 없다 (0 초기 · 10 중간 · 28 고급)", levels.Count == 3
            && TechLookTable.LevelFor(0) == LookLevel.Early && TechLookTable.LevelFor(TechLookTable.MidAt) == LookLevel.Mid && TechLookTable.LevelFor(TechLookTable.AdvancedAt) == LookLevel.Advanced,
            string.Join(",", levels));

        // 실제 배: 기술을 익히면 단계가 넘어간다 (검증 장면 — 같은 배를 초기 / 고급 기술로)
        var w = DayOne(seed, "Hanbit");
        var lv0 = TechLookTable.LevelOf(w);
        int score0 = TechLookTable.Score(w);
        var order = TechWeb.Every.OrderBy(t => t.Era).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
        int added = 0;
        foreach (var t in order) { if (TechLookTable.Score(w) >= TechLookTable.MidAt) break; if (w.Eras.Known.Add(t.Id)) added++; }
        var lv1 = TechLookTable.LevelOf(w);
        foreach (var t in order) { if (TechLookTable.Score(w) >= TechLookTable.AdvancedAt) break; if (w.Eras.Known.Add(t.Id)) added++; }
        var lv2 = TechLookTable.LevelOf(w);
        Check("장면 — 같은 배: 갓 떠난 배는 초기 · 기술을 익히면 중간 · 더 익히면 고급", lv0 == LookLevel.Early && lv1 == LookLevel.Mid && lv2 == LookLevel.Advanced,
            $"처음 점수 {score0} ({lv0}) → +{added}개 익힘 → {TechLookTable.Score(w)} ({lv2}) · 다음 단계까지 {TechLookTable.Toward(w):0.00}");
        var e0 = TechLookTable.Of(lv0);
        var e2 = TechLookTable.Of(lv2);
        int diffs = new[] { e0.Wall != e2.Wall, e0.Light != e2.Light, e0.Frame != e2.Frame, e0.Wires != e2.Wires, e0.Lamp != e2.Lamp, e0.Peel != e2.Peel, e0.Gloss != e2.Gloss, e0.RivetStep != e2.RivetStep }.Count(b => b);
        Check("장면 — 초기 / 고급 배는 벽 · 빛 · 테두리 · 배선 · 조명 · 칠 · 광택 · 리벳이 모두 다르다 (한눈에 다르다)", diffs == 8, $"다른 항목 {diffs}/8");

        // ═══════════ ② 기술마다 배 모습 ═══════════
        var keys = TechWeb.Every.Select(TechWeb.Visual).ToList();
        var table = TechLookTable.Visuals;
        var missing = keys.Where(k => !table.ContainsKey(k)).ToList();
        var extra = table.Keys.Except(keys).ToList();
        Check($"모습 — TechWeb 기술 {TechWeb.Every.Length}개의 Visual 열쇠가 모두 표에 있다 (겹침 · 남는 줄 없음)",
            missing.Count == 0 && extra.Count == 0 && keys.Distinct().Count() == keys.Count && TechLookTable.VisualRows.Length == keys.Count,
            $"열쇠 {keys.Distinct().Count()} · 표 {TechLookTable.VisualRows.Length}" + (missing.Count > 0 ? $" · 빠짐: {string.Join(",", missing.Take(8))}" : "") + (extra.Count > 0 ? $" · 남음: {string.Join(",", extra.Take(8))}" : ""));
        bool rowsOk = TechLookTable.VisualRows.All(r => (r.Anchor != VAnchor.Fix || r.Fix != null) && r.What.Length > 0 && TechLookTable.TechOf(r.Key) != null);
        Check("모습 — 줄마다 자리 · 대상 · 설명 · 기술이 있다", rowsOk, $"자리별 {string.Join(" ", TechLookTable.VisualRows.GroupBy(r => r.Anchor).Select(g => $"{g.Key}{g.Count()}"))}");
        var cases = TechLookCases(visSrc);
        var staticFns = cases.Where(kv => kv.Key.EndsWith("Static")).ToList();
        var liveFns = cases.Where(kv => kv.Key.EndsWith("Live")).ToList();
        var drawn = staticFns.SelectMany(kv => kv.Value.Keys).ToHashSet();
        var liveDrawn = liveFns.SelectMany(kv => kv.Value.Keys).ToHashSet();
        var notDrawn = keys.Where(k => !drawn.Contains(k)).ToList();
        Check("모습 — 열쇠 111개가 모두 그림 함수(Vis*Static)의 case 로 연결된다", notDrawn.Count == 0 && drawn.Count >= keys.Count,
            $"정적 함수 {staticFns.Count}개 · 그린 열쇠 {drawn.Count}" + (notDrawn.Count > 0 ? $" · 그림 없음: {string.Join(",", notDrawn.Take(10))}" : ""));
        var liveRows = TechLookTable.VisualRows.Where(r => r.Live).Select(r => r.Key).ToList();
        var liveMissing = liveRows.Where(k => !liveDrawn.Contains(k)).ToList();
        Check("모습 — 움직이는 줄(Live)은 모두 움직임 함수(Vis*Live)에도 있다", liveMissing.Count == 0, $"움직임 {liveRows.Count}줄" + (liveMissing.Count > 0 ? $" · 빠짐: {string.Join(",", liveMissing)}" : ""));
        var bodies = staticFns.SelectMany(kv => kv.Value).Select(kv => (kv.Key, body: FixArtNormalize(kv.Value))).ToList();
        var sameBody = bodies.GroupBy(b => b.body).Where(g => g.Count() > 1).Select(g => string.Join("=", g.Select(x => x.Key))).ToList();
        var thin = bodies.Where(b => FixArtDrawCall.Matches(b.body).Count < 2).Select(b => b.Key).ToList();
        Check("모습 — 그림이 열쇠마다 다르다 (같은 내용 없음 · 도형 둘 이상)", sameBody.Count == 0 && thin.Count == 0,
            (sameBody.Count > 0 ? $"같음: {string.Join(" ", sameBody.Take(5))}" : "모두 다름") + (thin.Count > 0 ? $" · 도형 모자람: {string.Join(",", thin.Take(8))}" : ""));
        bool fieldColor = lookSrc.Contains("TechIcons.FieldColor(t.Field)") && visSrc.Contains("TechIcons.Draw(");
        Check("모습 — 색은 기술 아이콘과 같은 분야 색 · 방 장치엔 아이콘 명판 · 갈림길 문장", fieldColor && visSrc.Contains("PaintIdentityCrest"), "TechLook.Field · VisMakerPlate · PaintIdentityCrest");

        // 갈림길 정체성
        var wf = DayOne(seed, "Hanbit");
        wf.Eras.Known.Add("algaeox");
        wf.Eras.Known.Add("chemox");
        wf.TechWeb.ForkStates["oxygen"].Side = 1; // 조류 광합성을 골랐다
        bool idA = TechLookTable.Identity(wf, "lifesupport.algaetrough"), idB = TechLookTable.Identity(wf, "lifesupport.candles");
        Check("모습 — 갈림길에서 고른 쪽(초록 숨의 배)만 배 전체에 짙게 퍼진다", idA && !idB, $"조류 수조 {idA} · 산소 양초 {idB}");
        var act = TechLookTable.Active(wf).Select(r => r.Key).ToList();
        Check("모습 — 익힌 기술만 그려진다 (익힌 차례가 아니라 표 차례 — 벽 자리가 늘 같다)", act.Contains("lifesupport.algaetrough") && act.Contains("lifesupport.candles") && !act.Contains("engine.warp")
            && act.SequenceEqual(act.OrderBy(k => Array.FindIndex(TechLookTable.VisualRows, r => r.Key == k))), $"그려질 것 {act.Count}개");
        var shipTypes = wf.Ship.Furniture.Select(f => f.Type).ToHashSet();
        var fixAbsent = TechLookTable.VisualRows.Where(r => r.Anchor == VAnchor.Fix && !shipTypes.Contains(r.Fix!.Value)).Select(r => r.Key).ToList();
        Check("모습 — 설비에 붙는 모습의 대상 설비가 한빛호에 있다 (정보)", true, fixAbsent.Count == 0 ? "모두 있다" : $"이 배엔 없음(그 설비가 생기면 붙는다): {string.Join(",", fixAbsent)}");

        // ═══════════ ③④ 단계 · 등급 ═══════════
        var all = Enum.GetValues<FurnitureType>();
        var noKit = all.Where(t => TechLookTable.Kit(t) == null).ToList();
        Check($"단계 — FurnitureType {all.Length}종 모두 단계 · 등급 줄이 있다", noKit.Count == 0 && TechLookTable.Kits.Length == all.Length,
            $"줄 {TechLookTable.Kits.Length}" + (noKit.Count > 0 ? $" · 빠짐: {string.Join(",", noKit)}" : ""));
        var badTier = TechLookTable.Kits.Where(k => k.II.Length == 0 || k.III.Length == 0 || k.IV.Length == 0 || k.Sig(2) == k.Sig(3) || k.Sig(3) == k.Sig(4) || k.Sig(2) == k.Sig(4)).Select(k => k.Type).ToList();
        Check("단계 — 종류마다 II · III · IV 부품이 있고 단계끼리 다르다 (올리면 모양이 바뀐다)", badTier.Count == 0, badTier.Count == 0 ? "모두" : string.Join(",", badTier));
        var triple = TechLookTable.Kits.GroupBy(k => $"{k.Sig(2)}|{k.Sig(3)}|{k.Sig(4)}").Where(g => g.Count() > 1).Select(g => string.Join("=", g.Select(k => k.Type))).ToList();
        Check("단계 — 부품 조합(II|III|IV)이 종류마다 다르다", triple.Count == 0, triple.Count == 0 ? $"{TechLookTable.Kits.Length}가지" : string.Join(" ", triple));
        var usedParts = TechLookTable.Kits.SelectMany(k => k.II.Concat(k.III).Concat(k.IV)).Select(p => p.Part).ToHashSet();
        var unusedParts = Enum.GetValues<TierPart>().Where(p => !usedParts.Contains(p)).ToList();
        var undrawnParts = Enum.GetValues<TierPart>().Where(p => !tierSrc.Contains($"case TierPart.{p}:")).ToList();
        Check("단계 — 부품 30종이 모두 쓰이고 모두 그림(case)이 있다", unusedParts.Count == 0 && undrawnParts.Count == 0,
            $"부품 {Enum.GetValues<TierPart>().Length}" + (unusedParts.Count > 0 ? $" · 안 씀: {string.Join(",", unusedParts)}" : "") + (undrawnParts.Count > 0 ? $" · 그림 없음: {string.Join(",", undrawnParts)}" : ""));
        bool posOk = TechLookTable.Kits.SelectMany(k => k.II.Concat(k.III).Concat(k.IV)).All(p => p.U >= 0f && p.U <= 1f && p.V >= 0f && p.V <= 1f);
        Check("단계 — 부품 자리가 몸체 안 (0~1)", posOk);
        var treeTypes = Tech.Types.ToList();
        Check("단계 — 기술 나무가 있는 설비(실제로 단계가 오르는 것)도 모두 덧그림을 가진다 (기존 단계 모양 유지 · 확장)", treeTypes.All(t => TechLookTable.Kit(t) != null)
            && File.ReadAllText(Path.Combine(dir, "ShipViewTiers.cs")).Contains("PaintTierLife"), $"나무 {treeTypes.Count}종");
        var improvs = TechLookTable.Kits.Select(k => k.Mk1).Distinct().Count();
        var trims = TechLookTable.Kits.Select(k => k.Mk3).Distinct().Count();
        var undrawnImprov = Enum.GetValues<Improv>().Where(i => i != Improv.Cardboard && !tierSrc.Contains($"case Improv.{i}:")).ToList();
        var undrawnTrim = Enum.GetValues<Trim>().Where(t => t != Trim.HaloRing && !tierSrc.Contains($"case Trim.{t}:")).ToList();
        Check("등급 — Mk.1 손질 11종 · Mk.3 마감 6종이 모두 쓰이고 그려진다 (설비 성격마다)", improvs == Enum.GetValues<Improv>().Length && trims == Enum.GetValues<Trim>().Length
            && undrawnImprov.Count == 0 && undrawnTrim.Count == 0, $"손질 {improvs} · 마감 {trims}");
        Check("등급 — Mk.1 은 색이 다른 판 · 삐뚤게 · 볼트 · 딱지, Mk.3 는 매끈한 광택 · 빛 (예전 공통 Mk.1 그림은 표 밖 설비만)",
            tierSrc.Contains("Mk1Static") && tierSrc.Contains("tilt") && tierSrc.Contains("\"Mk.1\"") && tierSrc.Contains("Mk3Static")
            && File.ReadAllText(Path.Combine(dir, "ShipView.cs")).Contains("m.Grade == MachineGrade.Mk1 && !FixtureArt.Has(f.Type)"));
        // 장면: 설비를 올리면 모양이 바뀐다
        var ox = TechLookTable.Kit(FurnitureType.OxygenGenerator)!;
        Check("장면 — 산소 발생기를 I → III 으로 올리면 붙는 부품이 0 → 4개", ox.At(1).Length == 0 && ox.II.Length + ox.III.Length == 4, $"II {ox.Sig(2)} · III {ox.Sig(3)} · IV {ox.Sig(4)}");

        // ═══════════ ⑤ 개조 칸 ═══════════
        var wr = DayOne(seed, "Hanbit");
        Run(wr, SimTime.Hours(1));
        Room? split = null;
        foreach (var r in wr.Ship.LiveRooms.ToList())
            if (Remodel.FindSplit(wr, r) is Remodel.SplitPlan sp) { split = Remodel.Apply(wr, sp); if (split != null) break; }
        long t0 = wr.Tick;
        long since = split != null ? TechLookTable.RemodelSince(wr, split) : -1;
        long sinceA = split?.SplitFrom != null ? TechLookTable.RemodelSince(wr, split.SplitFrom) : -1;
        float f0 = TechLookTable.Freshness(wr.Tick, since);
        Check("개조 — 칸막이로 나눈 두 칸 모두 그 때를 기록 시각으로 읽는다 · 막 끝난 칸은 새것(1)", split != null && since == t0 && sinceA == t0 && f0 > 0.99f, $"{split?.Name} · 때 {since} · 새것 {f0:0.00}");
        float fHalf = TechLookTable.Freshness(t0 + (long)(SimTime.TicksPerDay * TechLookTable.FreshDays / 2f), since);
        float fOld = TechLookTable.Freshness(t0 + (long)(SimTime.TicksPerDay * (TechLookTable.FreshDays + 1f)), since);
        Check($"개조 — 날이 갈수록 낡는다 ({TechLookTable.FreshDays:0}일 반쯤 0.5 · 지나면 0) · 고친 적 없는 칸은 0", Math.Abs(fHalf - 0.5f) < 0.02f && fOld == 0f && TechLookTable.Freshness(wr.Tick, -1) == 0f, $"반 {fHalf:0.00} · 뒤 {fOld:0.00}");
        var host = wr.Ship.LiveRooms.First(r => r.Type == RoomType.LifeSupport);
        long beforeHost = TechLookTable.RemodelSince(wr, host);
        int nBefore = wr.Ship.Furniture.Count;
        bool installed = Modules.Install(wr, host, FurnitureType.Scrubber, wr.Crew[0], host.Cells[host.Cells.Count / 2]);
        var newF = installed ? wr.Ship.Furniture[^1] : null;
        Check("개조 — 모듈을 달면 그 방도 새 패널 (방 이력 '설치' 줄의 때)", installed && TechLookTable.RemodelSince(wr, host) == wr.Tick && beforeHost != wr.Tick, $"{host.Name} {beforeHost} → {TechLookTable.RemodelSince(wr, host)}");

        // ═══════════ ⑥ 설치 · 업그레이드 순간 ═══════════
        var mi = newF?.Machine != null ? TechLookTable.MomentOf(newF.Machine) : (MomentKind.None, -1L);
        Check("순간 — 실제 모듈 설치 이력('달았다')을 읽어 설치 순간이 시작된다", mi.Item1 == MomentKind.Install && mi.Item2 == wr.Tick && TechLookTable.MomentPhase(wr.Tick, mi.Item2) == 0f,
            $"{mi.Item1} @{mi.Item2} · 설비 {nBefore}→{wr.Ship.Furniture.Count}");
        var gen = wr.Ship.Furniture.First(f => f.Type == FurnitureType.OxygenGenerator).Machine!;
        string before = Tech.TierName(gen);
        var nt = Tech.Next(gen)!;
        gen.Tier = nt.Tier; // Evolution.TierUp 과 같은 줄 (시험이 단계를 올린다)
        MarkLog.Add(gen.Marks, wr.Tick, $"정비공: {before} → {nt.Name}");
        var mu = TechLookTable.MomentOf(gen);
        Check("순간 — 단계를 올린 이력(… → 사바티에 반응기)은 업그레이드 순간", mu.Item1 == MomentKind.Upgrade && mu.Item2 == wr.Tick, $"{mu.Item1} · {nt.Name}");
        var kinds = new[] { ("A: Mk.3 개량 (고장 2회 뒤)", MomentKind.Mk3), ("A: Mk.1 임시품", MomentKind.Mk1), ("A: 정품 복원", MomentKind.Restore), ("A: 파손 뒤 다시 짜 맞춤", MomentKind.Reassemble), ("A: 함교 → 기관실", MomentKind.Reassemble), ("전조: 베어링", MomentKind.None) };
        var bad = kinds.Where(k => TechLookTable.Classify(gen, k.Item1) != k.Item2).Select(k => k.Item1).ToList();
        Check("순간 — 이력 줄 종류: Mk.3 · Mk.1 · 정품 복원 · 다시 짜 맞춤 · 옮김 · (전조는 순간 아님)", bad.Count == 0, bad.Count == 0 ? "모두 맞다" : string.Join(" / ", bad));
        float ph1 = TechLookTable.MomentPhase(wr.Tick + TechLookTable.MomentTicks / 2, mu.Item2);
        float ph2 = TechLookTable.MomentPhase(wr.Tick + TechLookTable.MomentTicks, mu.Item2);
        Check($"순간 — 몇 초(보통 빠르기 {TechLookTable.MomentTicks / (float)SimTime.TicksPerSecond:0}초) 동안 조립 → 불꽃 → 첫 점등, 그 뒤엔 끝", Math.Abs(ph1 - 0.5f) < 0.01f && ph2 < 0f
            && lookSrc.Contains("① 조립") && lookSrc.Contains("② 불꽃") && lookSrc.Contains("③ 첫 점등"), $"반 {ph1:0.00} · 끝 {ph2}");
        wr.TechWeb.Learned.Add(("radshield", wr.Tick));
        var rl = TechLookTable.RecentLearned(wr).ToList();
        Check("순간 — 방금 익힌 기술은 그 모습이 처음 붙는 자리에서 같은 순간을 겪는다", rl.Any(x => x.key == "hull.radlayer"), string.Join(",", rl.Select(x => x.key)));

        // ═══════════ 결정론 · 읽기만 ═══════════
        uint H()
        {
            var hw = World.CreateDefault(seed, 0, "Hanbit");
            Run(hw, SimTime.TicksPerDay + SimTime.Hours(6));
            return SaveGame.StateHash(hw);
        }
        uint h1 = H(), h2 = H();
        Check("결정론 — 같은 시드 하루 반 지문이 같다", h1 == h2, $"{h1:x8} / {h2:x8}");
        var wd = DayOne(seed, "Hanbit");
        Run(wd, SimTime.Hours(2));
        uint a1 = SaveGame.StateHash(wd);
        foreach (var r in wd.Ship.Rooms) TechLookTable.RemodelSince(wd, r);
        foreach (var m in wd.Ship.Machines) TechLookTable.MomentOf(m);
        TechLookTable.Active(wd); TechLookTable.LevelOf(wd); TechLookTable.RecentLearned(wd).ToList();
        uint a2 = SaveGame.StateHash(wd);
        Check("결정론 — 표가 세계를 읽어도 지문이 그대로 (읽기만)", a1 == a2, $"{a1:x8} / {a2:x8}");
        var mySrc = visSrc + tierSrc + lookSrc + File.ReadAllText(Path.Combine(dir, "TechLookTable.cs"));
        var writes = Regex.Matches(FixArtNormalize(mySrc), @"\.(Tier|Grade|Wear|Condition|Active|Powered|SplitSince|Done)=(?!=)|Known\.Add|MarkLog\.Add|\.Marks\.Add|Learned\.Add").Select(m => m.Value).Distinct().ToList();
        Check("결정론 — 그림 소스에 Core 상태 쓰기가 없다", writes.Count == 0, writes.Count == 0 ? "없음" : string.Join(" ", writes));
        var sv = File.ReadAllText(Path.Combine(dir, "ShipView.cs"));
        var fa = File.ReadAllText(Path.Combine(dir, "FixtureArt.cs"));
        Check("연결 — ShipView 가 층 · 매 프레임 갱신 · 움직임을 부르고, FixtureArt 가 tier · grade 자리를 부른다",
            sv.Contains("AddTechLookLayers();") && sv.Contains("UpdateTechLook();") && sv.Contains("PaintTechLookLive(ci);") && fa.Contains("TierGrade(in x, a);") && fa.Contains("TierGradeLife(in x, a, m);"));

        Console.WriteLine(_fails == 0 ? "\n✔ 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
