using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v18.15 가치관 · 딜레마 · 늦게 돌아오는 결과
public static partial class Program
{
    private static void SetValues(ValueSystem vs, CrewMember c, float safety, float commune, float rule, float mercy)
    {
        var o = vs.Of(c);
        o.V[0] = safety; o.V[1] = commune; o.V[2] = rule; o.V[3] = mercy;
    }

    private static int RunValueTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"가치관 · 딜레마 · 결정 장부 점검 (v18.15) · 시드 {seed}\n");
        string only = Environment.GetEnvironmentVariable("SHIPSIM_SCENE") ?? "";
        bool Do(string k) => only == "" || only.Contains(k);

        Check("딜레마 표 20종 이상", DilemmaTable.All.Length >= 20, $"{DilemmaTable.All.Length}종");
        Check("딜레마 열쇠가 겹치지 않는다", DilemmaTable.All.Select(d => d.Key).Distinct().Count() == DilemmaTable.All.Length, "");
        Check("딜레마마다 그림이 다르다", DilemmaTable.All.Select(d => d.Motif).Distinct().Count() == DilemmaTable.All.Length, $"{DilemmaTable.All.Select(d => d.Motif).Distinct().Count()}가지");
        Check("급한 딜레마와 회의 딜레마가 섞였다", DilemmaTable.All.Count(d => d.Urgent) >= 6 && DilemmaTable.All.Count(d => !d.Urgent) >= 10, $"급함 {DilemmaTable.All.Count(d => d.Urgent)}");
        Check("결과가 몇 주 뒤 돌아오는 딜레마", DilemmaTable.All.Count(d => d.RetMax >= 14) >= 8, $"{DilemmaTable.All.Count(d => d.RetMax >= 14)}종");

        // ── 1) 가치관: 사람마다 다르고 · 큰 가치관 · 경력을 따른다
        if (Do("1"))
        {
            var w = DayOne(seed, "Hanbit");
            Run(w, SimTime.Minutes(20));
            var ad = w.Crew.Where(c => !c.Dead && !c.IsChild).ToList();
            var vs = w.Values;
            Check("가치관: 모두에게 네 축", ad.All(c => vs.Peek(c) != null), $"{ad.Count}명");
            float spread = ad.Max(c => vs.Of(c).V[3]) - ad.Min(c => vs.Of(c).V[3]);
            Check("가치관: 사람마다 다르다 (동정 ↔ 냉정 폭)", spread > 0.4f, $"{spread:0.00}");
            var ppl = ad.Where(c => c.Value == CrewValue.People).ToList();
            var eff = ad.Where(c => c.Value == CrewValue.Efficiency).ToList();
            if (ppl.Count > 0 && eff.Count > 0)
                Check("가치관: '사람 먼저'가 '효율 먼저'보다 동정 쪽", ppl.Average(c => vs.Of(c).V[3]) > eff.Average(c => vs.Of(c).V[3]), $"{ppl.Average(c => vs.Of(c).V[3]):0.00} / {eff.Average(c => vs.Of(c).V[3]):0.00}");
            var line = vs.CardLine(ad[0]);
            Check("카드: 수치 없이 사람 말로", line != null && !System.Text.RegularExpressions.Regex.IsMatch(line, @"\d%|\d\.\d|[+-]\d") && !line.Contains("가치관") && !line.Contains("승인"), line ?? "-");
        }

        // ── 2) 구조 신호를 외면 → 동정 높은 사람이 싫어함 → 몇 주 뒤 기항지에서 생존자를 만나 배 안 분위기가 바뀐다
        if (Do("2"))
        {
            var w = DayOne(seed, "Hanbit");
            var vs = w.Values;
            Run(w, SimTime.Minutes(20));
            var cap = w.Command.Captain;
            Check("선장이 있다", cap != null, cap?.Name ?? "-");
            if (cap != null)
            {
                SetValues(vs, cap, 0.7f, -0.3f, 0.3f, -0.9f); // 냉정하고 일정을 지키는 선장
                var ad = w.Crew.Where(c => !c.Dead && !c.IsChild && c != cap).OrderBy(c => c.Id).ToList();
                var kind = ad[0];
                SetValues(vs, kind, -0.3f, 0.5f, 0f, 0.95f); // 동정이 깊은 사람
                for (int i = 1; i < ad.Count; i++) if (i % 3 == 1) SetValues(vs, ad[i], 0.6f, -0.2f, 0.2f, -0.6f);
                TrimFood(w, 2.4f); // 먹을 것이 빠듯하다 — 배를 돌리면 늦는다
                w.Policies.Set("rescue", 1);
                Hazards.Apply(w, HazardKind.RescueSignal, default, -1);
                Check("구조 요청을 받았다", w.Comms.SignalOpen, $"생존자 {w.Comms.SignalSurvivors}명");
                Run(w, SimTime.Minutes(40));
                var d = vs.Dilemmas.LastOrDefault(x => x.Spec.Key == "distress");
                Check("구조 신호가 딜레마가 됐다 (배 사정이 빠듯해서)", d != null, d?.Detail ?? "-");
                if (d != null)
                {
                    Check("급한 일이라 선장이 몇 사람에게 묻고 정했다", d.Stage == 2 && d.By == cap.Id && d.Voices.Count >= 1, $"{vs.ByName(d.By)} · 물어본 사람 {d.Voices.Count}명");
                    Check("냉정한 선장이 일정대로 간다", d.ChoseA == false, d.Choice);
                    Check("주 컴퓨터가 셈을 내놓았다", d.Computer.Contains("추진제"), d.Computer);
                    Run(w, SimTime.Minutes(20));
                    Check("신호가 끊겼다 (실제로 외면했다)", !w.Comms.SignalOpen && w.Comms.SignalsMissed > 0, $"놓친 신호 {w.Comms.SignalsMissed}");
                    var ko = vs.Of(kind);
                    Check("동정 높은 사람이 싫어한다", ko.Last is { Liked: false } && ko.Captain < 0f, $"{kind.Name}: {ko.Last?.Why} · 선장에 대한 마음 {ko.Captain:+0.00;-0.00}");
                    string? card = vs.CardLine(kind);
                    Check("카드에 자연스러운 말로", card != null && (card.Contains("안 됐다") || card.Contains("마음에 안 든다")), card ?? "-");
                    var cold = ad.Where(c => vs.Of(c).V[3] < -0.5f).ToList();
                    Check("냉정한 사람은 좋아한다", cold.Count > 0 && cold.Count(c => vs.Of(c).Last is { Liked: true }) * 2 >= cold.Count, $"{cold.Count(c => vs.Of(c).Last is { Liked: true })}/{cold.Count}");
                    var v = vs.Ledger.FirstOrDefault(x => x.Dilemma == d.Id);
                    Check("결정 장부에 적혔다 · 몇 주 뒤에 돌아온다", v != null && v.ReturnAt - v.Tick >= SimTime.TicksPerDay * 14 && v.AtPort, v != null ? $"{(v.ReturnAt - v.Tick) / SimTime.TicksPerDay}일 뒤 · 기항지" : "-");
                    if (v != null)
                    {
                        // 몇 주가 흘렀다 (그 사이는 건너뛴다) → 기항지에 닿는다
                        v.ReturnAt = w.Tick + SimTime.Hours(1);
                        Run(w, SimTime.Hours(3));
                        Check("기항지가 아니면 돌아오지 않는다", !v.Returned, "");
                        float stress0 = w.Crew.Where(c => !c.Dead && !c.IsChild).Average(c => c.Needs.Stress);
                        float trust0 = w.Command.Trust, tilt0 = w.Automation.Character.PeopleTilt, kindCap0 = vs.Of(kind).Captain;
                        int motions0 = w.Motions.All.Count(m => m.Policy == "rescue");
                        var sided = v.For.Select(id => w.Crew[id]).Where(c => !c.Dead && c != cap).ToList();
                        float mercy0 = sided.Count > 0 ? sided.Average(c => vs.Of(c).V[3]) : 0f;
                        w.Voyage.Force(LegKind.Port, enter: true);
                        Run(w, SimTime.Hours(3));
                        Check("기항지에서 그 캡슐의 생존자를 만났다", v.Returned && v.ReturnText.Contains("생존자"), v.ReturnText);
                        Check("외면에 섰던 사람 · 선장의 죄책감", vs.Of(cap).Conscience > 0.3f && sided.Count(c => vs.Of(c).Conscience > 0.2f) * 2 >= Math.Max(1, sided.Count), $"선장 · {sided.Count(c => vs.Of(c).Conscience > 0.2f)}/{sided.Count}");
                        Check("외면에 섰던 사람이 동정 쪽으로 기운다", sided.Count == 0 || sided.Average(c => vs.Of(c).V[3]) > mercy0, $"{mercy0:0.00} → {(sided.Count > 0 ? sided.Average(c => vs.Of(c).V[3]) : 0f):0.00}");
                        Check("반대했던 사람은 선장에게 더 실망", vs.Of(kind).Captain < kindCap0, $"{kindCap0:+0.00;-0.00} → {vs.Of(kind).Captain:+0.00;-0.00}");
                        Check("배 안 분위기가 무거워졌다 · 선장 신임이 내려갔다", w.Command.Trust < trust0, $"신임 {trust0:0.00} → {w.Command.Trust:0.00} · 스트레스 {stress0:0.00} → {w.Crew.Where(c => !c.Dead && !c.IsChild).Average(c => c.Needs.Stress):0.00}");
                        Check("꿈거리가 남았다", w.Crew.Count(c => w.After.Peek(c) is AfterMind am && am.Seen.Any(s => s.Key == $"ledger:{v.Id}")) >= 3, "");
                        Check("주 컴퓨터가 사람 쪽으로 기운다", w.Automation.Character.PeopleTilt > tilt0, $"{tilt0:+0.00;-0.00} → {w.Automation.Character.PeopleTilt:+0.00;-0.00}");
                        Check("회의를 바꾼다: 구조 방침을 바꾸자는 안건", w.Motions.All.Count(m => m.Policy == "rescue") > motions0, w.Motions.All.LastOrDefault(m => m.Policy == "rescue")?.Title ?? "-");
                        Check("연대기에 남았다", w.History.Events.Any(e => e.Text.Contains("생존자")), "");
                        Check("식당 벽에 남은 말", vs.Keepsakes.Count > 0, vs.Keepsakes.LastOrDefault().text ?? "-");
                    }
                }
            }
        }

        // ── 3) 산소가 모자라다: 의견이 가치관대로 갈리고 긴급 회의에서 표결
        if (Do("3"))
        {
            var w = DayOne(seed, "Hanbit");
            var vs = w.Values;
            Run(w, SimTime.Minutes(20));
            var ad = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            for (int i = 0; i < ad.Count; i++)
                if (i % 2 == 0) SetValues(vs, ad[i], 0.3f, 0.5f, 0f, 0.8f); else SetValues(vs, ad[i], -0.5f, -0.4f, 0f, -0.7f);
            var hurt = ad[1];
            hurt.Vitals.Injury = 0.35f;
            var d = vs.Raise("air_short", hurt.Id, -1, "공기 탱크가 30%다");
            Check("산소 딜레마가 회의에 올라갔다", d != null && d.Stage == 1 && d.Motion >= 0, d != null ? $"단계 {d.Stage}" : "-");
            if (d != null)
            {
                for (int i = 0; i < 60 * 14 && d.Stage < 2; i++) Run(w, 60);
                var m = w.Motions.Get(d.Motion);
                Check("긴급 회의에서 표결로 정했다", d.Stage == 2 && d.By == -2 && m is { Decided: >= 0 }, $"{d.Choice} · {m?.Outcome}");
                if (m != null && m.Initial.Count > 0)
                {
                    int agree = 0, n = 0;
                    foreach (var kv in m.Initial)
                    {
                        if (kv.Key == m.Proposer) continue;
                        float lean = vs.Lean(w.Crew[kv.Key], d.Spec.Vec);
                        if (MathF.Abs(lean) < 0.15f) continue;
                        n++;
                        if (MathF.Sign(lean) == MathF.Sign(kv.Value)) agree++;
                    }
                    Check("의견이 가치관대로 갈렸다", n >= 3 && agree >= n * 0.75f, $"{agree}/{n}");
                    int yes = m.Final.Count(x => x.Value > 0f), no = m.Final.Count(x => x.Value <= 0f);
                    Check("양쪽 다 손을 들었다 (갈림)", yes > 0 && no > 0, $"찬성 {yes} · 반대 {no}");
                    Check("회의에서 컴퓨터가 셈을 말했다 (표는 없다)", m.Item?.Computer?.Contains("공기") == true, m.Item?.Computer ?? "-");
                    var speech = m.Item?.Speeches.FirstOrDefault(s => s.Who != m.Proposer);
                    Check("발언에 가치관의 말이 실린다", m.Item != null && m.Item.Speeches.Count > 0, speech?.Text ?? "-");
                }
                var lost = ad.Where(c => vs.Of(c).Last is { Liked: false, By: -2 }).ToList();
                Check("진 쪽은 회의 결정을 싫어한다", lost.Count > 0, $"{lost.Count}명");
            }
        }

        // ── 4) 쌓인 반발 → 선장에게 털어놓고 · 불신임 안건
        if (Do("4"))
        {
            var w = DayOne(seed, "Hanbit");
            var vs = w.Values;
            Run(w, SimTime.Minutes(20));
            var cap = w.Command.Captain;
            if (cap != null)
            {
                SetValues(vs, cap, -0.8f, -0.6f, -0.3f, -0.9f); // 일만 아는 선장
                var ad = w.Crew.Where(c => !c.Dead && !c.IsChild && c != cap).OrderBy(c => c.Id).ToList();
                var sore = ad.Take(Math.Max(4, ad.Count / 2)).ToList();
                foreach (var c in sore) { SetValues(vs, c, 0.7f, 0.6f, 0.2f, 0.9f); c.ChangeAffinity(cap, -0.2f); }
                foreach (var c in ad.Skip(sore.Count)) SetValues(vs, c, -0.5f, 0f, 0f, -0.5f);
                foreach (var key in new[] { "night_repair", "share_water", "bad_news", "turn_back" })
                {
                    vs.Raise(key, key == "turn_back" ? sore[^1].Id : -1, -1, "");
                    Run(w, SimTime.Minutes(40));
                }
                var decided = vs.Dilemmas.Where(x => x.Stage == 2 && x.By == cap.Id).ToList();
                Check("선장이 급한 일을 잇달아 정했다", decided.Count >= 3, string.Join(" · ", decided.Select(x => x.Choice)));
                var ko = vs.Of(sore[0]);
                Check("결정마다 싫어함이 쌓인다", ko.Dislikes >= 3 && ko.Captain < -0.4f, $"싫어함 {ko.Dislikes} · 선장에 대한 마음 {ko.Captain:+0.00;-0.00}");
                Check("카드: 요즘 함장 결정이 마음에 안 든다", vs.CardLine(sore[0])?.Contains("함장 결정이 마음에 안 든다") == true, vs.CardLine(sore[0]) ?? "-");
                Run(w, SimTime.Hours(3));
                var conf = w.Motions.All.FirstOrDefault(m => m.Kind == MotionKind.Confidence && m.Target == cap.Id);
                Check("쌓인 반발이 불신임 안건이 됐다", conf != null && sore.Any(c => c.Id == conf.Proposer), conf != null ? $"{conf.Title} — {conf.Why}" : "-");
                Check("털어놓은 사람이 있다", sore.Any(c => vs.Of(c).Confided >= 0), "");
                if (conf != null)
                {
                    var (sv, sw) = vs.Opinion(sore[1], conf) ?? (0f, "");
                    Check("불신임 의견에 지난 결정이 근거로", sv > 0.2f && sw.Length > 0, sw);
                }
                Check("컴퓨터가 선장에게 귀띔했다", vs.Stats.Warnings > 0, $"{vs.Stats.Warnings}번");
            }
        }

        // ── 5) 밀항자: 처리에 따라 관계가 바뀐다
        if (Do("5"))
        {
            var w = DayOne(seed, "Hanbit");
            var vs = w.Values;
            Run(w, SimTime.Minutes(20));
            var ad = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            for (int i = 0; i < ad.Count; i++)
                if (i % 3 == 0) SetValues(vs, ad[i], 0.4f, -0.3f, 0.8f, -0.6f); else SetValues(vs, ad[i], -0.2f, 0.4f, -0.5f, 0.8f);
            var d = vs.Stowaway();
            Check("화물칸에서 사람이 나왔다", d != null && w.Crew[d.Subject].Joined?.Contains("화물칸") == true, d?.Detail ?? "-");
            if (d != null)
            {
                var sw = w.Crew[d.Subject];
                Check("숨어 지낸 자리가 남았다", vs.Nests.Count > 0, "");
                for (int i = 0; i < 60 * 14 && d.Stage < 2; i++) Run(w, 60);
                Check("회의에서 정했다", d.Stage == 2, d.Choice);
                var pro = ad.Where(c => vs.Stance(c, d).s > 0.2f).ToList();
                var con = ad.Where(c => vs.Stance(c, d).s < -0.2f).ToList();
                float ap = pro.Count > 0 ? pro.Average(c => sw.AffinityTo(c)) : 0f, ac = con.Count > 0 ? con.Average(c => sw.AffinityTo(c)) : 0f;
                Check("밀항자는 편들어 준 사람과 가까워지고 반대한 사람과 서먹하다", pro.Count > 0 && con.Count > 0 && ap > ac, $"{ap:+0.00;-0.00} / {ac:+0.00;-0.00}");
                if (d.ChoseA == true) Check("함께 가기로 했다", sw.Joined!.Contains("함께"), sw.Joined!);
                else
                {
                    Check("다음 기항지에 내려놓기로 했다", vs.DropAtPort(sw), "");
                    w.Voyage.Force(LegKind.Port, enter: true);
                    Run(w, SimTime.Minutes(30));
                    Check("기항지에서 내렸다", sw.Away && vs.Of(sw).Left, "");
                }
                Check("규칙 쪽 사람은 결정에 따라 반응했다", ad.Count(c => vs.Of(c).Recent.Any(r => r.Verdict == d.Verdict)) >= 3, "");
            }
        }

        // ── 6) 주 컴퓨터가 정한다 (선장이 없고 맡겨 둔 배) · 결과가 돌아오면 컴퓨터 성격이 바뀐다
        if (Do("6"))
        {
            var w = DayOne(seed, "Hanbit");
            var vs = w.Values;
            Run(w, SimTime.Minutes(20));
            var cap = w.Command.Captain;
            w.Policies.Set("computerask", 0);
            if (cap != null) cap.Away = true; // 선장이 자리에 없다
            var d = vs.Raise("trust_machine", -1, -1, "");
            Run(w, SimTime.Minutes(40));
            if (cap != null) cap.Away = false;
            Check("선장이 없으면 권한이 있는 주 컴퓨터가 정한다", d != null && d.Stage == 2 && d.By == -1, d != null ? $"{vs.ByName(d.By)} — {d.Choice}" : "-");
            Check("컴퓨터의 결정에도 사람마다 반응 (컴퓨터에 대한 마음)", w.Crew.Count(c => vs.Peek(c) is Outlook o && o.Recent.Any(r => r.By == -1)) >= 2, "");
            var v = d != null ? vs.Ledger.FirstOrDefault(x => x.Dilemma == d.Id) : null;
            if (v != null)
            {
                float c0 = w.Automation.Character.Caution;
                vs.ReturnNow(v);
                Check("며칠 뒤 컴퓨터가 맞았는지 돌아온다", v.Returned && v.ReturnText.Length > 0, v.ReturnText);
                Check("돌아온 결과가 컴퓨터 성격을 민다", MathF.Abs(w.Automation.Character.Caution - c0) > 0.01f, $"{c0:+0.00;-0.00} → {w.Automation.Character.Caution:+0.00;-0.00}");
            }
        }

        // ── 8) 저절로: 열흘 항해에서 딜레마가 생기고 · 정해지고 · 사람들이 반응한다
        if (Do("8"))
        {
            var w = DayOne(seed, "Hanbit");
            for (int day = 0; day < 10; day++) Run(w, SimTime.TicksPerDay);
            var vs = w.Values;
            foreach (var d in vs.Dilemmas) Console.WriteLine($"   · {SimTime.Clock(d.Raised)} {d.Spec.Name} — {d.Detail} → {vs.ByName(d.By)}: {d.Choice} (단계 {d.Stage})");
            Console.WriteLine($"   {vs.Stats.Line()}");
            Check("저절로 생긴 딜레마가 정해졌다", vs.Dilemmas.Any(d => d.Stage == 2), $"{vs.Dilemmas.Count}건");
            Check("사람마다 반응이 쌓였다", vs.Stats.Likes > 0 && vs.Stats.Dislikes > 0, vs.Stats.Line());
            Check("가치관이 겪은 일로 조금씩 움직였다", vs.Stats.Shifts > 0, $"{vs.Stats.Shifts}");
        }

        // ── 7) 결정론 · 성능
        if (Do("7"))
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint h1 = H(), h2 = H();
            Check("결정론 (같은 시드 · 같은 지문)", h1 == h2, $"{h1:x8} / {h2:x8}");
            if (Environment.GetEnvironmentVariable("SHIPSIM_NOPERF") != "1")
            {
                double Day(bool off)
                {
                    ValueSystem.Off = off;
                    var w = World.CreateDefault(seed, 0, "Cheonma");
                    Run(w, SimTime.Hours(2));
                    ValueSystem.UpdateTicks = 0;
                    var sw = Stopwatch.StartNew();
                    Run(w, SimTime.TicksPerDay);
                    ValueSystem.Off = false;
                    return sw.Elapsed.TotalSeconds;
                }
                double t0 = Math.Min(Day(true), Day(true)), t1 = Math.Min(Day(false), Day(false));
                double own = ValueSystem.UpdateTicks * 1.0 / Stopwatch.Frequency;
                Console.WriteLine($"   성능 (30인 하루): 끔 {t0:0.0}초 · 켬 {t1:0.0}초 ({(t1 / t0 - 1) * 100:+0;-0}%) · 이 시스템 틱 {own * 1000:0}ms");
                Check("성능 · 30인 배 하루 ±10%", t1 < t0 * 1.10, $"{t0:0.0} → {t1:0.0}초");
            }
        }
        return _fails == 0 ? 0 : 1;
    }
}
