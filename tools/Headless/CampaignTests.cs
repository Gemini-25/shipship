using System;
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
}
