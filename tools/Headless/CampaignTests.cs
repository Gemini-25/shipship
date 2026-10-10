using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v12.9 캠페인: 이어지는 임무 · 갈림길 · 세대선 (나이·짝·출생·성장·노환·기념일)
public static partial class Program
{
    private static int RunCampaignTest(int seed)
    {
        _fails = 0;
        float mode0 = CampaignSystem.ModeValue, year0 = GenerationSystem.YearDaysValue;
        Console.WriteLine($"캠페인 점검 (v12.9) · 시드 {seed}\n");
        try
        {
            // 1) 캠페인: 1장에서 시작해, 첫 항해를 마치면 다음 장으로
            {
                CampaignSystem.ModeValue = 1f;
                var w = World.CreateDefault(seed, 0, "Mirinae");
                CampaignSystem.ModeValue = 0f;
                var cp = w.Campaign;
                bool started = cp.Active && cp.Current?.Chapter == 1;
                int days = 0;
                while (days < 40 && cp.Current?.Chapter == 1) { Run(w, SimTime.TicksPerDay); days++; }
                var done = cp.Done.FirstOrDefault();
                Check("캠페인 — 1장 「첫 항해」를 마치면 2장으로", started && cp.Current?.Chapter == 2,
                    $"{days}일 · {(done.m != null ? $"{done.m.Title} {(done.ok ? "해냄" : "실패")} ({done.note})" : "아직")} · 지금 {cp.Current?.Chapter}장 「{cp.Current?.Title}」 · {cp.Status().text}");
            }
            // 2) 세대선: 가까운 둘이 짝이 되고, 아이가 태어나 자라고, 아이는 일하지 않는다
            {
                CampaignSystem.ModeValue = 2f;
                GenerationSystem.YearDaysValue = 1f;
                var w = World.CreateDefault(seed, 0, "Hanbit");
                CampaignSystem.ModeValue = 0f;
                var adults = w.Crew.Where(c => !c.Dead).Take(4).ToList();
                foreach (var a in adults) { a.Age = 27f; foreach (var b in adults) if (a != b) a.ChangeAffinity(b, 0.9f); }
                int pairs = 0, kidWorked = 0;
                for (int d = 0; d < 16; d++)
                {
                    Run(w, SimTime.TicksPerDay);
                    pairs = w.Crew.Count(c => c.Partner != null) / 2;
                    kidWorked += w.Crew.Count(c => c.IsChild && c.Job?.Order != null);
                }
                var g = w.Generation;
                var kid = w.Crew.FirstOrDefault(c => c.BornAboard);
                Check("세대선 — 짝이 되고 아이가 태어나 자란다, 아이는 일하지 않는다", g.Enabled && pairs >= 1 && g.Births >= 1 && kid != null && kid.Age > 0.5f && kidWorked == 0,
                    $"짝 {pairs} · 태어남 {g.Births} · {(kid != null ? $"{kid.Name} {kid.Age:0.0}살" : "아이 없음")} · 새해 {g.Anniversaries} · 아이가 맡은 일 {kidWorked}");
                // 열네 살이 되면 일을 배운다
                if (kid != null)
                {
                    kid.Age = 13.95f;
                    Run(w, SimTime.Hours(6));
                    Check("세대선 — 열네 살이 되면 일을 시작한다", !kid.IsChild && g.Comings >= 1, $"{kid.Name} {kid.Age:0.0}살 · {CrewRoles.Name(kid.Role)} · 성인식 {g.Comings}");
                }
                // 노환
                var old = w.Crew.First(c => !c.Dead && !c.BornAboard);
                old.Age = 85f;
                float h0 = old.Vitals.Health;
                Run(w, SimTime.TicksPerDay * 2);
                Check("세대선 — 나이가 들면 몸이 약해진다", old.Vitals.Health < h0 || old.Dead, $"{old.Name} {old.Age:0}살 · 체력 {h0 * 100:0} → {old.Vitals.Health * 100:0}%{(old.Dead ? " · 떠났다" : "")}");
            }
            // 4) 2장: 모으는 물건은 개조에 쓰지 않고, 기항지에 판 개수로 센다
            {
                var w = World.CreateDefault(seed, 0, "Mirinae");
                var cp = w.Campaign;
                cp.JumpTo("mine");
                int held = w.Board.Held(ItemKind.Rare);
                w.Voyage.Sold[ItemKind.Rare] = w.Voyage.Sold.GetValueOrDefault(ItemKind.Rare) + 4;
                Run(w, SimTime.Hours(2));
                Check("2장 — 희귀 소재는 개조에 안 쓰고, 기항지에 판 개수로 센다", held == 6 && cp.Current?.Chapter == 3 && w.Board.Held(ItemKind.Rare) == 0,
                    $"붙잡아 둠 {held} · 지금 {cp.Current?.Chapter}장 「{cp.Current?.Title}」");
                cp.JumpTo("trade");
                Check("4장 상선단 — 돈을 모으는 동안 기항지에서 아껴 쓴다", w.Voyage.Reserve == 60f, $"아껴 둘 돈 {w.Voyage.Reserve:0}");
            }
            // 5) 5장 세대선: 배에서 태어난 맏이가 열네 살이 되어 일을 맡으면 캠페인이 끝난다 (배는 계속 간다)
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                var cp = w.Campaign;
                cp.JumpTo("colony");
                var adults = w.Crew.Where(c => !c.Dead).Take(2).ToList();
                var kid = w.AddChild(adults[0], adults[1], new Rng(seed));
                kid.Age = 13.9f;
                string before = cp.Status().text;
                Run(w, SimTime.Hours(12));
                Check("5장 세대선 — 맏이가 열네 살이 되면 다음 세대 · 캠페인 끝", cp.Current == null && cp.Done.LastOrDefault().ok && w.Generation.Enabled,
                    $"{before} → {(cp.Current == null ? "캠페인 끝" : cp.Status().text)} · {kid.Name} {kid.Age:0.0}살");
            }
            // 3) 결정론: 세대선 배도 같은 시드면 같은 역사
            {
                uint H()
                {
                    CampaignSystem.ModeValue = 2f;
                    GenerationSystem.YearDaysValue = 1f;
                    var w = World.CreateDefault(seed, 0, "Mirinae");
                    CampaignSystem.ModeValue = 0f;
                    Run(w, SimTime.TicksPerDay * 4);
                    return SaveGame.StateHash(w);
                }
                uint a = H(), b = H();
                Check("결정론 — 세대선 배도 같은 시드 같은 지문", a == b, $"{a:x8} / {b:x8}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"예외: {e}");
            _fails++;
        }
        finally
        {
            CampaignSystem.ModeValue = mode0;
            GenerationSystem.YearDaysValue = year0;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 캠페인 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
    /// <summary>긴 캠페인: 1장부터 끝까지 (길어야 days일) — 장이 바뀔 때마다, 그리고 열흘마다 한 줄.</summary>
    private static int RunCampaignLong(int days, int seed, string ship, string? from = null)
    {
        float mode0 = CampaignSystem.ModeValue;
        CampaignSystem.ModeValue = 1f;
        var w = World.CreateDefault(seed, 0, ship);
        CampaignSystem.ModeValue = mode0;
        Player.AllowDeath(w, true);
        var cp = w.Campaign;
        if (from != null) cp.JumpTo(from);
        Console.WriteLine($"긴 캠페인 · {ship} · 시드 {seed} · 길어야 {days}일{(from != null ? $" · {cp.Current?.Chapter}장 「{cp.Current?.Title}」부터" : "")}\n");
        int doneSeen = 0, histSeen = 0;
        for (int d = 1; d <= days; d++)
        {
            Run(w, SimTime.TicksPerDay);
            foreach (var e in w.History.Events.Skip(histSeen).Where(e => e.Text.Contains("광맥") || e.Text.Contains("교역 —") || e.Text.Contains("건졌다") || e.Text.Contains("죽었다") || e.Text.Contains("태어났다") || e.Text.Contains("짝이")))
                Console.WriteLine($"        {e.Tick / (float)SimTime.TicksPerDay,5:0.0}일  {e.Text}");
            histSeen = w.History.Events.Count;
            while (cp.Done.Count > doneSeen)
            {
                var (m, ok, tick, note) = cp.Done[doneSeen++];
                Console.WriteLine($"  {tick / (float)SimTime.TicksPerDay,5:0.0}일  {m.Chapter}장 「{m.Title}」 {(ok ? "해냄" : "실패")} — {note}{(m.NextB != null ? $" → {cp.Choice}" : "")}");
            }
            int alive = w.Crew.Count(c => !c.Dead);
            if (d % 10 == 0 || alive == 0)
                Console.WriteLine($"  {d,5}일  {cp.Current?.Chapter}장 {cp.Status().text} · 살아 있음 {alive} (어린이 {w.Crew.Count(c => !c.Dead && c.IsChild)}) · 사망 {w.History.Deaths} · 항해 {w.Voyage.Number} · 돈 {w.Voyage.Credits:0} · 기술 {w.Eras.Known.Count} · {EraSystem.EraName(w.Eras.Era)}");
            if (alive == 0) { Console.WriteLine("  전멸"); break; }
            if (cp.Current == null) { Console.WriteLine("  캠페인 끝"); break; }
        }
        var g = w.Generation;
        Console.WriteLine($"\n세대선: 태어남 {g.Births} · 성인식 {g.Comings} · 노환 {g.Elders} · 기념일 {g.Anniversaries} · 짝 {w.Crew.Count(c => !c.Dead && c.Partner != null) / 2}");
        Console.WriteLine($"사고 {w.Causes.Incidents.Count} · 실수 {w.Life.Stats.Mistakes} · 말다툼 {w.Life.Stats.Arguments} · 지문 {SaveGame.StateHash(w):x8}");
        return 0;
    }
}
