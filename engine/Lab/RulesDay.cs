using ShipEngine;

namespace Lab;

/// <summary>
/// 평시 하루 — 디딤호(6명 · 로봇 2) 하루를 한 번만 돌려 마음(먹고 자고 일함) · 관계(대화 · 다툼) · 물자(장부 · 사바티에 · 공조 응결)를 함께 본다.
/// 예전엔 세 묶음이 같은 하루를 저마다 돌렸다 (하루 셋 → 하나).
/// </summary>
public static partial class Program
{
    private static void DayRules()
    {
        if (!Do("평시 하루")) return;
        Console.WriteLine("\n[평시 하루 — 디딤호 6명 · 로봇 2]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        double Rh(Sim s)
        {
            var f = s.Get<Field>(); double sum = 0; int n = 0;
            foreach (var room in s.Layout.Rooms) foreach (int c in room.Cells)
                if (f.Pressure(c) > 50) { sum += f.Partial(f.T.gH2O, c) / Water.SatKPa(f.Temp(c)); n++; }
            return n > 0 ? sum / n : 0;
        }

        var day = ShipWorld.Crewed("Didim", 3, 6, 2);
        var m = day.Get<Mind>(); var life = day.Get<Life>();
        var people = m.Agents.Where(a => a.IsPerson).ToList();
        var meals = new int[m.Agents.Count]; var sleep = new int[m.Agents.Count]; var work = new int[m.Agents.Count]; var starving = new int[m.Agents.Count];
        var lastAte = people.ToDictionary(a => a.Id, a => a.C.AteAt);
        // 판단 이관 (바) 감사: 하루 동안 마음이 한 사회적 행동 · 사회가 낸 '행동' 사실을 모두 모은다 (행동은 두 틱 뒤 비우고 · 사실은 고리라 틱마다)
        var acts = new List<SocialAct>(); var deeds = new List<Fact>();
        long actCur = m.ActsTotal, factCur = day.Facts.Total;
        static bool Deed(in Fact f) => f.Kind is FactKind.Talk or FactKind.Fight or FactKind.Sabotage or FactKind.Lie or FactKind.Confessed
            || f.Kind == FactKind.Meeting && f.Size >= 0 || f.Kind == FactKind.Relationship && f.Size == -1 || f.Kind == FactKind.Culture && (int)f.Size == (int)CultureEvent.Nickname;
        var dir = new Director(day.Layout) { KeepRing = false };   // ⑬ 이야기 편집: 날이 바뀌면 지난 하루가 한 장이 된다
        long startTick = day.Tick;
        for (int k = 0; k < Time.Hours(24); k++)
        {
            day.Step();
            dir.Observe(day);
            acts.AddRange(m.ActsSince(actCur)); actCur = m.ActsTotal;
            foreach (var f in day.Facts.Since(factCur))
            {
                if (Deed(f)) deeds.Add(f);
                if (Debug && f.Kind == FactKind.Ignition) Console.WriteLine($"    {Time.Clock(day.Tick)} 불: {string.Join(" ← ", day.Causes.Chain(f.Cause).Select(c => day.Causes.Text(c)))}");
            }
            factCur = day.Facts.Total;
            foreach (var a in people)
            {
                if (a.C.AteAt != lastAte[a.Id]) { meals[a.Id]++; lastAte[a.Id] = a.C.AteAt; }
                if (life[a.C.Body].Asleep != 0) sleep[a.Id]++;
                if (a.C.Goal == GoalKind.Work) work[a.Id]++;
                if (a.C.Hunger >= 0.99f || a.C.Thirst >= 0.9f) starving[a.Id]++;
                if (Debug && k % 375 == 0 && (a.C.Hunger > 0.8f || a.C.Thirst > 0.8f))
                    Console.WriteLine($"    {Time.Clock(day.Tick)} {m.NameOf(a.Id)} 배고픔 {a.C.Hunger:0.00} 목마름 {a.C.Thirst:0.00} 목표 {a.C.Goal} · {m.Thought(a.Id)} · 양분 {life[a.C.Body].Reserve:0.0}");
            }
            if (Debug && k % Time.Hours(1) == 0)
                Console.WriteLine($"    {Time.Clock(day.Tick)} " + string.Join(" · ", people.Select(a => $"{m.NameOf(a.Id)}:{a.C.Goal}@{(m.RoomOf(a.C.Cell) >= 0 ? day.Layout.Rooms[m.RoomOf(a.C.Cell)].Name : "-")}")));
        }

        // ── 마음: 굶거나 목마른 사람 없이 먹고 자고 일한다 · 벽에 금 없음 ──
        {
            double sleepH(int id) => sleep[id] / (double)Time.TicksPerHour;
            // 첫날은 졸림이 사람마다 달라 잠이 하루 경계에 걸린다 — 4~12시간
            bool ok = people.All(a => life[a.C.Body].Alive != 0 && starving[a.Id] < Time.Hours(1) && meals[a.Id] is >= 2 and <= 6 && sleepH(a.Id) is >= 4 and <= 12);
            Check("평시 하루 (디딤호 6명 · 로봇 2): 모두 살아 있고 하루 2~6끼 · 한 시간 넘게 굶주리거나 목마른 사람 없음 · 잠 4~12시간 · 일하고 · 벽에 금이 가지 않는다",
                ok && day.Facts.CountOf(FactKind.WallCrack) == 0 && people.Count(a => work[a.Id] > Time.Hours(2)) >= 4,
                string.Join(" · ", people.Select(a => $"{m.NameOf(a.Id)} 끼니 {meals[a.Id]} 잠 {sleepH(a.Id):0.0}시간 일 {work[a.Id] / (double)Time.TicksPerHour:0.0}시간 굶주린 {starving[a.Id] / (double)Time.TicksPerHour:0.0}시간"))
                + $" · 금 {day.Facts.CountOf(FactKind.WallCrack)}");
        }

        // ── 판단 이관 (바) 감사: 사회가 낸 '행동' 사실(대화 · 모임 열기 · 몸싸움 · 몰래 방해 · 헤어짐 · 별명 · 거짓말 · 털어놓음)마다 그 틱(두 틱 안)에 행위자의 판단 행동이 있다 ──
        {
            bool Backed(in Fact f)
            {
                var (kind, byTarget) = f.Kind switch
                {
                    FactKind.Talk => (SocialActKind.Talk, false), FactKind.Meeting => (SocialActKind.CallMeeting, false), FactKind.Fight => (SocialActKind.Attack, false),
                    FactKind.Sabotage => (SocialActKind.Sabotage, false), FactKind.Relationship => (SocialActKind.BreakUp, false), FactKind.Lie => (SocialActKind.TellLie, false),
                    FactKind.Confessed => (SocialActKind.Confess, false), _ => (SocialActKind.Nickname, true),   // 별명: 사실의 행위자는 불린 이 · 행동은 부른 이의 것
                };
                long t = f.Tick; int who = f.Actor;
                bool talk = f.Kind == FactKind.Talk;   // 대화의 대답(듣는 이의 한 마디)은 같은 '대화' 행동 안에 마음이 정했다 — 행동의 대상
                return acts.Any(x => (x.Kind == kind || talk && x.Kind == SocialActKind.Mediate) && x.Tick <= t && x.Tick >= t - 2 && (byTarget ? x.Target == who : x.Actor == who || talk && x.Target == who));   // 말림도 그 사람의 말 (말린 이의 한 마디가 대화 사실로 남는다)
            }
            var orphans = deeds.Where(f => !Backed(f)).ToList();
            string counts = string.Join(" · ", deeds.GroupBy(f => f.Kind).Select(g => $"{FactNames.Of(g.Key)} {g.Count()}"));
            if (Debug) foreach (var f in orphans.Take(8)) Console.WriteLine($"    판단 없는 행동 사실: {Time.Clock(f.Tick)} {f.Kind} {f.Size} · {m.NameOf(f.Actor)} · {day.Causes.Story(f.Cause)}");
            Check("판단 이관 감사 (평시 하루): 사회가 낸 행동 사실(대화 · 모임 열기 · 몸싸움 · 몰래 방해 · 헤어짐 · 별명 · 거짓말 · 털어놓음)마다 그 틱에 행위자의 판단 행동이 있다 — 사회 엔진은 아무 행동도 지어내지 않는다",
                deeds.Count > 0 && orphans.Count == 0,
                $"행동 사실 {deeds.Count} ({counts}) · 마음의 행동 {acts.Count} · 판단 없는 것 {orphans.Count}{(orphans.Count > 0 ? $" (처음: {FactNames.Of(orphans[0].Kind)} {Time.Clock(orphans[0].Tick)})" : "")}");
        }

        // ── ⑧ 마음 건강: 보통의 하루에는 아무도 우울 · 불안해지지 않고 · 악몽 · 공황 발작 · 되살아남이 없다 ──
        Check("평시 하루의 마음: 아무도 우울 · 불안 0.05를 넘지 않고 · 악몽 · 공황 발작 · 되살아남이 없다",
            people.All(a => a.C.Depress < 0.05f && a.C.Anxiety < 0.05f) && m.Nightmares == 0 && m.PanicAttacks == 0 && m.Flashbacks == 0,
            $"우울 많아야 {people.Max(a => a.C.Depress):0.000} · 불안 {people.Max(a => a.C.Anxiety):0.000} · 충격 {people.Max(a => a.C.Shock):0.00} · 악몽 {m.Nightmares} · 발작 {m.PanicAttacks} · 되살아남 {m.Flashbacks}");

        // ── ⑨ 문화: 보통의 하루에는 금기 · 추모 · 규칙을 어기는 일이 없다 (함께 먹는 때는 하루로는 관습이 되지 않는다 — 사흘이 걸린다) ──
        {
            var so = day.Get<Society>();
            Check("평시 하루의 문화: 금기 · 숨진 이의 침대 · 추모 · 규칙을 어긴 일이 없고 · 하루 만에 관습이 서지 않는다",
                so.Taboos == 0 && so.BedTaboos == 0 && so.Memorials == 0 && so.RuleBreaks == 0 && so.Customs == 0,
                $"금기 {so.Taboos} · 침대 {so.BedTaboos} · 추모 {so.Memorials} · 축하 {so.Celebrations} · 별명 {so.Nicknames} · 관습 {so.Customs} · 어김 {so.RuleBreaks} · 이 배의 관습: {so.CultureLine(day) ?? "없음"}");
        }

        // ── 망: 사람이 탄 하루 끝 — 공급이 수요를 채우고 · 방 온도가 288~300K · 원자로 멀쩡 ──
        {
            var net = day.Get<Network>(); var f = day.Get<Field>();
            var vented = f.L.Rooms.Where(r => net.Vent[r.Id] >= 0).ToList();
            var temps = vented.Select(r => f.Temp(net.Vent[r.Id])).ToList();
            var hottest = vented.OrderByDescending(r => f.Temp(net.Vent[r.Id])).First();
            Check("평시 하루 끝: 공급이 수요를 채우고 · 방 온도가 288~300K · 원자로 멀쩡", net.Supplied >= net.Demanded - 1e-6 && temps.All(t => t > 288 && t < 300) && !net.ReactorScram,
                $"수요 {net.Demanded:0.0}kW · 원자로 {net.ReactorKw:0.0}kW · 냉각 {net.CoolingKw:0}kW · 방 온도 {temps.Min():0.0}~{temps.Max():0.0}K (가장 더운 곳 {hottest.Name}) · 장치 {net.Devices.Count} · 급전선 {net.Feeders.Count}");
        }

        // ── 주컴퓨터 일지: 22시 일지 한 장 · 7시 아침 방송 — 평시엔 '큰 일 없이 지나갔다' ──
        {
            var sm = day.Get<ShipMind>();
            string j = sm.Journal.Count > 0 ? sm.Journal[^1].text : "";
            if (Debug) Console.WriteLine($"    일지:\n      {j.Replace("\n", "\n      ")}\n    아침 방송: {sm.Morning}");
            Check("평시 하루의 주컴퓨터: 22시 일지 한 장('큰 일 없이 지나갔다' · 배 산소 · 식량 · 물 며칠 치) · 7시 아침 방송",
                sm.Journal.Count == 1 && Time.HourOfDay(sm.Journal[0].tick) == 22 && j.StartsWith("큰 일 없이 지나갔다") && j.Contains("식량 ") && j.Contains("물 ") && sm.Morning.StartsWith("좋은 아침입니다."),
                $"일지 {sm.Journal.Count}장 · \"{j.Replace("\n", " / ")}\" · 방송 \"{sm.Morning}\"");
        }

        // ── 관계: 말은 자주 나누고 · 몸싸움은 없다 (갈등은 드물게만 끝까지 간다) ──
        {
            var so = day.Get<Society>();
            if (Debug) foreach (var x in so.Sayings) Console.WriteLine($"    {Time.Clock(x.Tick)} {m.NameOf(x.Speaker)} → {m.NameOf(x.Listener)} {x.Topic}/{x.End} @ {(m.RoomOf(x.Cell) >= 0 ? day.Layout.Rooms[m.RoomOf(x.Cell)].Name : "-")}");
            Check("평소의 하루 (디딤호 6명): 대화는 많고 · 다툼은 드물고 · 몸싸움 · 몰래 방해는 없다",
                so.Talks >= 10 && so.Arguments <= 3 && so.Fights == 0 && so.Sabotages == 0,   // 대화 수는 시드마다 10~16 (화장실 (다) 앞뒤 시드 다섯: 11·16·15·14·11 → 10·15·12·14·13 — 시드 3이 문턱에 걸려 있었다)
                $"대화 {so.Talks} · 다툼 {so.Arguments} · 사과 {so.Apologies} · 소문 {so.Rumors} · 몸싸움 {so.Fights} · 방해 {so.Sabotages} · 가까워짐 {so.Friendships}");
        }

        // ── 물자 장부: 처음 재고 + 적은 흐름 = 지금 재고 · 며칠 버티나 ──
        {
            if (Debug) { var nn = day.Get<Network>(); Console.WriteLine($"    샤워 {m.ShowersTaken} · 틀어 둠 {m.ShowersLeftOn} · 주컴퓨터가 잠금 {day.Get<ShipMind>().ShowersCut} · 계량기 {nn.ShowerMeter.Sum():0}몰 · 씻은 물 {-day.Book.Of(Res.Water, Flow.Washed):0}몰 · 손 {m.HandWashes} · 마신 {m.WaterDrunk:0}몰 · 변기 {m.Toilets} · 줄 {m.Queued} · 돌아섬 {m.QueueGaveUp} · 안내 {day.Get<ShipMind>().WashCalls}/{day.Get<ShipMind>().WashOrders} · 닦기 {m.Scrubbed} · 손질 일 {day.Get<ShipMind>().Log.Count(l => l.text.Contains("변기"))}"); }
            var w = day.Get<Work>(); var b = day.Book;
            double worst = 0; string worstName = "";
            for (int k = 0; k < b.Kinds; k++)
            {
                if (k == (int)Res.O2) continue; // 산소는 아래에서 (공기는 새어 나갈 수 있다)
                double o = Math.Abs(w.Other(day, k)) / Math.Max(1, Math.Abs(w.Stock(k)));
                if (o > worst) { worst = o; worstName = k < Book.Bulk ? ((Res)k).ToString() : m.ItemNames[k - Book.Bulk]; }
            }
            double o2Other = w.Other(day, (int)Res.O2), breathed = -b.Of(Res.O2, Flow.Breathed);
            var (foodDays, _) = w.DaysLeft(day, (int)Res.Food);
            var (waterDays, waterNoMake) = w.DaysLeft(day, (int)Res.Water);
            var smd = day.Get<ShipMind>();
            Check("물자 장부: 디딤호 하루 — 처음 재고 + 적은 흐름 = 지금 재고 (식량 · 물 · 물건 정확히 · 산소는 숨의 1% 안) · 며칠 버티나를 센다 · 샤워 물은 오수 통에서 되찾으니 물 배급을 열지 않는다 (찾아 고친 결함 — 아침 샤워 몇 번에 '30일 아래'로 보였다)",
                worst < 1e-9 && Math.Abs(o2Other) < breathed * 0.01 && foodDays is > 150 and < 400 && waterDays > waterNoMake && smd.RationWaterOn == 0 && m.ShowersTaken >= 3, // 탐사선: 180일 치 포장 식량
                $"가장 큰 어긋남 {worst:0e0} ({worstName}) · 산소 {o2Other:0.0}몰 / 숨 {breathed:0}몰 · 식량 {foodDays:0.0}일 · 물 {waterDays:0}일 (되찾지 못하면 {waterNoMake:0.0}일) · 샤워 {m.ShowersTaken} · 물 배급 {smd.RationWaterOn}");
            Check("평시 하루의 화장실: 줄을 선 이가 없으면 주컴퓨터는 줄 안내를 하지 않는다 (지나가는 이 · 손 씻는 이를 기다리는 이로 치던 결함)",
                m.Queued > 0 || smd.WashCalls + smd.WashOrders == 0, $"줄 {m.Queued} · 안내 {smd.WashCalls}/{smd.WashOrders}");
        }

        // ── 사바티에: 갈라 낸 수소와 걸러 모은 이산화탄소로 가른 물의 절반 가까이를 되찾는다 (ISS — 수소가 모자라 절반을 넘지 못한다) ──
        {
            var b = day.Book;
            double split = -b.Of(Res.Water, Flow.Split), back = b.Of(Res.Water, Flow.Sabatier);
            Check("사바티에: 산소를 만들려고 가른 물의 40~50%를 이산화탄소 + 수소에서 되찾는다",
                split > 0 && back / split is > 0.4 and <= 0.5, $"가른 물 {split * 0.018:0.0}L · 되찾음 {back * 0.018:0.0}L ({back / Math.Max(1e-9, split):0.00}) · 버린 메탄 {day.Get<Network>().VentedCH4 * 0.016:0.00}kg");
        }

        // ── 공조 응결: 제습기가 없는 배도 공조 열교환기가 숨 · 땀의 습기를 물로 모은다 → 습도 60% 아래 ──
        {
            double rh = Rh(day), cond = day.Book.Of(Res.Water, Flow.Condensed);
            Check("공조 응결: 제습기 없는 디딤호 — 열교환기가 하루 몇 L의 습기를 물탱크로 되찾고 · 방 습도는 60% 아래",
                cond * 0.018 > 3 && rh < 0.6, $"모은 물 {cond * 0.018:0.0}L/일 · 평균 습도 {rh * 100:0}%");
        }

        // ── ⑬ 이야기 편집: 날이 바뀌는 순간 지난 하루가 한 장이 된다 (제목 · 하루의 결 · 그 날의 때) ──
        {
            var ch = dir.Chapters.FirstOrDefault(c => !c.Week);
            Check("하루의 장: 날이 바뀌면 지난 하루를 한 장으로 묶는다 — '1일째 — …' 제목 · 하루의 결 · 그 날의 처음부터 날이 바뀐 때까지",
                ch != null && ch.Title.StartsWith("1일째 — ") && ch.Arc.Length > 0 && ch.From == startTick + 1 && ch.To == Time.TicksPerDay && dir.Chapters.Count(c => !c.Week) == 1,
                ch == null ? $"장 없음 (시작 {startTick} · 끝 {day.Tick})" : $"\"{ch.Title}\" · {Time.Clock(ch.From)}~{Time.Clock(ch.To)} · 장면 {ch.Scenes.Count} · {string.Join(" / ", ch.Lines)}");
        }

        // ── 소리 단계 S6: 열 수축의 딱 — 외판이 해를 받고 그늘에 들며 늘고 준다 (너무 잦으면 안 된다) ──
        {
            long ticks = day.Get<Structure>().ThermalTicks;
            Check("열 수축: 디딤호 하루의 딱 소리는 배 전체에서 시간당 몇 번 (하루 20 ~ 1,000번)", ticks >= 20 && ticks <= 1000, $"하루 {ticks}번");
        }

        // ── 음식 향은 쌓이지 않는다 (검토 ④-2): 하루 내내 밥을 지어도 정화기의 숯이 걷는다 — 예전엔 없애는 곳이 없어 이틀 만에 배 전체가 13.5ppm ──
        {
            var fa = day.Get<Field>();
            double mol = 0, ppm = 0; int n = 0;
            for (int c = 0; c < day.Layout.N; c++) if (fa.Moles(c) > 1) { ppm += fa.Ppm(fa.T.gAroma, c); n++; }
            ppm /= Math.Max(1, n);
            mol = fa.Total(fa.T.gAroma);
            if (Debug)
                foreach (var r in day.Layout.Rooms.Select((r, i) => (r, i)).OrderByDescending(x => x.r.Cells.Sum(c => fa.Gas(fa.T.gAroma, c))).Take(5))
                    Console.WriteLine($"    향 {r.r.Name} {r.r.Cells.Sum(c => fa.Gas(fa.T.gAroma, c)):0.0000}몰 · 통풍구 {day.Get<Network>().Vent[r.i]} · 막음 {day.Get<Network>().Damper[r.i]}");
            double held = day.Get<Network>().ScrubHeld()[fa.T.Gases[fa.T.gAroma].Index], caught = held / Math.Max(1e-12, held + mol);
            // 덕트가 배 공기를 정화기로 돌리는 시간 상수는 열 몇 시간 (ISS TCCS도 시간당 15m³ — 하루 남짓) — 끼니마다 오르고 숯이 걷어 2ppm 아래에 머문다
            Check("음식 향이 쌓이지 않는다: 하루 밥을 지은 디딤호에서 낸 향의 4할 넘게 정화기 숯에 붙잡히고 · 배 평균 2ppm 아래 (예전엔 걷는 곳이 없어 나흘에 13.5ppm)",
                m.Cooked > 0 && caught > 0.4 && ppm < 2,
                $"밥 짓기 {m.Cooked} · 숯에 {held:0.000}몰 ({caught:P0}) · 공기에 {mol:0.000}몰 · 배 평균 {ppm:0.00}ppm");
        }

        // ── 긴 왕복 (검토 ⑤-2): 하루 끝(125의 배수 틱 — 주컴퓨터가 물자를 묻기 7틱 앞)에서 찍어 되살린 세계와 원래 세계를 150틱 나란히 —
        //    일과 물자 엔진이 늦게 이으면 되살린 쪽 주컴퓨터만 '마실 물 · 식량 · 산소 0일 치'를 보고 7틱 만에 갈라졌다. 맨 끝에 둔다 (원래 세계를 더 돌린다) ──
        {
            var back = new Sim(day.Setup); back.Load(day.Save());
            long at = day.Tick, diff = -1;
            for (int k = 0; k < 150 && diff < 0; k++) { day.Step(); back.Step(); if (day.Hash() != back.Hash()) diff = day.Tick; }
            Check("긴 왕복: 하루 끝에 되살린 세계가 원래 세계와 150틱 바이트까지 같다 (주컴퓨터가 물자를 묻는 틱을 지나도)", diff < 0,
                $"되살린 틱 {at} (125 나머지 {at % 125})" + (diff < 0 ? "" : $" · 틱 {diff}에 갈라짐"));
        }
    }
}
