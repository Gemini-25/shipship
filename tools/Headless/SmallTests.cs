using System;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// 압축-라 작은 묶음 시험: v18.0 옷 · 보호구 · 안경 · 손에 익은 공구 / v18.1 편지 / v18.8 배 종류 / v18.9 내기 · 맞바꾸기.
public static partial class Program
{
    private static int RunSmallTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"압축-라 작은 묶음 (옷 · 안경 · 편지 · 배 종류 · 내기) — 시드 {seed}\n");

        // ① 방열복 없이 불길에 든 사람이 더 다친다 (+ 실제 불에서 방열복을 걸친다)
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(10));
            var wear = w.Personal.Wear;
            var up = w.Crew.Where(c => !c.Dead && !c.IsChild && c.IsAwake && !c.Outside).OrderBy(c => c.Id).ToList();
            var a = up[0];
            var b = up[1];
            wear.Of(a).Wearing = Garment.Heat;
            wear.Of(b).Wearing = Garment.Work;
            a.Vitals.Health = b.Vitals.Health = 1f;
            w.Casualty.CrossFire(a, a.Cell, 0.8f);
            w.Casualty.CrossFire(b, b.Cell, 0.8f);
            float la = 1f - a.Vitals.Health, lb = 1f - b.Vitals.Health;
            float ra = wear.RiskMul(a, "불길에 덴 화상"), rb = wear.RiskMul(b, "불길에 덴 화상");
            wear.Of(a).Wearing = Garment.Sleep;
            float rs = wear.RiskMul(a, "불길에 덴 화상");
            wear.Of(a).Wearing = Garment.Work;
            Check("방열복 없이 불길에 든 사람이 더 다친다 (잠옷은 더)", lb > la * 2f && rb > ra * 2f && rs > rb,
                $"방열복 {a.Name} 체력 −{la * 100:0.0}% · 작업복 {b.Name} −{lb * 100:0.0}% · 다칠 확률 배율 방열복 {ra:0.00} / 작업복 {rb:0.00} / 잠옷 {rs:0.00}");

            // 실제 불: 소화기를 들고 가는 사람이 방열복을 걸친다 · 모자라면 그냥 들어간다
            var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Galley or RoomType.Workshop or RoomType.Storage).OrderBy(r => r.Id).First();
            foreach (var cell in room.Cells.Where(c => w.Ship.IsOpenFloor(c)).OrderBy(c => c.Y).ThenBy(c => c.X).Take(4)) w.Fire.Ignite(cell, 0.6f);
            int dons = wear.Stats.HeatDons;
            for (int i = 0; i < 60 && wear.Stats.HeatDons == dons; i++) Run(w, SimTime.Minutes(1));
            var worn = w.Crew.Where(c => wear.Peek(c)?.Wearing == Garment.Heat).Select(c => c.Name).ToList();
            Check("불이 나면 소화기 함 옆 방열복을 걸치고 들어간다 (벌 수가 모자라면 그냥)", wear.Stats.HeatDons > dons,
                $"걸침 {wear.Stats.HeatDons - dons} ({string.Join(", ", worn)}) · 방열복 {wear.HeatSuits}벌 · 없이 들어감 {wear.Stats.NoSuitFights} · 컴퓨터 경고 {wear.Stats.ComputerHeatWarn}");
        }

        // ② 안경이 깨진 사람이 계기 값을 잘못 읽는다 → 주 컴퓨터가 잡고 → 예비 안경으로 바꾼다
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(10));
            var wear = w.Personal.Wear;
            var m = w.Ship.Furniture.Select(f => f.Machine).OfType<Machine>().First(x => x.Condition > 0.3f);
            var c = w.Crew.Where(x => !x.Dead && !x.IsChild && x.IsAwake).OrderBy(x => x.Id).First();
            var o = wear.Of(c);
            o.NeedsGlasses = true;
            o.Eyes = Specs.Good;
            int good = Enumerable.Range(0, 30).Count(_ => { var (s, t) = wear.ReadGauge(c, m); return s != t; });
            o.Eyes = Specs.Cracked;
            var wrong = Enumerable.Range(0, 30).Select(_ => wear.ReadGauge(c, m)).Where(r => r.shown != r.truth).ToList();
            Check("안경이 깨진 사람이 계기 값을 잘못 읽는다 (멀쩡하면 바로 읽는다)", good == 0 && wrong.Count >= 8,
                $"{c.Name} {m.Name}: 멀쩡한 안경 틀림 {good}/30 · 금 간 안경 틀림 {wrong.Count}/30 (예: {string.Join(" · ", wrong.Take(3).Select(r => $"{r.truth}%→{r.shown}%"))})");
            bool caught = false;
            for (int i = 0; i < 20 && !caught; i++) caught = wear.Gauge(c, m) && o.KnowsBlurry;
            int spares = wear.SpareGlasses;
            for (int i = 0; i < 12 && o.Eyes == Specs.Cracked; i++) Run(w, SimTime.Hours(1));
            Check("주 컴퓨터가 감지기 값과 맞춰 보고 다시 읽어 달라 한다 → 안경 탓인 걸 알고 바꾼다", caught && wear.Stats.ComputerCaught + wear.Stats.MateCaught > 0 && o.Eyes is Specs.Good or Specs.Taped,
                $"컴퓨터가 잡음 {wear.Stats.ComputerCaught} · 동료 {wear.Stats.MateCaught} · 안경 {o.Eyes} · 예비 {spares}→{wear.SpareGlasses}");
        }

        // ③ 손에 익은 공구 · 남의 공구는 어색하다 · 하루 동안 손때가 붙는다
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.TicksPerDay);
            var wear = w.Personal.Wear;
            var techs = w.Crew.Where(c => c.Role is CrewRole.Technician or CrewRole.Engineer or CrewRole.Electrician).OrderBy(c => c.Id).ToList();
            var t = techs[0];
            float fresh = wear.OwnToolFeel(t);
            wear.Of(t).ToolHours = 30f;
            float worn = wear.OwnToolFeel(t);
            float hours = techs.Sum(x => wear.Of(x).ToolHours);
            Run(w, SimTime.Hours(2));
            bool applied = techs.Any(x => MathF.Abs(x.ToolFactor - wear.OwnToolFeel(x)) < 1e-4f);
            Check("손에 익은 공구가 빠르고 · 남의 공구는 어색하다 · 하루 일하면 손때가 붙는다", worn > fresh && fresh > 1f && wear.BorrowFeel(t) < 1f && applied && hours > 0f,
                $"{t.Name}: 새 {fresh:0.000} → 손에 익음 {worn:0.000} · 남의 것 {wear.BorrowFeel(t):0.00} · 하루 손때 {hours:0.0}시간 · 일 속도에 들어감 {applied} · 어색 {wear.Stats.Awkward} · {wear.Stats.Summary()}");
        }

        // ④ 통신이 끊긴 동안 쌓인 편지가 복구되자 한꺼번에 들어온다
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(2));
            var mail = w.Personal.Mail;
            mail.Jam = true;
            int before = mail.Stats.Arrived;
            Run(w, SimTime.TicksPerDay * 4);
            int during = mail.Stats.Arrived - before;
            int waiting = mail.Waiting;
            mail.Jam = false;
            Run(w, SimTime.Minutes(30));
            Check("답장이 오지 않던 사람이 통신이 복구되자 밀린 편지를 한꺼번에 받는다", during == 0 && waiting >= 3 && mail.Stats.Batches > 0 && mail.Stats.MaxBatch >= 2 && mail.Stats.ComputerOut > 0,
                $"끊긴 나흘 들어옴 {during} · 중계국에 쌓임 {waiting} · 복구 뒤 한꺼번에 {mail.Stats.Batches}명 (최대 {mail.Stats.MaxBatch}통) · 컴퓨터 끊김 알림 {mail.Stats.ComputerOut} · 돌아옴 알림 {mail.Stats.ComputerBack}");

            // ⑤ 부고: 기분 · 일기 · (답장을 미뤘다면) 후회 · 친구 위로 · 컴퓨터가 근무를 덜어 준다
            var c = w.Crew.Where(x => !x.Dead && !x.IsChild && x.IsAwake && !x.Outside).OrderBy(x => x.Id).First();
            var k = mail.Of(c).First();
            k.Alive = true;
            k.Owed = 1;
            k.OwedSince = w.Tick;
            float sad0 = w.Brain2.Emotions.Get(c, Feeling.Sadness);
            long t0 = w.Tick;
            int regrets = mail.Stats.Regrets;
            mail.Post(c, k, News.Death, 0f);
            for (int i = 0; i < 36 && mail.Stats.Regrets == regrets; i++) Run(w, SimTime.Minutes(10));
            float sad = w.Brain2.Emotions.Get(c, Feeling.Sadness);
            bool wrote = c.Diary.Any(d => d.tick >= t0 && d.text.Contains("부고"));
            Check("부고 소식에 슬퍼하고 일기에 남긴다 · 답장을 미뤘던 사람은 후회한다 · 컴퓨터가 근무를 덜어 준다", sad > sad0 + 0.2f && wrote && mail.Stats.Regrets > regrets && c.ExcusedUntil > w.Tick,
                $"{c.Name}: 슬픔 {sad0:0.00}→{sad:0.00} · 일기 {wrote} · 후회 {mail.Stats.Regrets - regrets} · 위로 {mail.Stats.Comforted} · 근무 덜기 {mail.Stats.ComputerEase} · {mail.Stats.Summary()}");
        }

        // ⑥ 진 내기를 갚으려고 대신 당직을 선다
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(3));
            var bets = w.Personal.Bets;
            var adults = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            var pair = (a: adults[2], b: adults[3]);
            int bet0 = bets.Stats.Bets;
            for (int i = 0; i < 20 && bets.Stats.Bets == bet0; i++) bets.OnCardGame(adults[0], adults[1]);
            Check("카드판에 초콜릿 · 당번이 걸리고 · 못 내면 빚이 된다", bets.Stats.Bets > bet0 && (bets.Stats.PaidOnSpot > 0 || w.Schemes.Debts.Any(d => d.Why.Contains("카드 내기"))),
                $"{bets.Stats.Summary()} · 빚 {string.Join(", ", w.Schemes.Debts.Where(d => d.Amount > 0).Select(d => $"{d.Why} {d.Amount}"))}");
            if (pair.a == null) { Check("진 내기를 갚으려고 대신 당직을 선다", false, "근무가 겹치지 않는 둘이 없다"); }
            else
            {
                var d = bets.AddDebt(pair.a, pair.b, 3, "카드 내기에 건 당번");
                int covers = bets.Stats.Covers;
                for (int i = 0; i < 72 && bets.Stats.Covers == covers; i++) Run(w, SimTime.Hours(1));
                Check("진 내기를 갚으려고 대신 당직을 선다 (빚이 줄고 · 근무표가 바뀌고 · 사이가 좋아진다)", bets.Stats.Covers > covers && d.Amount < 3 && pair.a.CoveringUntil > 0,
                    $"{pair.a.Name}(근무 {pair.a.Schedule.WorkStart:0}시) → {pair.b.Name}(근무 {pair.b.Schedule.WorkStart:0}시) 빚 3→{d.Amount} · 대신 당직 {bets.Stats.Covers - covers} · 컴퓨터 기록 {bets.Stats.ComputerNoted}");
            }
            Run(w, SimTime.TicksPerDay);
            var ms = w.Personal.Mail.Stats;
            Check("편지가 오가고 쉬는 시간에 답장을 쓴다 · 저녁에 같은 방 사람끼리 맞바꾼다", ms.Read > 0 && ms.Replies > 0 && bets.Stats.Barters + bets.Stats.OnCredit > 0,
                $"편지: {ms.Summary()} / 내기: {bets.Stats.Summary()}");
        }

        // ⑦ 모든 뼈대 × 용도 생성 배가 하루를 정상으로 넘긴다
        {
            var bad = new System.Collections.Generic.List<string>();
            int ok = 0;
            var sw = Stopwatch.StartNew();
            foreach (var f in ShipInfos.GenFrames)
                foreach (var p in ShipInfos.GenPurposes)
                {
                    string key = ShipGenerator.KeyFor(p, f, 6, seed);
                    try
                    {
                        var w = World.CreateDefault(seed, 0, key);
                        int n0 = w.Crew.Count(c => !c.Dead);
                        Run(w, SimTime.TicksPerDay);
                        int dead = n0 - w.Crew.Count(c => !c.Dead);
                        if (dead > 0) bad.Add($"{ShipInfos.Name(f)} {ShipInfos.Name(p)} 죽음 {dead}");
                        else ok++;
                    }
                    catch (Exception e) { bad.Add($"{ShipInfos.Name(f)} {ShipInfos.Name(p)}: {e.Message}"); }
                }
            Check("모든 뼈대(직선 · 고리 · 척추 · 바퀴 · 화물 · 누더기) × 용도(일반 … 예인 · 구조 · 급유 · 농업) 생성 배가 하루를 넘긴다", bad.Count == 0,
                $"{ok}/{ShipInfos.GenFrames.Length * ShipInfos.GenPurposes.Length}척 정상 ({sw.Elapsed.TotalSeconds:0}초)" + (bad.Count > 0 ? " · " + string.Join(" / ", bad) : ""));
        }

        // ⑧ 결정론
        uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
        uint h1 = H(), h2 = H();
        Check("결정론 (같은 시드 → 같은 지문)", h1 == h2, $"{h1:x8} / {h2:x8}");

        // ⑨ 성능: 30인 배 하루 — 이 묶음이 쓴 시간 (켬/끔 비교)
        {
            double Day(bool off, out double ms)
            {
                var w = World.CreateDefault(seed, 0, "Cheonma");
                w.Personal.Off = off;
                var sw = Stopwatch.StartNew();
                Run(w, SimTime.TicksPerDay);
                ms = w.Personal.Ms;
                return sw.Elapsed.TotalMilliseconds;
            }
            double offMs = Day(true, out _), onMs = Day(false, out double own);
            double share = own / onMs;
            Check("성능: 30인 배 하루 — 이 묶음 몫이 10% 안", share < 0.10,
                $"끔 {offMs / 1000:0.0}초 · 켬 {onMs / 1000:0.0}초 ({(onMs / offMs - 1) * 100:+0;-0}%) · 이 묶음 {own:0}ms ({share * 100:0.0}%)");
        }

        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
