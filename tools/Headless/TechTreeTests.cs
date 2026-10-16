using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v23 기술 체계 개편 점검 (--techtreetest):
//   표(시대 7 × 줄기 8 · 곁가지 23 · 옛 142 빠짐없이 한 번) · 크기별 시작 시대 · 진도(며칠에 하나 · 시대는 한두 달) · 이유 · 함장 지시
//   · 겪은 일이 연구를 민다 · 카드(전후 숫자) · 공사로 설치 · 효과가 실제로 · 곁가지(조합 · 겪어야) · 잊기와 되찾기 · 저장.
public static partial class Program
{
    private static int RunTechTreeTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"기술 체계 점검 (v23) · 시드 {seed}\n");
        try
        {
            // 0) 표
            {
                var (covered, missing, twice) = TechTree.Coverage();
                bool grid = Enumerable.Range(1, 7).All(e => Enum.GetValues<Branch>().All(b => TechTree.Main.Count(t => t.Era == e && t.Branch == b) == 1));
                int combos = TechTree.SideTechs.Count(t => t.Combo != null), gates = TechTree.SideTechs.Count(t => t.GateOld != null);
                int forks = TechTree.Main.Count(t => t.Fork != null);
                bool gateNodes = TechTree.SideTechs.Where(t => t.GateOld != null).All(t => TechWeb.Node(t.GateOld!).Gate != null);
                bool comboKnown = TechTree.SideTechs.Where(t => t.Combo != null).All(t => t.Combo!.All(o => TechTree.OwnerOf(o) != null));
                Check("표 — 시대 7 × 줄기 8 = 본 기술 56 · 곁가지 23(숨은 조합 13 · 겪어야 열리는 10) · 갈림길 8 · 옛 기술 142가 빠짐없이 한 번씩 담겼다",
                    TechTree.Main.Length == 56 && grid && combos == 13 && gates == 10 && forks == 8 && missing.Length == 0 && twice.Length == 0 && covered == 142 && gateNodes && comboKnown,
                    $"본 {TechTree.Main.Length} · 조합 {combos} · 조건 {gates} · 갈림길 {forks} · 담김 {covered}" + (missing.Length > 0 ? $" · 빠짐 {string.Join(",", missing)}" : "") + (twice.Length > 0 ? $" · 겹침 {string.Join(",", twice)}" : ""));
            }

            // 1) 크기별 시작 시대: 8명 이하 Ⅰ · 9~14 Ⅱ · 15~24 Ⅲ · 25↑ Ⅳ — 앞 시대 기술을 알고 설비가 그 단계로 지어져 있다
            {
                var rows = new List<string>();
                bool ok = true;
                foreach (var key in new[] { "Kestrel", "Bodeum", "Gorae", "Hanul" })
                {
                    var t = ShipCatalog.All.FirstOrDefault(x => x.Key == key);
                    if (t == null) continue;
                    var w = World.CreateDefault(seed, 0, key);
                    int crew = w.Crew.Count;
                    int want = ShipTiers.EraOf(t.Crew);
                    int reactor = w.Ship.FurnitureOf(FurnitureType.ReactorCore).Select(f => f.Machine!.Tier).DefaultIfEmpty(1).Max();
                    int water = w.Ship.FurnitureOf(FurnitureType.WaterRecycler).Select(f => f.Machine!.Tier).DefaultIfEmpty(1).Min();
                    bool good = w.Eras.Era == want && w.Eras.MainKnown == 8 * (want - 1) && reactor == Tech.Unlocked(w, FurnitureType.ReactorCore) && water == Tech.Unlocked(w, FurnitureType.WaterRecycler);
                    ok &= good;
                    rows.Add($"{t.Name} {crew}명 → {TechTree.Roman(w.Eras.Era)} {TechTree.EraName(w.Eras.Era)} (기술 {w.Eras.MainKnown} · 원자로 {Tech.Roman(reactor)} · 정수기 {Tech.Roman(water)}){(good ? "" : " ✗")}");
                }
                Check("크기별 시작 시대 — 큰 배일수록 앞선 시대에서 떠나고 설비도 그 단계", ok && rows.Count >= 3, string.Join(" · ", rows));
            }

            // 2) 진도: 보통 배가 스무 날 — 작은 기술은 며칠에 하나 · 고른 이유가 연대기에 남는다
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                int k0 = w.Eras.MainKnown + w.Eras.SideKnown;
                float r0 = w.Research;
                var learnedAt = new List<(int day, string name)>();
                int seen = w.Eras.Order.Count;
                for (int d = 0; d < 20; d++)
                {
                    Run(w, SimTime.TicksPerDay);
                    while (seen < w.Eras.Order.Count) { learnedAt.Add((w.Day, TechTree.Find(w.Eras.Order[seen])?.Name ?? w.Eras.Order[seen])); seen++; }
                }
                int got = w.Eras.MainKnown + w.Eras.SideKnown - k0;
                float perDay = (w.Research - r0) / 20f;
                var whys = w.History.Events.Where(e => e.Text.StartsWith("다음 연구 —")).Select(e => e.Text).ToList();
                Check("진도 — 스무 날에 기술 셋 이상 (며칠에 하나) · 고른 이유가 연대기에", got >= 3 && got <= 12 && whys.Count >= got && whys.All(x => x.Contains("·")),
                    $"{w.Crew.Count}명 · 시대 {TechTree.Roman(w.Eras.Era)} · 연구 하루 {perDay:0.0} · 익힘 {got}: {string.Join(", ", learnedAt.Select(x => $"D{x.day} {x.name}"))} · 지금 {w.Eras.ProjectTech?.Name} {w.Eras.Fraction * 100:0}% · 이유 예: {whys.LastOrDefault()}");
            }

            // 3) 겪은 일이 연구를 민다 + 카드의 전후 숫자
            {
                bool was = ShipTiers.On;
                ShipTiers.On = false;
                var w = World.CreateDefault(seed, 0, "Hanbit");
                ShipTiers.On = was;
                Run(w, SimTime.Hours(2));
                w.Eras.Begin("l1", "시험");
                float p0 = w.Eras.Progress;
                w.History.Fires += 2;
                Run(w, SimTime.Hours(1) + 2);
                float p1 = w.Eras.Progress;
                w.History.Meteors += 3; // 다른 줄기: 쌓아 둔다
                Run(w, SimTime.Hours(1) + 2);
                float ins = w.Eras.Insight[(int)Branch.Hull];
                w.Eras.LearnNow("l1");
                var card = w.Eras.Unlocks.Last();
                Check("겪은 일 — 불 두 번이 생명유지 연구를 바로 밀고, 운석은 선체 줄기에 쌓인다 · 카드에 전후 숫자 (불 100% → 70%)",
                    p1 >= p0 + 2.9f && ins >= 2.9f && card.Id == "l1" && card.Lines.Any(l => l.Contains("→") && l.Contains('%')) && card.Tones.Contains(1),
                    $"진척 {p0:0.0} → {p1:0.0} · 선체에 쌓인 것 {ins:0.0} · 카드: {string.Join(" / ", card.Lines)}");
            }

            // 4) 공사로 설치: 기술을 익혀도 설비는 그대로 — 로봇 · 승무원이 재료로 고쳐 단다
            {
                bool was = ShipTiers.On;
                ShipTiers.On = false;
                var w = World.CreateDefault(seed, 0, "Hanbit");
                ShipTiers.On = was;
                foreach (var t in TechTree.Main.Where(t => t.Era == 1)) w.Eras.LearnNow(t.Id);
                w.Eras.LearnNow("f2");
                var beds = w.Ship.FurnitureOf(FurnitureType.GrowBed).Select(f => f.Machine!).ToList();
                bool same = beds.All(m => m.Tier == 1);
                int up = 0;
                for (int d = 0; d < 6 && up == 0; d++) { Run(w, SimTime.TicksPerDay); up = beds.Count(m => m.Tier >= 2); }
                var line = w.Log.Entries.LastOrDefault(e => e.Text.Contains("LED 수경 재배대로 올렸다")).Text;
                Check("공사로 설치 — 개량 재배를 익힌 순간엔 재배대가 그대로, 며칠 안에 사람 · 로봇이 LED 재배대로 고쳐 단다",
                    same && up >= 1, $"재배대 {beds.Count}대 중 {up}대 II · {line}");
            }

            // 5) 효과가 실제로: 진공 에너지 탭 → 원자로 +25% · 초공간 항법 → 항해 +25%
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                float kw0 = w.Power.ReactorRated;
                float mine0 = w.Eras.TreeMul("mining");
                w.Eras.LearnNow("p7");
                w.Eras.LearnNow("w5");
                float kw1 = w.Power.ReactorRated;
                Check("효과 — 진공 에너지 탭: 원자로 정격 +25% · 궤도 채굴 드론망: 채굴 ×1.4 (설비 · 행성 일이 읽는다)",
                    MathF.Abs(kw1 / kw0 - 1.25f) < 0.01f && MathF.Abs(w.Eras.TreeMul("mining") / mine0 - 1.4f) < 0.01f,
                    $"원자로 {kw0:0} → {kw1:0}kW · 채굴 ×{w.Eras.TreeMul("mining"):0.00}");
            }

            // 6) 곁가지: 숨은 조합은 두 재료를 알 때 보이고 · 겪어야 열리는 것은 그 일을 겪으면
            {
                bool was = ShipTiers.On;
                ShipTiers.On = false;
                var w = World.CreateDefault(seed, 0, "Hanbit");
                ShipTiers.On = was;
                var lab = TechTree.Find("side.labsafety")!;
                bool hid = !w.Eras.SideOpen(lab) && w.Eras.Status(lab) == "?";
                w.Eras.LearnNow("c1"); // 점검표 + 연구 노트
                bool open = w.Eras.SideOpen(lab) && w.Eras.Available.Contains(lab);
                var vent = TechTree.Find("side.blastvent")!;
                bool v0 = !w.Eras.SideOpen(vent);
                w.Blast.Stats.Detonations += 1;
                w.TechWeb.Scan();
                bool v1 = w.Eras.SideOpen(vent);
                w.Eras.LearnNow("side.blastvent");
                Check("곁가지 — 실험실 안전 절차는 점검표 · 연구 노트를 함께 익히자 보이고, 폭압 배출구는 폭발을 한 번 겪자 열린다 (힌트: " + vent.Hint + ")",
                    hid && open && v0 && v1 && w.Eras.Known.Contains("blastvent") && w.Eras.Era == 1,
                    $"조합 숨김 {hid} → 보임 {open} · 조건 닫힘 {v0} → 열림 {v1} · 곁가지 익힘 {w.Eras.SideKnown} (시대는 그대로 {w.Eras.Era})");
            }

            // 7) 잊기와 되찾기: 그 솜씨를 아는 사람이 없으면 경고 뒤 열흘에 잊는다 — 다시 배우면 반값 · 보통 배(아는 사람이 있다)는 지킨다
            {
                bool was = ShipTiers.On;
                float keep = EraSystem.KeepSkill;
                ShipTiers.On = false;
                var w = World.CreateDefault(seed, 0, "Hanbit");
                var w2 = World.CreateDefault(seed, 0, "Hanbit");
                ShipTiers.On = was;
                var m1 = TechTree.Find("m1")!;
                w.Eras.LearnNow("m1");
                w2.Eras.LearnNow("m1");
                EraSystem.KeepSkill = 1.01f; // 아무도 그만큼은 모른다 (아는 사람이 모두 떠난 배 — 솜씨 1.00인 사람도 있다)
                for (int d = 0; d < EraSystem.ForgetDays + 2 && w.Eras.Known.Contains("m1"); d++) Run(w, SimTime.TicksPerDay);
                EraSystem.KeepSkill = keep;
                bool lost = !w.Eras.Known.Contains("m1") && !w.Eras.Known.Contains("triage") && w.Eras.Lost.Contains("m1");
                float half = w.Eras.CostOf(m1) / (m1.Cost * EraSystem.CostScale * w.Eras.SizeMul);
                string? warn = w.Eras.Stats.Warnings >= 1 ? $"경고 {w.Eras.Stats.Warnings}번" : null;
                w.Eras.LearnNow("m1");
                bool back = w.Eras.Known.Contains("triage") && !w.Eras.Lost.Contains("m1") && w.Eras.Unlocks.Last().Relearn;
                for (int d = 0; d < EraSystem.ForgetDays + 2; d++) Run(w2, SimTime.TicksPerDay);
                float keeper = w2.Crew.Where(c => !c.Dead && !c.LeftShip).Max(c => c.RawSkill(Skill.Medicine));
                bool kept = w2.Eras.Known.Contains("m1") && w2.Eras.Stats.Forgotten == 0;
                Check("잊기 — 그 솜씨를 아는 사람이 없으면 경고 뒤 응급 분류 · 위생 수칙을 잊고(안에 담긴 응급 분류법도), 다시 배우면 반값 · 아는 사람이 있는 배는 지킨다",
                    lost && MathF.Abs(half - 0.5f) < 0.01f && warn != null && back && w.Eras.Stats.Forgotten == 1 && kept,
                    $"잊음 {lost} (D{w.Day}) · 값 ×{half:0.00} · 경고: {warn} · 되찾음 {back} · 보통 배: 의술 {keeper:0.00} → 지킴 {kept}");
            }

            // 8) 저장 · 되살림 — 새 기술 상태가 그대로
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.Hours(3));
                w.Eras.LearnNow(w.Eras.Available.First().Id);
                var snap = StateSave.Write(w);
                var r = StateSave.Read(snap);
                bool same = r.Eras.Known.SetEquals(w.Eras.Known) && r.Eras.Project == w.Eras.Project && r.Eras.Era == w.Eras.Era && r.Eras.Unlocks.Count == w.Eras.Unlocks.Count;
                Check("저장 — 익힌 것 · 지금 연구 · 시대 · 카드가 그대로 돌아온다", same, $"{snap.Length / 1024}KB · 익힘 {r.Eras.Known.Count} · 연구 {r.Eras.ProjectTech?.Name}");
            }
        }
        catch (Exception ex) { Check("예외 없이", false, ex.ToString()); }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>진도 재기 (--techpace [--ship=X] [--days=N]): 배마다 연구 하루 · 익힌 기술 · 시대.</summary>
    private static int RunTechPace(int seed, string[] args)
    {
        int days = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--days="))?.Split('=')[1], out var dd) ? dd : 30;
        var ships = args.FirstOrDefault(a => a.StartsWith("--ship="))?.Split('=')[1].Split(',') ?? new[] { "Kestrel", "Hanbit", "Gorae" };
        foreach (var key in ships)
        {
            var w = World.CreateDefault(seed, 0, key);
            float r0 = w.Research;
            int m0 = w.Eras.MainKnown, s0 = w.Eras.SideKnown, e0 = w.Eras.Era;
            var marks = new List<string>();
            int seen = w.Eras.Order.Count;
            for (int d = 1; d <= days; d++)
            {
                Run(w, SimTime.TicksPerDay);
                while (seen < w.Eras.Order.Count) { var t = TechTree.Find(w.Eras.Order[seen]); marks.Add($"D{d}{(t?.Side == true ? "*" : "")}{t?.Id}"); seen++; }
                if (d % 10 == 0) Console.WriteLine($"  {key} D{d}: 연구 {w.Research - r0:0} (하루 {(w.Research - r0) / d:0.0}) · 본 {w.Eras.MainKnown - m0} · 곁 {w.Eras.SideKnown - s0} · 시대 {e0}→{w.Eras.Era} · 지금 {w.Eras.Project} {w.Eras.Fraction * 100:0}% · 겪은 일 {w.Eras.Stats.Lessons} · 실험 {w.TechWeb.Stats.Experiments}");
            }
            Console.WriteLine($"{key} {w.Crew.Count(c => !c.Dead)}명: {string.Join(" ", marks)}");
        }
        return 0;
    }
}
