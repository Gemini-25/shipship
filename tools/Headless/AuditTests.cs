using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ShipSim.Core;

// v16.23 점검 도구 자체 시험 (--audittest): 탐지기가 꾸민 장면을 실제로 잡는지 · 같은 인자 같은 숫자.
public static partial class Program
{
    private static int RunAuditTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"점검 항해 탐지기 시험 (v16.23) · 시드 {seed}\n");
        var w = World.CreateDefault(seed, 0, "Hanbit");
        w.CrewCanDie = true;
        var run = new AuditRun { Ship = "Hanbit", Seed = seed };
        var probe = new AuditProbe(w, run);
        void Go(int minutes)
        {
            for (int m = 0; m < minutes; m++)
            {
                for (int t = 0; t < SimTime.Minutes(1); t++) w.Step();
                probe.Minute();
            }
        }
        Go(30);

        // ① 급사: 멀쩡하던 사람이 갑자기
        var a = w.Crew.First(c => !c.Dead && !c.Down);
        a.Vitals.Health = 0.0005f;
        Go(2);
        var da = run.Deaths.FirstOrDefault(d => d.Name == a.Name);
        Check("죽음을 잡는다", da != null, $"사망 {run.Deaths.Count}");
        Check("급사는 '대응 없이 죽음'에서 빠진다", da?.Sudden == true, $"Sudden={da?.Sudden}");

        // ② 쓰러진 채 30분 넘게 → 죽음: 쓰러짐 · 쓰러진 시간이 남는다
        var b = w.Crew.First(c => !c.Dead && !c.Down && c != a);
        for (int i = 0; i < 35; i++) { b.Vitals.Health = 0.1f; Go(1); }
        b.Vitals.Health = 0.0005f;
        Go(2);
        var db = run.Deaths.FirstOrDefault(d => d.Name == b.Name);
        Check("쓰러짐을 잡는다", run.Downs.Any(d => d.Name == b.Name), $"쓰러짐 {run.Downs.Count}");
        Check("죽기 전 쓰러진 시간", db != null && db.DownMin >= 20 && !db.Sudden, $"쓰러짐 {db?.DownMin}분 · 급사 {db?.Sudden} · 스스로 {db?.SelfMin} · 곁 {db?.NearMin}");

        // ③ 공황 길이
        var c3 = w.Crew.First(c => !c.Dead && !c.Down);
        c3.Mind.PanicUntil = w.Tick + SimTime.Minutes(20);
        Go(25);
        Check("공황 길이를 잰다", run.PanicMin.Any(x => x is >= 3 and <= 22), $"공황 {string.Join(",", run.PanicMin)}분");

        // ④ 같은 고장 반복 · 고친 직후 다시
        var m = w.Ship.Machines.First(x => x.Faults.Count == 0 && x.Body.Type == FurnitureType.WaterRecycler);
        for (int i = 0; i < 3; i++)
        {
            var f = new Fault { Kind = FaultKind.FilterClogged, Since = w.Tick };
            m.Faults.Add(f);
            Go(2);
            m.Faults.Remove(f);
            Go(3);
        }
        Check("고친 직후 다시 고장", run.Refails.Count(r => r.What.Contains(m.Body.Name)) >= 2, $"재고장 {run.Refails.Count}");

        // ⑤ 정전 · 배터리 바닥 → 보조 발전기 기록
        w.Power.Heat(2000f);
        for (int i = 0; i < 40; i++) { w.Power.BatteryCharge = 0f; Go(1); }
        Go(20);

        probe.End();
        Check("반복 고장 (6시간 안에 3번)", run.Repeats.Any(r => r.Count >= 3 && r.What.Contains(m.Body.Name)), string.Join(" / ", run.Repeats.Select(r => $"{r.What} ×{r.Count}")));
        var aux = run.Aux.FirstOrDefault(x => x.Minutes >= 10);
        Check("정전 · 배터리 바닥을 잡는다", aux != null, string.Join(" / ", run.Aux.Select(x => $"{x.Why} {x.Minutes:0}분 · 켬 {x.AfterMin:0}분 {x.By}")));
        Console.WriteLine($"    (보조 발전기: {(aux == null ? "-" : aux.AfterMin >= 0 ? $"{aux.AfterMin:0}분 만에 {aux.By}" : "안 켬")} · 있음 {aux?.Had})");

        // ⑥ 게임 안 글 낱말
        var hits = new Dictionary<string, int>();
        var ex = new List<string>();
        var seen = new HashSet<string>();
        AuditScanText("트리아지 모듈이 AI로 v16.19 페일세이프를 걸었다", "시험", hits, ex, seen);
        AuditScanText("AIR 필터 · 플라스틱 통 · 예비 간선으로 넘겼다", "시험", hits, ex, seen);
        Check("개발 용어를 잡는다", hits.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(new[] { "AI", "v1x.x", "모듈", "트리아지", "페일세이프" }.OrderBy(k => k, StringComparer.Ordinal)), string.Join(",", hits.Keys));
        Check("세계 안의 말은 안 잡는다 (AIR · 플라스틱)", hits.Values.Sum() == 5, $"{hits.Values.Sum()}");

        // ⑦ 규칙 표가 모두 돈다 · 상위 10 이 나온다
        var ctx = new AuditCtx { Runs = new() { run } };
        var fs = AuditEvaluate(ctx);
        Check("규칙 표 전부 계산", fs.Count == AuditRuleTable.Length && fs.All(f => !f.Summary.StartsWith("규칙 계산 실패")), string.Join(" / ", fs.Where(f => f.Summary.StartsWith("규칙")).Select(f => f.Id)));
        Check("대응 없이 죽음 규칙이 급사를 빼고 센다", fs.First(f => f.Id == "death.noresp").Count <= 1, fs.First(f => f.Id == "death.noresp").Summary);

        // ⑧ 결정론: 같은 인자 같은 숫자 (벽시계 값은 뺀다)
        string J(AuditRun r) { r.Wall = 0; r.SecPerDay = 0; r.Prof.Clear(); return JsonSerializer.Serialize(r, AuditJson); }
        var r1 = AuditOne("Kestrel", seed, 0.3f);
        var r2 = AuditOne("Kestrel", seed, 0.3f);
        Check("결정론 (같은 항해 두 번)", r1.Hash == r2.Hash && J(r1) == J(r2), $"{r1.Hash:x8} / {r2.Hash:x8}");

        Console.WriteLine(_fails == 0 ? "\n점검 탐지기 시험 통과" : $"\n실패 {_fails}개");
        return _fails == 0 ? 0 : 1;
    }
}
