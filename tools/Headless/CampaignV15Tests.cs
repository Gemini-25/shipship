using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v15.6 캠페인 임무 30: 4장 뒤로 갈래가 퍼지고 모두 세대선으로 모인다 · 목표는 배 상태로 판정 · 기한 · 보상/대가 · 갈림길
public static partial class Program
{
    private static int RunCampaign15Test(int seed)
    {
        _fails = 0;
        Console.WriteLine($"캠페인 임무 30 점검 (v15.6) · 시드 {seed}\n");
        try
        {
            var all = CampaignSystem.All;
            var byId = new Dictionary<string, Mission>();
            foreach (var m in all) byId.TryAdd(m.Id, m);

            // 1) 목록: 30개 · 이름이 겹치지 않고 · 다음 임무가 모두 있고 · 새 임무는 보상·대가와 끝(기한 또는 저절로 끝나는 목표)이 있다
            {
                var badLinks = all.SelectMany(m => new[] { m.NextA, m.NextB }).Where(x => x != null && !byId.ContainsKey(x)).ToList();
                var noTerms = CampaignV15.Rows.Where(m => m.Reward == MissionPay.None || m.Cost == MissionPay.None || m.RewardN <= 0 || m.CostN <= 0
                    || m.Days <= 0 && m.Goal is not (MissionGoal.Endure or MissionGoal.Arrive)).Select(m => m.Id).ToList();
                var noBranch = all.Where(m => m.NextB != null && (m.BranchA == null || m.BranchB == null)).Select(m => m.Id).ToList();
                int goals = CampaignV15.Rows.Select(m => m.Goal).Distinct().Count();
                Check("목록 — 임무 30 · 이름이 겹치지 않고 다음 임무가 다 있다 · 새 임무는 보상·대가·기한이 있다", all.Length == 30 && byId.Count == 30 && badLinks.Count == 0 && noTerms.Count == 0 && noBranch.Count == 0 && goals >= 12,
                    $"임무 {all.Length} (새 {CampaignV15.Rows.Length}) · 이름 {byId.Count} · 목표 종류 {goals}" + (badLinks.Count > 0 ? $" · 없는 다음: {string.Join(",", badLinks)}" : "")
                    + (noTerms.Count > 0 ? $" · 조건 빠짐: {string.Join(",", noTerms)}" : "") + (noBranch.Count > 0 ? $" · 갈래 이름 빠짐: {string.Join(",", noBranch)}" : ""));
            }

            // 2) 고아 없음: 1장에서 30개 모두 닿고, 다음 임무는 꼭 한 장 뒤 (돌고 돌지 않는다), 끝은 세대선 하나
            {
                var seen = new HashSet<string> { all[0].Id };
                var q = new Queue<string>();
                q.Enqueue(all[0].Id);
                while (q.Count > 0)
                {
                    var m = byId[q.Dequeue()];
                    foreach (var n in new[] { m.NextA, m.NextB })
                        if (n != null && byId.ContainsKey(n) && seen.Add(n)) q.Enqueue(n);
                }
                var orphans = all.Where(m => !seen.Contains(m.Id)).Select(m => m.Id).ToList();
                var ends = all.Where(m => m.NextA == null).Select(m => m.Id).ToList();
                var badStep = all.Where(m => new[] { m.NextA, m.NextB }.Any(n => n != null && byId.TryGetValue(n, out var x) && x.Chapter != m.Chapter + 1)).Select(m => m.Id).ToList();
                var into = all.Where(m => m.Id != all[0].Id).Where(m => !all.Any(p => p.NextA == m.Id || p.NextB == m.Id)).Select(m => m.Id).ToList();
                int forks = all.Count(m => m.NextB != null), chapters = all.Max(m => m.Chapter);
                Check("고아 없음 — 1장에서 30개 모두 닿고 · 장은 한 칸씩 · 끝은 세대선 하나", orphans.Count == 0 && into.Count == 0 && badStep.Count == 0 && ends.SequenceEqual(new[] { "colony" }) && forks >= 8,
                    $"닿음 {seen.Count}/30 · 갈림길 {forks} · {chapters}장까지 · 끝 {string.Join(",", ends)}" + (orphans.Count > 0 ? $" · 고아: {string.Join(",", orphans)}" : "")
                    + (into.Count > 0 ? $" · 들어오는 길 없음: {string.Join(",", into)}" : "") + (badStep.Count > 0 ? $" · 장 건너뜀: {string.Join(",", badStep)}" : ""));
            }

            // 3) 대표 임무: 조건을 맞추면 해내고 · 보상이 배에 들어오고 · 다음 장으로
            {
                var cases = new List<(string id, Action<World> meet, Func<World, (bool, string)> after)>
                {
                    ("convoy", w => w.Voyage.PortsVisited++, w => (w.Voyage.Credits >= 40f + 25f, $"돈 {w.Voyage.Credits:0}")),
                    ("deepfield", w => w.Eras.Known.Add(EraSystem.All.First(t => !w.Eras.Known.Contains(t.Id)).Id), w => (w.Research >= 30f, $"연구 {w.Research:0}")),
                    ("bazaar", w => GiveTo(w, ItemKind.Plate, 6), w => (true, $"금속판 {w.Ship.CountStored(ItemKind.Plate)}")),
                    ("wreck", w => w.Voyage.Salvaged++, w => (true, $"전자 부품 {w.Ship.CountStored(ItemKind.Electronics)}")),
                    ("beacon", w => w.Comms.Rescued++, w => (true, $"구조 {w.Comms.Rescued}")),
                    ("survey", w => w.Campaign.Rewind(4.05f), w => (w.Research >= 40f, $"연구 {w.Research:0}")),
                    ("ledger", w => w.Voyage.Sold[ItemKind.Ice] = w.Voyage.Sold.GetValueOrDefault(ItemKind.Ice) + 8, w => (true, $"금속판 {w.Ship.CountStored(ItemKind.Plate)}")),
                    ("relic", w => w.Voyage.Sold[ItemKind.Rare] = w.Voyage.Sold.GetValueOrDefault(ItemKind.Rare) + 2, w => (true, "")),
                    ("newcomers", w => w.Daily.Stats.Fired += 12, w => (true, $"일상 {w.Daily.Stats.Fired}")),
                    ("vigil", w => { foreach (var c in w.Crew) c.Needs.Stress = 0f; }, w => (true, $"사기 {w.Society.Morale * 100:0}")),
                    ("keepers", w => w.Culture.Stats.Explained++, w => (true, $"받아들임 {w.Culture.Stats.Explained}")),
                    ("harbor", w => w.Voyage.PortsVisited += 2, w => (true, $"돈 {w.Voyage.Credits:0}")),
                    ("census", w => w.AddSurvivor(w.Crew.First(c => !c.Dead).Cell), w => (true, $"승무원 {w.Crew.Count(c => !c.Dead)}")),
                    ("granary", w => GiveTo(w, ItemKind.Structure, 4), w => (true, $"구조재 {w.Ship.CountStored(ItemKind.Structure)}")),
                    ("cradle", w => w.Generation.Births++, w => (w.Generation.Enabled, $"세대선 {(w.Generation.Enabled ? "켜짐" : "꺼짐")}")),
                };
                var lines = new List<string>();
                int passed = 0;
                foreach (var (id, meet, after) in cases)
                {
                    var w = World.CreateDefault(seed, 0, "Mirinae");
                    var cp = w.Campaign;
                    cp.JumpTo(id);
                    var m = cp.Current!;
                    string before = cp.Status().text;
                    // 임무가 시작될 때 생기는 일: 항로 · 신호 · 붙잡아 두는 물건 · 아껴 둘 돈
                    var nextLeg = w.Voyage.Legs.ElementAtOrDefault(w.Voyage.Index + 1);
                    bool begun = m.Id switch
                    {
                        "deepfield" => nextLeg?.Kind == LegKind.Nebula,
                        "wreck" => nextLeg?.Kind == LegKind.Derelict,
                        "harbor" => nextLeg?.Kind == LegKind.Port,
                        "beacon" => w.Comms.SignalOpen,
                        "bazaar" => w.Board.Held(ItemKind.Plate) == w.Ship.CountStored(ItemKind.Plate) + 6,
                        "relic" => w.Board.Held(ItemKind.Rare) == 4,
                        "ledger" => w.Board.Held(ItemKind.Ice) == 10 && nextLeg?.Kind == LegKind.Port && cp.ContractFee() > 0f,
                        "granary" => w.Board.Held(ItemKind.Structure) == w.Ship.CountStored(ItemKind.Structure) + 4,
                        "census" or "lifeline" => w.Comms.SignalOpen,
                        _ => true,
                    };
                    meet(w);
                    for (int h = 0; h < 30 && cp.Done.Count == 0; h++) Run(w, SimTime.Hours(1)); // 사기는 천천히 오른다
                    var last = cp.Done.LastOrDefault();
                    var (good, note) = after(w);
                    bool ok = begun && last.m?.Id == id && last.ok && cp.Current != null && (cp.Current.Id == m.NextA || cp.Current.Id == m.NextB) && good
                        && w.History.Events.Any(e => e.Text.Contains($"「{m.Title}」 — 해냈다 · 보상:"));
                    if (ok) passed++;
                    lines.Add($"{(ok ? "○" : "×")} {m.Chapter}장 {m.Title}({m.Goal}) {before} → {(cp.Current != null ? cp.Current.Id : "끝")}{(note.Length > 0 ? $" · {note}" : "")}{(begun ? "" : " · 시작 효과 없음")}");
                }
                foreach (var l in lines) Console.WriteLine("    " + l);
                Check("대표 임무 — 조건을 맞추면 해내고 보상을 받고 다음 장으로 (시작할 때 항로·신호·비축도)", passed == cases.Count && passed >= 8, $"{passed}/{cases.Count}");
            }

            // 3-1) 비축 임무는 정제기가 모을 만큼 더 만든다 (임무가 없으면 늘 채우던 만큼만)
            {
                int Plates(bool mission)
                {
                    var w = World.CreateDefault(seed, 0, "Mirinae");
                    if (mission) w.Campaign.JumpTo("bazaar");
                    GiveTo(w, ItemKind.MetalOre, 30);
                    Run(w, SimTime.TicksPerDay);
                    return w.Ship.CountStored(ItemKind.Plate);
                }
                int plain = Plates(false), stocked = Plates(true);
                Check("비축 임무 — 정제기가 금속판을 더 만든다", stocked > plain, $"하루 뒤 금속판: 임무 없음 {plain} · 보급항 임무 {stocked}");
            }

            // 4) 기한 · 대가: 기한을 넘기거나 버티다 쓰러지면 놓치고, 대가를 치르고, 그래도 다음 장으로 간다
            {
                var w = World.CreateDefault(seed, 0, "Mirinae");
                var cp = w.Campaign;
                cp.JumpTo("convoy");
                float credits0 = w.Voyage.Credits;
                cp.Rewind(16.2f);
                Run(w, SimTime.Hours(2));
                var a = cp.Done.LastOrDefault();
                bool late = a.m?.Id == "convoy" && !a.ok && a.note.Contains("기한") && MathF.Abs(w.Voyage.Credits - (credits0 - 15f)) < 0.01f && cp.Current?.Chapter == 6;

                var w2 = World.CreateDefault(seed, 0, "Mirinae");
                var cp2 = w2.Campaign;
                cp2.JumpTo("survey");
                var victim = w2.Crew.First(c => !c.Dead);
                float stress0 = w2.Crew.Where(c => !c.Dead && c != victim).Average(c => c.Needs.Stress);
                victim.Vitals.Health = 0.1f; // 쓰러진다
                Run(w2, SimTime.Hours(1));
                var b = cp2.Done.LastOrDefault();
                float stress1 = w2.Crew.Where(c => !c.Dead && c != victim).Average(c => c.Needs.Stress);
                bool fell = b.m?.Id == "survey" && !b.ok && b.note.Contains("쓰러졌다") && cp2.Current?.Id == "relic" && stress1 > stress0 + 0.05f;
                Check("기한·대가 — 기한을 넘기면 위약금, 버티다 쓰러지면 마음이 무겁다 · 그래도 다음 장으로", late && fell,
                    $"상선단 호위: {a.note} · 돈 {credits0:0} → {w.Voyage.Credits:0} · 지금 {cp.Current?.Id} | 미지 탐사: {b.note} · 스트레스 {stress0 * 100:0} → {stress1 * 100:0} · 지금 {cp2.Current?.Id}");
            }

            // 5) 갈림길: 배 사정이나 결과에 따라 다음 임무가 다르다
            {
                string After(string id, Action<World> setup, Action<World> meet)
                {
                    var w = World.CreateDefault(seed, 0, "Mirinae");
                    w.Campaign.JumpTo(id);
                    setup(w);
                    meet(w);
                    Run(w, SimTime.Hours(1));
                    return w.Campaign.Current?.Id ?? "끝";
                }
                // 물자: 금속판·먹을 것이 넉넉하면 난파선 수색, 모자라면 보급항
                string poor = After("convoy", w => Life.Take(w, ItemKind.Plate, w.Ship.CountStored(ItemKind.Plate)), w => w.Voyage.PortsVisited++);
                string rich = After("convoy", w => { GiveTo(w, ItemKind.Plate, 20); GiveTo(w, ItemKind.Meal, 80); }, w => w.Voyage.PortsVisited++);
                // 다친 사람: 있으면 의무실, 없으면 난파선 더 안쪽
                string hurt = After("wreck", w => NeedsSystem.AddInjury(w.Crew.First(c => !c.Dead).Vitals, 0.6f, "시험"), w => w.Voyage.Salvaged++);
                string fine = After("wreck", w => { foreach (var c in w.Crew) { c.Vitals.Injury = 0f; c.Vitals.Wounds.Clear(); } }, w => w.Voyage.Salvaged++);
                // 결과: 건지면 새 식구, 신호가 끊기면 기억하는 배
                string saved = After("beacon", _ => { }, w => w.Comms.Rescued++);
                string lost = After("beacon", _ => { }, w => w.Campaign.Rewind(3.2f));
                // 옛 길에서 새 갈래로: 교역의 길 → 상선단 호위 · 탐사의 길 → 깊은 성운
                string trade = After("trade", _ => { }, w => w.Voyage.Credits = 60f);
                string explore = After("explore", _ => { }, w => { foreach (var t in EraSystem.All.Where(t => !w.Eras.Known.Contains(t.Id)).Take(2).ToList()) w.Eras.Known.Add(t.Id); });
                Check("갈림길 — 물자·다친 사람·결과에 따라 다음 임무가 다르다 · 옛 길은 새 갈래로 잇는다",
                    poor == "bazaar" && rich == "wreck" && hurt == "clinic" && fine == "relic" && saved == "newcomers" && lost == "vigil" && trade == "convoy" && explore == "deepfield",
                    $"물자 모자람 {poor} / 넉넉 {rich} · 다침 {hurt} / 멀쩡 {fine} · 건짐 {saved} / 놓침 {lost} · 교역 {trade} · 탐사 {explore}");
            }

            // 6) 결정론: 캠페인 배가 같은 시드면 같은 지문 (새 임무가 항로를 틀고 신호를 받아도)
            {
                (uint, string) H()
                {
                    float mode0 = CampaignSystem.ModeValue;
                    CampaignSystem.ModeValue = 1f;
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    CampaignSystem.ModeValue = mode0;
                    w.Campaign.JumpTo("deepfield");
                    Run(w, SimTime.TicksPerDay + SimTime.Hours(6));
                    return (SaveGame.StateHash(w), $"{w.Campaign.Current?.Id} · {w.Campaign.Status().text}");
                }
                var a = H();
                var b = H();
                Check("결정론 — 캠페인 배도 같은 시드 같은 지문", a == b, $"{a.Item1:x8} / {b.Item1:x8} · {a.Item2}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 캠페인 임무 30 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>창고(끼니는 냉장고 먼저)에 물건을 넣는다.</summary>
    private static void GiveTo(World w, ItemKind k, int n)
    {
        int put = 0;
        foreach (var box in w.Ship.Containers.Where(f => f.Storage!.Accepts(k)).OrderBy(f => f.Type == FurnitureType.Fridge ? 0 : 1))
        {
            put += box.Storage!.Add(k, n - put);
            if (put >= n) break;
        }
    }
}
