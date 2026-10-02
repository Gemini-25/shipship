using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.23 점검 — 이상 패턴 규칙 표. 규칙 하나 = 한 줄 (Id · 이름 · 낮을수록 좋은가 · 판정 함수).
// 새 규칙은 AuditRuleTable 에 줄 하나를 더하면 보고서 · 상위 10 · 이전 비교에 저절로 들어간다.
// 심각도: 0 정보 · 1 낮음 · 2 보통 · 3 높음 · 4 치명.

public sealed class AFinding
{
    public string Id = "", Name = "", Summary = "", Fix = "";
    public int Sev, Count;
    public double Metric;
    public bool LowerBetter = true;
    public double Target = double.NaN;
    public List<string> Examples = new();
}

public sealed class AuditCtx
{
    public List<AuditRun> Runs = new();
    public HashSet<string> CatalogRooms = new();   // 카탈로그 · 생성기 표본에서 나오는 방 종류
    public int CatalogShips;
    public Dictionary<string, int> SrcHits = new();
    public List<string> SrcEx = new();
    public int SrcFiles;
    public IEnumerable<ADeath> Deaths => Runs.SelectMany(r => r.Deaths);
    public IEnumerable<ACase> Cases => Runs.SelectMany(r => r.Cases);
    public float Days => Runs.Sum(r => r.Days);
    public static string Tag(AuditRun r) => $"{r.Ship}·{r.Seed}";
}

public static partial class Program
{
    /// <summary>Target 이 있으면 "목표에 가까울수록 좋음" (사망 수준처럼 너무 적어도 너무 많아도 나쁜 값).</summary>
    private sealed record AuditRule(string Id, string Name, bool LowerBetter, Func<AuditCtx, AFinding> Eval, double Target = double.NaN);

    private static AFinding F(int sev, int count, double metric, string summary, string fix, IEnumerable<string>? ex = null) =>
        new() { Sev = sev, Count = count, Metric = Math.Round(metric, 4), Summary = summary, Fix = fix, Examples = ex?.Take(6).ToList() ?? new() };

    private static string Hr(float h) { long t = SimTime.Hours(7 + h); return $"{SimTime.Day(t)}일 {SimTime.Clock(t)}"; }

    private static string DeathEx(AuditRun r, ADeath d) =>
        $"{AuditCtx.Tag(r)} {Hr(d.Hour)} · {d.Room} · {d.Name} — {d.Cause} (스스로 대응 {d.SelfMin}분 · 곁 {d.NearMin}분 · 쓰러짐 {d.DownMin}분 · 공황 {d.PanicMin}분{(d.Case != "" ? $" · 사고 \"{d.Case}\"" : "")})";

    private static IEnumerable<(AuditRun r, ADeath d)> DeathsWith(AuditCtx c) => c.Runs.SelectMany(r => r.Deaths.Select(d => (r, d)));

    /// <summary>규모별 사고당 사망 (개인 · 보통(방+계통) · 배 · 우주).</summary>
    private static (int cases, int deaths) ScaleDeaths(AuditCtx c, params IncidentScale[] s)
    {
        var set = s.Select(x => (int)x).ToHashSet();
        var cs = c.Cases.Where(k => set.Contains(k.Peak)).ToList();
        return (cs.Count, cs.Sum(k => k.Deaths));
    }

    private static string ScaleDowns(AuditCtx c, params IncidentScale[] s)
    {
        var set = s.Select(x => (int)x).ToHashSet();
        var cs = c.Cases.Where(k => set.Contains(k.Peak)).ToList();
        int d = cs.Sum(k => k.Downs);
        return $" · 쓰러짐 {d}명 (사고당 {(cs.Count == 0 ? 0 : d / (double)cs.Count):0.00})";
    }

    private static readonly AuditRule[] AuditRuleTable =
    {
        new("death.noresp", "대응 없이 죽음", true, c =>
        {
            var hit = DeathsWith(c).Where(x => !x.d.Sudden && x.d.SelfMin == 0 && x.d.NearMin == 0).ToList();
            int sudden = c.Deaths.Count(d => d.Sudden);
            return F(hit.Count > 0 ? 3 : 0, hit.Count, hit.Count, $"사망 {c.Deaths.Count()}명 중 {hit.Count}명이 죽기 전 30분 동안 본인도 같은 방 누구도 살려는 행동(대피 · 구조 · 비상 작업)을 안 했다 · 급사 {sudden}명은 뺐다",
                "Mind · CrisisCrew: 위험을 알아챘는데 대피/구조로 이어지지 않는 길 · 쓰러진 사람 구조 요청", hit.Select(x => DeathEx(x.r, x.d)));
        }),
        new("death.panic", "공황 중 사망", true, c =>
        {
            var hit = DeathsWith(c).Where(x => x.d.Panic).ToList();
            return F(hit.Count > 0 ? 3 : 0, hit.Count, hit.Count, $"죽기 전 10분 안에 공황이던 사람 {hit.Count}명",
                "CrisisCrew 공황: 위험한 방에서 얼어붙지 않게 · 공황 중에도 출구 쪽으로 · 곁 사람이 끌어내기", hit.Select(x => DeathEx(x.r, x.d)));
        }),
        new("panic.length", "공황 지속 시간", true, c =>
        {
            var all = c.Runs.SelectMany(r => r.PanicMin).OrderBy(x => x).ToList();
            if (all.Count == 0) return F(0, 0, 0, "공황 없음", "");
            int p50 = all[all.Count / 2], p90 = all[Math.Min(all.Count - 1, (int)(all.Count * 0.9))], max = all[^1];
            string dist = $"0~5분 {all.Count(x => x < 5)} · 5~15분 {all.Count(x => x is >= 5 and < 15)} · 15~30분 {all.Count(x => x is >= 15 and < 30)} · 30~60분 {all.Count(x => x is >= 30 and < 60)} · 60분+ {all.Count(x => x >= 60)}";
            return F(p90 > 30 || max > 120 ? 2 : 0, all.Count, p90, $"공황 {all.Count}번 · 중앙 {p50}분 · 90% {p90}분 · 최대 {max}분 · {dist}",
                "공황이 길면 대응할 사람이 빠진다 — 진정(곁 사람 · 경보 해제) 경로", null);
        }),
        new("death.normal", "보통 재해(방 · 계통) 사망 수준", false, c =>
        {
            var (n, d) = ScaleDeaths(c, IncidentScale.Room, IncidentScale.System);
            double avg = n == 0 ? 0 : d / (double)n;
            int sev = n == 0 ? 1 : n >= 10 && d == 0 ? 3 : avg > 0.3 ? 3 : avg > 0.15 ? 2 : 0;
            string judge = n == 0 ? "보통 재해가 한 번도 안 났다" : n >= 10 && d == 0 ? "너무 안전 — 보통 재해로 아무도 안 죽었다 (\"가끔 사망\"이 목표)" : d == 0 ? "아직 사망 없음 (사고 수가 적어 판정 보류)" : avg > 0.3 ? "너무 위험 — 보통 재해가 자주 사람을 죽인다" : "목표 범위 (가끔 사망)";
            return F(sev, d, avg, $"보통 재해 {n}건 · 사망 {d}명 · 사고당 {avg:0.000}{ScaleDowns(c, IncidentScale.Room, IncidentScale.System)} — {judge}",
                d == 0 ? "운석우 · 화재 · 정전 · 배관 파열이 사람을 다치게 하는 길(연기 · 감압 · 감전 · 고립)이 끝까지 가는지 · 대응이 너무 완벽한지" : "보통 재해 대응/대피 속도",
                c.Runs.SelectMany(r => r.Cases.Where(k => k.Peak is 1 or 2 && k.Deaths > 0).Select(k => $"{AuditCtx.Tag(r)} {Hr(k.Hour)} · {k.Room} · {k.Name} — 사망 {k.Deaths}")));
        }, 0.08),
        new("death.ship", "배 전체급 사고 피해", false, c =>
        {
            var (n, d) = ScaleDeaths(c, IncidentScale.Ship);
            double avg = n == 0 ? 0 : d / (double)n;
            int sev = n == 0 ? 1 : avg < 0.1 ? 2 : avg > 3 ? 2 : 0;
            return F(sev, n, avg, n == 0 ? "배 전체급 사고가 한 번도 안 났다" : $"배 전체급 {n}건 · 사망 {d}명 · 사고당 {avg:0.00}{ScaleDowns(c, IncidentScale.Ship)} ({(avg < 0.1 ? "큰 피해가 아니다" : avg > 3 ? "너무 크다" : "목표 범위")})",
                "배 전체급은 \"큰 피해\" — 사망 0.1~3 이 목표", c.Runs.SelectMany(r => r.Cases.Where(k => k.Peak == 3).Select(k => $"{AuditCtx.Tag(r)} {Hr(k.Hour)} · {k.Name} — {k.Hours:0.0}시간 · 사망 {k.Deaths}")));
        }, 1.0),
        new("death.cosmic", "우주급 사고 생존 위기", false, c =>
        {
            var (n, d) = ScaleDeaths(c, IncidentScale.Cosmic);
            double avg = n == 0 ? 0 : d / (double)n;
            return F(n == 0 ? 0 : avg < 0.5 ? 1 : 0, n, avg, n == 0 ? "우주급 사고 없음 (이 기간 · 이 시드)" : $"우주급 {n}건 · 사망 {d}명 · 사고당 {avg:0.00}{ScaleDowns(c, IncidentScale.Cosmic)}{(avg < 0.5 ? " — 생존 위기라기엔 약하다" : "")}",
                "우주급만 진짜 생존 위기", c.Runs.SelectMany(r => r.Cases.Where(k => k.Peak == 4).Select(k => $"{AuditCtx.Tag(r)} {Hr(k.Hour)} · {k.Name} — 사망 {k.Deaths}")));
        }, 2.0),
        new("fault.repeat", "같은 고장이 짧은 시간에 반복", true, c =>
        {
            var hit = c.Runs.SelectMany(r => r.Repeats.Select(x => (r, x))).OrderByDescending(x => x.x.Count).ToList();
            int sev = hit.Count == 0 ? 0 : hit.Count >= c.Runs.Count ? 3 : 2;
            return F(sev, hit.Count, hit.Count / (double)Math.Max(1, c.Runs.Count), $"6시간 안에 같은 설비 · 같은 고장이 3번 넘게: {hit.Count}건 (항해당 {hit.Count / (double)Math.Max(1, c.Runs.Count):0.0})",
                "원인을 안 고치고 증상만 되돌림 — 차단기 다시 올리기 전 원인(과부하 · 누전 · 젖음) 제거 · 같은 고장 세 번째면 다르게", hit.Select(x => $"{AuditCtx.Tag(x.r)} {x.x.Room} · {x.x.What} ×{x.x.Count} ({Hr(x.x.First)}~{Hr(x.x.Last)})"));
        }),
        new("fault.refail", "고친 직후 다시 고장", true, c =>
        {
            var hit = c.Runs.SelectMany(r => r.Refails.Select(x => (r, x))).ToList();
            var top = hit.GroupBy(x => x.x.What).OrderByDescending(g => g.Count()).Select(g => $"{AuditCtx.Tag(g.First().r)} {g.First().x.Room} · {g.Key} ×{g.Count()} (처음 {Hr(g.First().x.Hour)} · {g.First().x.GapMin:0}분 만에)");
            return F(hit.Count == 0 ? 0 : hit.Count > c.Runs.Count * 2 ? 3 : 2, hit.Count, hit.Count / (double)Math.Max(1, c.Runs.Count), $"고친 뒤 30분 안에 같은 고장: {hit.Count}건", "수리 완료 판정 · 원인 제거 확인", top);
        }),
        new("aux.slow", "보조 발전기를 안 켬/늦게 켬", true, c =>
        {
            var eps = c.Runs.SelectMany(r => r.Aux.Where(a => a.Minutes >= 10f && a.Had).Select(a => (r, a))).ToList();
            if (eps.Count == 0) return F(0, 0, 0, "10분 넘는 정전 · 배터리 바닥이 없었다 (또는 보조 발전기 없음)", "");
            var on = eps.Where(x => x.a.AfterMin >= 0f).ToList();
            double rate = on.Count / (double)eps.Count;
            double avg = on.Count == 0 ? 0 : on.Average(x => x.a.AfterMin);
            string by = string.Join(" · ", on.GroupBy(x => x.a.By.StartsWith("사람") ? "사람" : x.a.By).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}"));
            int sev = rate < 0.5 ? 3 : avg > 20 ? 2 : 0;
            return F(sev, eps.Count - on.Count, 1 - rate, $"정전/배터리 바닥 {eps.Count}번 중 보조 발전기 켬 {on.Count}번 ({rate * 100:0}%) · 켜기까지 평균 {avg:0}분 · 누가: {(by == "" ? "-" : by)}",
                "ComputerTriage 보조 발전기 시동 · 사람 StartAux 작업이 정전 때 실제로 잡히는지", eps.Where(x => x.a.AfterMin < 0f || x.a.AfterMin > 20f).Select(x => $"{AuditCtx.Tag(x.r)} {Hr(x.a.Hour)} · {x.a.Why} {x.a.Minutes:0}분 · {(x.a.AfterMin < 0 ? "끝까지 안 켬" : $"{x.a.AfterMin:0}분 만에 {x.a.By}")}"));
        }),
        new("unused.room", "배에 있는데 아무도 안 쓴 방", true, c =>
        {
            var all = c.Runs.SelectMany(r => r.Rooms.Select(x => (r, x))).ToList();
            var idle = all.Where(x => x.x.Dwell == 0 && x.x.Touched == 0).ToList();
            double frac = all.Count == 0 ? 0 : idle.Count / (double)all.Count;
            var byKind = idle.GroupBy(x => x.x.Kind).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{RoomTypes.Name(Enum.Parse<RoomType>(g.Key))} ×{g.Count()} (예 {AuditCtx.Tag(g.First().r)} {g.First().x.Name})");
            return F(frac > 0.1 ? 2 : frac > 0 ? 1 : 0, idle.Count, frac, $"방 {all.Count}개(항해 합) 중 {idle.Count}개({frac * 100:0}%)는 아무도 머물거나 손대지 않았다",
                "방의 쓰임(RoomUse) · 그 방 설비를 쓰는 활동이 실제로 고르는지", byKind);
        }),
        new("unused.roomkind", "방 종류 70 중 안 나온 것", true, c =>
        {
            var kinds = Enum.GetValues<RoomType>().Where(t => t != RoomType.Corridor).ToList();
            var seen = c.Runs.SelectMany(r => r.Rooms.Select(x => x.Kind)).ToHashSet();
            var missRun = kinds.Where(k => !seen.Contains(k.ToString())).ToList();
            var missCat = kinds.Where(k => !c.CatalogRooms.Contains(k.ToString())).ToList();
            return F(missRun.Count > 35 ? 3 : missRun.Count > 15 ? 2 : missRun.Count > 0 ? 1 : 0, missRun.Count, missRun.Count,
                $"점검한 배에 나온 방 종류 {kinds.Count - missRun.Count}/{kinds.Count} · 카탈로그+생성기 표본 {c.CatalogShips}척에서도 안 나오는 종류 {missCat.Count}",
                "ShipGenerator(V16) 용도별 방 목록 · 기본 배 설계도에 새 방 종류 넣기",
                new[] { "점검 배에 없음: " + string.Join(", ", missRun.Select(RoomTypes.Name)), "어디서도 안 나옴: " + (missCat.Count == 0 ? "-" : string.Join(", ", missCat.Select(RoomTypes.Name))) });
        }),
        new("unused.fixture", "아무도 안 만진 설비 종류", true, c =>
        {
            var present = c.Runs.SelectMany(r => r.FixPresent.Keys).ToHashSet();
            var touched = c.Runs.SelectMany(r => r.FixTouched.Keys).ToHashSet();
            var miss = present.Where(p => !touched.Contains(p)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            return F(miss.Count > present.Count / 2 ? 2 : miss.Count > 0 ? 1 : 0, miss.Count, present.Count == 0 ? 0 : miss.Count / (double)present.Count,
                $"배에 있는 설비 종류 {present.Count} 중 {miss.Count}종은 사람 · 로봇 일의 대상이 된 적이 없다 (저절로 도는 설비 포함)",
                "설비를 쓰는 활동 · 정비 주기", new[] { string.Join(", ", miss.Select(m => Enum.TryParse<FurnitureType>(m, out var ft) ? FurnitureTypes.Name(ft) : m)) });
        }),
        new("unused.activity", "한 번도 안 나온 활동", true, c =>
        {
            var ids = Brain.Activities.Select(a => a.Id).Distinct().ToList();
            var seen = c.Runs.SelectMany(r => r.Acts).ToHashSet();
            var miss = ids.Where(i => !seen.Contains(i)).ToList();
            double frac = miss.Count / (double)Math.Max(1, ids.Count);
            return F(frac > 0.3 ? 2 : miss.Count > 0 ? 1 : 0, miss.Count, frac, $"활동 {ids.Count}종 중 {ids.Count - miss.Count}종이 나왔다 · 안 나온 {miss.Count}종",
                "점수(Score)가 늘 0이거나 조건이 안 맞는 활동", new[] { string.Join(", ", miss.Select(i => $"{Brain.Activities.First(a => a.Id == i).Label}({i})")) });
        }),
        new("unused.daily", "안 나온 일상 사건", true, c =>
        {
            var seen = c.Runs.SelectMany(r => r.Daily).ToHashSet();
            var miss = DailySystem.Catalog.Where(s => !seen.Contains(s.Id)).ToList();
            double frac = miss.Count / (double)DailySystem.Catalog.Length;
            return F(frac > 0.5 ? 2 : miss.Count > 0 ? 1 : 0, miss.Count, frac, $"일상 사건 {DailySystem.Catalog.Length}종 중 {DailySystem.Catalog.Length - miss.Count}종이 나왔다",
                "Daily 조건 · 가중치", new[] { string.Join(", ", miss.Take(40).Select(s => s.Name)) });
        }),
        new("unused.scene", "안 나온 일상 장면", true, c =>
        {
            var seen = c.Runs.SelectMany(r => r.Scenes).ToHashSet();
            var miss = Enum.GetValues<SceneKind>().Where(k => !seen.Contains(k.ToString())).ToList();
            return F(miss.Count > 0 ? 1 : 0, miss.Count, miss.Count, $"장면 {Enum.GetValues<SceneKind>().Length}종 중 안 나온 {miss.Count}종", "DailyScenes 시작 조건", new[] { string.Join(", ", miss) });
        }),
        new("unused.tech", "늘어난 기술", false, c =>
        {
            var got = c.Runs.SelectMany(r => r.TechGained).ToHashSet();
            int per = c.Runs.Count == 0 ? 0 : (int)Math.Round(c.Runs.Average(r => r.TechGained.Count));
            return F(got.Count == 0 ? 2 : 0, got.Count, got.Count, $"기술 {TechWeb.Every.Length}종 중 이번 항해들에서 새로 얻은 것 {got.Count}종 (항해당 {per})",
                "연구 · 기술 그물 조건이 항해 중에 실제로 열리는지", new[] { string.Join(", ", got.Take(30).Select(t => TechWeb.Find(t)?.Name ?? t)) });
        }),
        new("unused.incident", "안 나온 사고 종류", true, c =>
        {
            var seen = c.Runs.SelectMany(r => r.Keys).ToHashSet();
            var rows = ScaleTable.All;
            var miss = rows.Where(r => !seen.Contains(r.Key)).ToList();
            double frac = miss.Count / (double)Math.Max(1, rows.Length);
            return F(frac > 0.8 ? 1 : 0, miss.Count, frac, $"사고 도감 {rows.Length}줄 중 {rows.Length - miss.Count}줄이 실제로 났다",
                "이야기꾼 · 위험 표의 고르기", new[] { "안 난 예: " + string.Join(", ", miss.Take(25).Select(r => r.Name)) });
        }),
        new("incident.petty", "자잘한 사고만 많음", true, c =>
        {
            var cs = c.Cases.ToList();
            if (cs.Count == 0) return F(2, 0, 1, "사고가 하나도 없었다", "이야기꾼 · 위험 간격");
            int small = cs.Count(k => k.Peak <= 1), big = cs.Count(k => k.Peak >= 2);
            double share = small / (double)cs.Count;
            int[] bins = new int[5];
            foreach (var k in cs) bins[Math.Clamp(k.Peak, 0, 4)]++;
            double perDay = cs.Count / Math.Max(0.01, c.Days);
            return F(share > 0.85 && big < c.Runs.Count ? 2 : share > 0.9 ? 1 : 0, small, share,
                $"사고 {cs.Count}건 (하루 {perDay:0.0}건) · 개인 {bins[0]} · 방 {bins[1]} · 계통 {bins[2]} · 배 {bins[3]} · 우주 {bins[4]} · 개인+방 비율 {share * 100:0}% · 번진 사고(시작보다 커짐) {cs.Count(k => k.Peak > k.Base)}",
                "작은 사고는 줄이고 계통급 이상(정전 · 배관 파열 · 감압)이 며칠에 한 번은 나게", null);
        }),
        new("incident.chain", "연쇄 길이", true, c =>
        {
            var cs = c.Cases.ToList();
            if (cs.Count == 0) return F(0, 0, 0, "-", "");
            int max = cs.Max(k => k.Chain);
            double avg = cs.Average(k => k.Chain);
            var longest = c.Runs.SelectMany(r => r.Cases.Select(k => (r, k))).OrderByDescending(x => x.k.Chain).Take(3);
            return F(max >= 8 ? 1 : 0, cs.Count(k => k.Chain >= 3), avg, $"연쇄 평균 {avg:0.0}고리 · 최대 {max}고리 · 3고리 이상 {cs.Count(k => k.Chain >= 3)}건", "긴 연쇄는 원인 차단이 늦다는 뜻",
                longest.Select(x => $"{AuditCtx.Tag(x.r)} {Hr(x.k.Hour)} · {x.k.Name} — {x.k.Chain}고리 · 방 {x.k.Spread}개"));
        }),
        new("resource.out", "자원 고갈", true, c =>
        {
            var hit = c.Runs.SelectMany(r => r.ResOut.Select(kv => (r, kv.Key, kv.Value))).OrderBy(x => x.Value).ToList();
            bool vital = hit.Any(x => x.Key is "식량" or "물" or "공기 탱크");
            string mins = string.Join(" · ", new[] { "식량", "물", "공기 탱크", "배터리", "금속판", "실링폼" }.Select(k =>
                $"{k} 최저 {c.Runs.Min(r => r.ResMin.GetValueOrDefault(k)):0.#}"));
            return F(vital ? 3 : hit.Count > 0 ? 2 : 0, hit.Count, hit.Count, $"바닥난 적 {hit.Count}번 · {mins}", "물자 흐름 · 배급 · 재료 만들기", hit.Select(x => $"{AuditCtx.Tag(x.r)} {x.Key} — {Hr(x.Value)}에 바닥 (시작 {x.r.ResStart.GetValueOrDefault(x.Key):0.#})"));
        }),
        new("food.mono", "식량원이 수경 재배뿐", true, c =>
        {
            var h = new Dictionary<string, int>();
            foreach (var r in c.Runs) foreach (var (k, v) in r.Harvest) h[k] = h.GetValueOrDefault(k) + v;
            int all = h.Values.Sum(), hydro = h.GetValueOrDefault(nameof(RoomType.Hydroponics));
            double other = all == 0 ? 0 : 1 - hydro / (double)all;
            float fin = c.Runs.Sum(r => r.FoodIn), fout = c.Runs.Sum(r => r.FoodOut);
            return F(all > 0 && other < 0.1 ? 1 : 0, all - hydro, 1 - other, $"수확 {all}번 · 수경 {hydro} · 그 밖 {all - hydro} ({other * 100:0}%) · 방별 {string.Join(" · ", h.OrderByDescending(kv => kv.Value).Select(kv => $"{RoomTypes.Name(Enum.Parse<RoomType>(kv.Key))} {kv.Value}"))} · 식량 들어옴 {fin:0} / 나감 {fout:0}끼",
                "조류 · 단백질 · 정원 · 원정 · 사냥 같은 다른 식량원이 실제로 쓰이는지", null);
        }),
        new("computer.wrong", "주컴퓨터 판단이 틀림", true, c =>
        {
            var cs = c.Runs.Where(r => r.Comp.Measured).Select(r => r.Comp).ToList();
            if (cs.Count == 0) return F(0, 0, 0, "측정 안 됨 (주컴퓨터 없음)", "");
            int right = cs.Sum(x => x.Right), wrong = cs.Sum(x => x.Wrong), total = cs.Sum(x => x.Total), held = cs.Sum(x => x.Held);
            double rate = right + wrong == 0 ? 0 : wrong / (double)(right + wrong);
            return F(rate > 0.3 ? 2 : rate > 0.15 ? 1 : 0, wrong, rate, $"조치 {total}건 (하루 {total / Math.Max(0.01, c.Days):0.0}) · 맞음 {right} · 틀림 {wrong} ({rate * 100:0}%) · 미룸 {held}",
                "Automation · ComputerTriage 채점에서 틀린 종류부터", c.Runs.Where(r => r.Comp.Measured).OrderByDescending(r => r.Comp.Wrong).Select(r => $"{AuditCtx.Tag(r)} 조치 {r.Comp.Total} · 틀림 {r.Comp.Wrong}"));
        }),
        new("computer.idle", "주컴퓨터가 손 놓은 시간", true, c =>
        {
            var rs = c.Runs.Where(r => r.Comp.Measured).ToList();
            if (rs.Count == 0) return F(0, 0, 0, "측정 안 됨", "");
            double mins = rs.Sum(r => r.Days * 1440.0);
            int off = rs.Sum(r => r.Comp.OfflineMin);
            double frac = off / Math.Max(1.0, mins);
            return F(frac > 0.05 ? 2 : frac > 0.01 ? 1 : 0, off, frac, $"꺼짐 · 재부팅 {off}분 ({frac * 100:0.0}%) · 재부팅 {rs.Sum(r => r.Comp.Reboots)}번 · 과열 {rs.Sum(r => r.Comp.Overheats)}번 · 등급 하락 {rs.Sum(r => r.Comp.GradeDrops)}번",
                "주컴퓨터 과열 · 재부팅 원인", rs.Where(r => r.Comp.OfflineMin > 0).OrderByDescending(r => r.Comp.OfflineMin).Select(r => $"{AuditCtx.Tag(r)} {r.Comp.OfflineMin}분 · 재부팅 {r.Comp.Reboots}"));
        }),
        new("computer.remote", "원격 조치 vs 사람 요청", false, c =>
        {
            var rs = c.Runs.Where(r => r.Comp.Measured).ToList();
            int remote = rs.Sum(r => r.Comp.Remote), asked = rs.Sum(r => r.Comp.Asked);
            double share = remote + asked == 0 ? 0 : remote / (double)(remote + asked);
            return F(0, remote + asked, share, rs.Count == 0 ? "측정 안 됨" : $"스스로 끝낸 조치 {remote} · 사람에게 부탁한 조치 {asked} (원격 {share * 100:0}%)", "", null);
        }),
        new("computer.options", "주컴퓨터가 비교한 후보 수", false, c =>
        {
            var rs = c.Runs.Where(r => r.Comp.Measured).ToList();
            int dec = rs.Sum(r => r.Comp.Decisions), opt = rs.Sum(r => r.Comp.Options);
            double avg = dec == 0 ? 0 : opt / (double)dec;
            return F(dec > 0 && avg < 2 ? 1 : 0, dec, avg, rs.Count == 0 ? "측정 안 됨" : dec == 0 ? "미리 돌려 보기 결정 없음" : $"결정 {dec}번 · 평균 후보 {avg:0.0}개", "ComputerForesee 후보", null);
        }),
        new("bots.robot", "로봇 가동률 · 고장 간격", true, c =>
        {
            int min = c.Runs.Sum(r => r.Bots.RobotMin), act = c.Runs.Sum(r => r.Bots.RobotActive), down = c.Runs.Sum(r => r.Bots.RobotDown), f = c.Runs.Sum(r => r.Bots.RobotFaults), lost = c.Runs.Sum(r => r.Bots.RobotLost);
            if (min == 0) return F(0, 0, 0, "로봇 없음", "");
            double downFrac = down / (double)min;
            double mtbf = f == 0 ? 0 : act / 60.0 / f;
            return F(downFrac > 0.3 || lost > 0 ? 2 : downFrac > 0.1 ? 1 : 0, f, downFrac, $"로봇 항해당 {c.Runs.Average(r => r.Bots.Robots):0.#}대 · 일함 {act * 100.0 / min:0}% · 멈춤/고장 {downFrac * 100:0}% · 고장 {f}번 (일한 {mtbf:0.0}시간마다) · 잃음 {lost}",
                "로봇 고장 · 충전 · 사람이 고쳐 주는지", null);
        }),
        new("bots.drone", "드론 표류 · 잃음", true, c =>
        {
            int min = c.Runs.Sum(r => r.Bots.DroneMin), act = c.Runs.Sum(r => r.Bots.DroneActive), down = c.Runs.Sum(r => r.Bots.DroneDown);
            int f = c.Runs.Sum(r => r.Bots.DroneFaults), adrift = c.Runs.Sum(r => r.Bots.DroneAdrift), lost = c.Runs.Sum(r => r.Bots.DroneLost);
            if (min == 0) return F(0, 0, 0, "드론 없음", "");
            return F(lost > 0 ? 2 : adrift > 0 ? 1 : 0, adrift + lost, adrift + lost, $"드론 일함 {act * 100.0 / min:0}% · 고장/부서짐 {down * 100.0 / min:0}% · 고장 {f}번 · 표류 {adrift}번 · 잃음 {lost}대",
                "드론 배터리 · 귀환 판단", c.Runs.Where(r => r.Bots.DroneAdrift + r.Bots.DroneLost > 0).Select(r => $"{AuditCtx.Tag(r)} 표류 {r.Bots.DroneAdrift} · 잃음 {r.Bots.DroneLost}"));
        }),
        new("perf.day", "하루 시뮬 시간", true, c =>
        {
            var ok = c.Runs.Where(r => r.Error == null).ToList();
            if (ok.Count == 0) return F(0, 0, 0, "-", "");
            var byShip = ok.GroupBy(r => r.Ship).Select(g => (ship: g.Key, crew: g.First().Crew, s: g.Average(r => r.SecPerDay))).OrderByDescending(x => x.s).ToList();
            var prof = ok.SelectMany(r => r.Prof).GroupBy(p => p.Key).Select(g => (g.Key, ms: g.Sum(p => p.Ms) / ok.Count)).OrderByDescending(x => x.ms).Take(5);
            return F(byShip[0].s > 60 ? 1 : 0, byShip.Count, ok.Average(r => r.SecPerDay), $"하루 시뮬 (점검 훅 포함) 가장 느린 배 {byShip[0].ship}({byShip[0].crew}인) {byShip[0].s:0.0}초 · 무거운 계통 (하루 ms, 항해 평균) {string.Join(" · ", prof.Select(p => $"{p.Key} {p.ms:0}"))}",
                "Prof 상위 계통", byShip.Select(x => $"{x.ship}({x.crew}인) {x.s:0.0}초/일"));
        }),
        new("error.exception", "예외 (항해가 멈춤)", true, c =>
        {
            var hit = c.Runs.Where(r => r.Error != null).ToList();
            return F(hit.Count > 0 ? 4 : 0, hit.Count, hit.Count, $"예외로 멈춘 항해 {hit.Count}/{c.Runs.Count}", "스택의 첫 ShipSim 줄", hit.Select(r => $"{AuditCtx.Tag(r)} {Hr(r.ErrorHour)} — {r.Error}"));
        }),
        new("stall.claim", "오래 안 끝나는 일 (무한 대기 · 교착)", true, c =>
        {
            var hit = c.Runs.SelectMany(r => r.Stalls.Select(s => (r, s))).OrderByDescending(x => x.s.Hours).ToList();
            return F(hit.Any(x => x.s.Hours >= 6) ? 3 : hit.Count > 0 ? 2 : 0, hit.Count, hit.Count, $"맡은 사람이 그대로인데 진척이 3시간 넘게 멈춘 일 {hit.Count}건",
                "WorkBoard 클레임 해제 · 막힘(Block) 조건", hit.Select(x => $"{AuditCtx.Tag(x.r)} {Hr(x.s.Hour)}부터 {x.s.Hours:0.0}시간 · {x.s.Room} · {x.s.What} — {x.s.Who}"));
        }),
        new("text.runtime", "게임 안 글에 개발 용어 (실행 중)", true, c =>
        {
            var hits = new Dictionary<string, int>();
            foreach (var r in c.Runs) foreach (var (k, v) in r.TextHits) hits[k] = hits.GetValueOrDefault(k) + v;
            int n = hits.Values.Sum();
            return F(n > 0 ? 3 : 0, n, n, n == 0 ? "기록 · 경보 · 말 · 일기 · 컴퓨터 기록 · 사고 이름에서 못 찾았다" : string.Join(" · ", hits.OrderByDescending(kv => kv.Value).Select(kv => $"\"{kv.Key}\" {kv.Value}종")),
                "그 세계 사람이 쓸 말로 바꾸기 (예비 간선으로 넘겼다 · 급한 곳부터 전기를 돌렸다)", c.Runs.SelectMany(r => r.TextEx).Distinct());
        }),
        new("text.static", "게임 안 글에 개발 용어 (소스 문자열)", true, c =>
        {
            int n = c.SrcHits.Values.Sum();
            return F(n > 0 ? 2 : 0, n, n, $"game/src 의 한국어 문자열 리터럴 {c.SrcFiles}개 파일 훑음 — {(n == 0 ? "없음" : string.Join(" · ", c.SrcHits.OrderByDescending(kv => kv.Value).Select(kv => $"\"{kv.Key}\" {kv.Value}")))} (화면에 안 나가는 문자열도 섞여 있다)",
                "화면 · 기록에 나가는 문자열부터", c.SrcEx);
        }),
        new("ship.weak", "배가 너무 약함", true, c =>
        {
            var weak = c.Runs.Where(r => r.Crew > 0 && (r.Alive < r.Crew * 0.5 || r.RoomsLost >= Math.Max(2, r.RoomsTotal / 5))).ToList();
            double frac = weak.Count / (double)Math.Max(1, c.Runs.Count);
            int dead = c.Runs.Sum(r => r.Crew - r.Alive), crew = c.Runs.Sum(r => r.Crew);
            return F(frac > 0.33 ? 3 : weak.Count > 0 ? 2 : 0, weak.Count, frac, $"절반 넘게 죽거나 방 1/5 넘게 잃은 항해 {weak.Count}/{c.Runs.Count} · 전체 사망 {dead}/{crew}명",
                "선체 · 구조 · 연쇄 차단이 버티는지", weak.Select(r => $"{AuditCtx.Tag(r)} 생존 {r.Alive}/{r.Crew} · 잃은 방 {r.RoomsLost}/{r.RoomsTotal}"));
        }),
        new("death.total", "사망 원인", true, c =>
        {
            var ds = c.Deaths.ToList();
            var causes = ds.GroupBy(d => d.Cause).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} ×{g.Count()}");
            int unattributed = ds.Count(d => d.Scale < 0);
            return F(0, ds.Count, ds.Count / (double)Math.Max(1, c.Runs.Count), $"사망 {ds.Count}명 (항해당 {ds.Count / (double)Math.Max(1, c.Runs.Count):0.00}) · 사고에 안 묶인 죽음 {unattributed}", "", causes);
        }),
    };

    private static List<AFinding> AuditEvaluate(AuditCtx c)
    {
        var list = new List<AFinding>();
        foreach (var rule in AuditRuleTable)
        {
            AFinding f;
            try { f = rule.Eval(c); }
            catch (Exception e) { f = F(1, 0, 0, $"규칙 계산 실패: {e.GetType().Name} {e.Message}", "점검 규칙 고치기"); }
            f.Id = rule.Id;
            f.Name = rule.Name;
            f.LowerBetter = rule.LowerBetter;
            f.Target = rule.Target;
            list.Add(f);
        }
        return list;
    }
}
