using ShipEngine;

namespace Lab;

/// <summary>⑪ 일 · 물자 (바퀴 2) — 배급 · 몰래 먹기 · 보관함 무게 · 막힌 곳 · 물 회수율. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void SupplyRules()
    {
        if (!Do("물자의 모자람")) return;
        Console.WriteLine("\n[⑪ 물자의 모자람]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = Worlds.MindLegend; legend['L'] = "FoodLocker"; legend['f'] = "Refinery"; legend['R'] = "WaterRecycler";
        // 왼쪽 방: 정제기(f) · 물 재생기(R) · 식량 보관함(L) — 오른쪽 방에도 보관함 하나
        var lay = Worlds.Ascii(Worlds.MindShipComputer.Replace("#..p.b....D.............#", "#..p.bf.R.D.............#")
                                                      .Replace("#.........#....a....a.r.#", "#......L..#....a....a.r.#"), legend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int left = lay.Room[At(3, 2)];
        string Lines(Sim s, long from = 0) => string.Join(" / ", s.Get<ShipMind>().Log.Where(l => l.tick >= from).Select(l => l.text));
        void SetFood(Sim s, double mol)
        {
            var f = s.Get<Field>();
            foreach (var fu in lay.Furniture) foreach (int c in fu.Cells) f.TakeStore(c, f.T.sFood, 1e9);
            var disp = lay.Furniture.First(fu => fu.Type == "MealDispenser");
            f.AddStore(disp.Cells[0], f.T.sFood, mol);
        }

        // ── 배급: 식량이 30일을 못 버티면 주컴퓨터가 배급을 열고 · 배식기는 사람마다 하루 몫을 넘게 주지 않는다(장부가 여섯 시간 쌓인 뒤 판단) · 몫을 다 먹고 배고프면 짜증 ──
        {
            (double maxAte, double meanAte, double ration, float anger, long opened, string log, string root, bool supply) Run(bool ration)
            {
                var s = Worlds.Full(lay, 3);
                var m = s.Get<Mind>(); var sm = s.Get<ShipMind>();
                sm.Policy.RationBelow = ration ? 30 : 0; sm.Policy.RationShare = 0.4f;   // 시험이라 몫을 작게 (반나절 안에 드러나게)
                var ps = new[] { m.AddPerson(s, At(3, 2)), m.AddPerson(s, At(6, 8)), m.AddPerson(s, At(17, 3)) };
                SetFood(s, 60);                                                     // 셋이 닷새 먹을 양
                foreach (int p in ps) { m.SetPersonality(p, 0.5f, 0.8f, 0.5f, 0.5f, 0.5f); m[p].C.Hunger = 0.7f; }   // 성실한 이들 (몰래 먹지 않는다)
                double[] atOpen = new double[ps.Length]; long openedAt = -1;
                for (int k = 0; k < Time.Hours(14); k++)
                {
                    s.Step();
                    if (openedAt < 0 && sm.RationsOpened > 0) { openedAt = s.Tick; atOpen = ps.Select(p => m.AteToday(p)).ToArray(); }
                }
                double[] ate = ps.Select(p => m.AteToday(p)).ToArray();
                // 배급을 연 뒤로는 하루 몫을 넘게 받지 않는다 (연 때 이미 몫보다 먹었으면 그 뒤로는 아무것도)
                bool kept = openedAt < 0 || ps.Select((p, i) => ate[i] <= Math.Max(m.RationFood, atOpen[i]) + 1e-6).All(x => x);
                var rf = s.Facts.Since(0).FirstOrDefault(f => f.Kind == FactKind.Rationed && f.Cause != 0);
                string root = rf.Kind == FactKind.Rationed ? s.Causes.Story(rf.Cause) : "";
                bool supply = rf.Kind == FactKind.Rationed && s.Causes.Chain(rf.Cause).Any(id => s.Causes[id].Kind == CauseKind.Supply);
                if (ration && !kept) return (99, 0, m.RationFood, 0, sm.RationsOpened, Lines(s), root, supply);
                if (Debug) Console.WriteLine($"    배급 연 때 {(openedAt >= 0 ? Time.Clock(openedAt) : "-")} · 그때 먹은 양 {string.Join(",", atOpen.Select(x => x.ToString("0.00")))}");
                float anger = ps.Average(p => m[p].C.Anger);
                if (Debug) Console.WriteLine($"    배급 {(ration ? "있음" : "없음")}: 몫 {m.RationFood:0.00} · 먹은 양 {string.Join(",", ate.Select(x => x.ToString("0.00")))} · 짜증 {anger:0.00} · 못 받음 {m.Refused} · {Lines(s)}");
                long shedSays = sm.Log.Count(l => l.text.StartsWith("전력이 모자라")), sheds = s.Facts.CountOf(FactKind.LoadShed);
                if (shedSays > sheds) return (ate.Max(), ate.Average(), m.RationFood, anger, -shedSays, Lines(s), root, supply);   // 끌 것이 없는데 되풀이해 말했다
                return (ate.Max(), ate.Average(), m.RationFood, anger, sm.RationsOpened, Lines(s), root, supply);
            }
            var on = Run(true); var off = Run(false);
            Check("배급: 식량이 30일을 못 버티면 주컴퓨터가 배급을 열고(방송) · 배식기는 사람마다 하루 몫을 넘게 주지 않는다(장부가 여섯 시간 쌓인 뒤 판단) · 몫을 다 먹고 배고프면 짜증 — 배급이 없던 같은 배는 더 먹는다 (전지가 바닥나도 끌 것이 없으면 '껐다'를 되풀이하지 않는다 · 막힌 곳 '식량'은 날 수가 바뀌어도 한 번만 방송)",
                on.opened == 1 && on.log.Contains("배급합니다") && on.log.Split(" / ").Count(l => l.StartsWith("막힌 곳: 식량")) == 1 && on.ration > 0 && on.maxAte < 99 && off.opened == 0 && off.meanAte > on.meanAte * 1.3 && on.anger > off.anger + 0.05f,
                $"배급 연 번 {on.opened} (음수면 '껐다'를 되풀이) · 몫 {on.ration:0.00}몰 · 가장 많이 먹은 이 {on.maxAte:0.00} · 평균 {on.meanAte:0.00} (배급 없음 {off.meanAte:0.00}) · 짜증 {on.anger:0.00} / {off.anger:0.00}");
            Check("배급의 뿌리는 일감판이 찾은 막힌 곳 (⑪ → ⑩): 배급을 연 원인 사슬이 '막혔다: 식량 … ← 까닭'(물자의 마디)에 잇는다",
                on.supply && on.root.Contains("막혔다: 식량") && on.root.Contains("배급을 열었다"), $"\"{on.root}\"");
        }

        // ── 의료 몫: 물 배급 중에도 덴 이(몸의 물 6% 모자람)는 하루 몫을 넘겨 모자란 만큼 마신다 (고치기 전: 몫에 막혀 목이 말라 죽었다) ──
        {
            var s = Worlds.Full(lay, 8);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, At(3, 2));
            s.Run(2);
            m.SetRation(0, 20, 0);                                    // 물 배급: 하루 20몰 (0.36L)
            ref var b = ref life[m[p].C.Body];
            b.Water = Mind.BodyWater0 * 0.94; b.Burn = 0.35f;          // 덴 팔 — 몸의 물 6% 모자람 · 화상으로 물을 잃는다
            s.Run(Time.Hours(2));
            double drank = m.DrankToday(p);
            float water = (float)(life.Bodies[m[p].C.Body].Water / Mind.BodyWater0);
            Check("의료 몫: 물 배급 중에도 덴 이(몸의 물 6% 모자람)는 하루 몫(20몰)을 넘겨 모자란 만큼 마신다",
                drank > 60 && water > 0.96f, $"두 시간에 마신 물 {drank:0}몰 (몫 20) · 몸의 물 {water:P0}");
        }

        // ── 물도 같은 길: 물 재생기가 망가지고 물탱크가 이틀 치면 물 배급(보통의 85% — 목숨 몫 밑으로는 줄이지 않는다) · 마시는 곳은 몫을 넘게 주지 않는다 ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var sm = s.Get<ShipMind>(); var net = s.Get<Network>();
            var ps = new[] { m.AddPerson(s, At(3, 2)), m.AddPerson(s, At(6, 8)), m.AddPerson(s, At(17, 3)) };
            foreach (var d in net.Devices.Where(d => d.Def.Role == "recycler")) net.Age(d.Id, Part.Mechanical, 1.0);
            net.WaterTank = 3 * ShipMind.WaterPerDay * 2;                      // 셋이 이틀 마실 물
            s.Run((int)Time.Hours(23));                                         // 0시 전까지 (몫은 0시마다 새로)
            double most = ps.Max(p => m.DrankToday(p));
            if (Debug) Console.WriteLine($"    물: 몫 {m.RationWater:0} · 마신 양 {string.Join(",", ps.Select(p => m.DrankToday(p).ToString("0")))} · 물탱크 {net.WaterTank:0} · 식량 몫 {m.RationFood:0.00}");
            Check("물도 같은 길: 물 재생기가 망가지고 물탱크가 이틀 치면 물 배급(보통의 85% — 목숨 몫 밑으로는 줄이지 않는다) · 마시는 곳은 하루 몫을 넘게 주지 않는다 (몫까지 마신 이가 있다)",
                sm.RationWaterOn == 1 && Math.Abs(m.RationWater - ShipMind.WaterShare * ShipMind.WaterPerDay) < 1e-6 && most <= m.RationWater + 1e-6 && most > m.RationWater - 1 && m.RationFood == 0,
                $"물 몫 {m.RationWater:0}몰 · 가장 많이 마신 이 {most:0}몰 · 식량 배급 {(m.RationFood > 0 ? "있음" : "없음")}");
        }

        // ── 몰래 먹기는 성격대로: 몫을 다 먹고 배고픈 덜 성실 · 덜 다정한 이는 보관함에서 몰래 한 끼 · 성실하고 다정한 이는 참는다 ·
        //    주컴퓨터는 보관함 무게로 '몫 밖으로 빈 끼니'만 안다 (누가 했는지는 모른다) · 곁에 사람이 있으면 더 참았다가 한다 ──
        {
            (long pilfered, float hungerAt, long missing, string log, float grudge, long seen) Run(bool sly, bool watched)
            {
                var s = Worlds.Full(lay, 4);
                var m = s.Get<Mind>(); var sm = s.Get<ShipMind>(); var so = s.Get<Society>();
                sm.Policy.RationBelow = 0;                                          // 배급은 시험이 연다 (주컴퓨터에 요청하는 길과 같다)
                int p = m.AddPerson(s, At(3, 7));
                m.SetPersonality(p, 0.5f, sly ? 0.1f : 0.9f, 0.5f, sly ? 0.1f : 0.8f, 0.5f);
                int w = -1;
                if (watched) { w = m.AddPerson(s, At(4, 4)); m.SetPersonality(w, 0.5f, 0.9f, 0.3f, 0.5f, 0.3f); m[w].C.Station = lay.Furniture.First(f => f.Type == "Battery").Id; }
                s.Run(5);
                m.SetRation(0.01, 0, 0);
                int ws = (int)Time.HourOfDay(s.Tick);
                m[p].C.ShiftHour = (ws + 12) % 24;                                  // 일하는 시간이 아니다
                if (w >= 0) m[w].C.ShiftHour = ws;                                 // 지켜보는 이는 그 방에서 일한다
                m[p].C.Hunger = 0.66f;
                float hungerAt = -1;
                for (int k = 0; k < Time.Hours(4); k++)
                {
                    s.Step();
                    foreach (int q in new[] { p, w }) if (q >= 0) { ref var c = ref m[q].C; c.Sleepy = 0; c.Thirst = 0; c.Bored = 0; c.Lonely = 0; if (q == w) c.Hunger = 0; }
                    if (hungerAt < 0 && m.Pilfered > 0) hungerAt = m[p].C.Hunger + 0.0f;
                }
                if (hungerAt < 0 && m.Pilfered > 0) hungerAt = 1;
                // 먹고 난 배고픔이 아니라 꺼내기 직전의 배고픔: 사실의 때에서 거슬러 센다
                var fact = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Pilfer);
                float grudge = w >= 0 ? so.Grudge[w * so.N + p] : 0;
                if (Debug) Console.WriteLine($"    {(sly ? "덜 성실" : "성실")} · {(watched ? "지켜봄" : "혼자")}: 몰래 {m.Pilfered} · 꺼낸 때 {(fact.Kind == FactKind.Pilfer ? Time.Clock(fact.Tick) : "-")} · 원망 {grudge:0.00} · 본 일 {so.PilferSeen} · {m.Thought(p)} · {Lines(s)}");
                return (m.Pilfered, fact.Kind == FactKind.Pilfer ? (float)(fact.Tick) : -1, sm.MissingFound, Lines(s), grudge, so.PilferSeen);
            }
            var sly = Run(true, false); var honest = Run(false, false);
            string name = "";
            {
                var s0 = Worlds.Full(lay, 4); var m0 = s0.Get<Mind>(); name = m0.NameOf(m0.AddPerson(s0, At(3, 7)));
            }
            Check("몰래 먹기는 성격대로: 몫을 다 먹고 배고픈 덜 성실 · 덜 다정한 이는 식량 보관함에서 몰래 한 끼 · 성실하고 다정한 이는 참는다 · 주컴퓨터는 '몫 밖으로 빈 끼니'만 안다 (이름은 모른다)",
                sly.pilfered >= 1 && honest.pilfered == 0 && sly.missing >= 1 && sly.log.Contains("몫 밖으로") && !sly.log.Split(" / ").Any(l => l.Contains("몫 밖으로") && l.Contains(name)),
                $"덜 성실 {sly.pilfered}번 · 성실 {honest.pilfered}번 · 주컴퓨터가 안 빈 끼니 {sly.missing}번");
            var seen = Run(true, true);
            Check("곁에 사람이 있으면 더 참았다가 한다 · 본 이는 원망한다 (성실할수록 크게)",
                seen.pilfered >= 1 && seen.hungerAt > sly.hungerAt && seen.seen >= 1 && seen.grudge > 0.15f,
                $"혼자 {Time.ToSeconds((long)sly.hungerAt) / 60:0}분 · 지켜볼 때 {Time.ToSeconds((long)seen.hungerAt) / 60:0}분에 꺼냄 · 본 이의 원망 {seen.grudge:0.00}");
        }

        // ── 막힌 곳: 수리 꾸러미가 없고 원료도 없다 — 사슬을 거슬러 첫 막힌 곳까지 (방송 · 일지): 정제기가 망가졌다 / 꺼 두었다(전력 여유가 없다) ·
        //    막힌 곳이 전력 여유면 그 설비는 80% 대신 95%까지 켠다 (보조 발전기 25kW에 다른 짐이 있어 80%로는 못 켜던 정제기 12kW가 켜진다) ──
        {
            var layG = Worlds.Ascii(Worlds.MindShipComputer.Replace("#..p.b....D.............#", "#..p.bf.R.D.............#")
                                                       .Replace("#.........#....a....a.r.#", "#..g..G.A.#....a....a.r.#"), new Dictionary<char, string>(legend) { ['g'] = "AuxGenerator", ['G'] = "GrowRack", ['A'] = "Autoclave" });
            (string blocks, string log, string journal, bool on, float demand) Run(Layout ly, bool broken, bool aux)
            {
                var s = Worlds.Full(ly, 5);
                var m = s.Get<Mind>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>(); var wk = s.Get<Work>();
                sm.JournalHour = 1;
                m.AddPerson(s, Worlds.At(ly, 3, 7));
                foreach (string it in new[] { "원료", "수리 꾸러미" }) { int i = m.ItemIndex(it); for (int fu = 0; fu < ly.Furniture.Count; fu++) m.Stock[fu * m.ItemCount + i] = 0; }
                int rf = net.Devices.First(d => d.Type == "Refinery").Id;
                if (broken)
                {
                    net.Age(rf, Part.Mechanical, 1.0);
                    // ⑥ 고칠 길이 없다: 로봇 부품 주머니는 비었고 · 떼어 쓸 모터도 이미 다 떼어 썼다
                    foreach (var a in m.Agents) a.C.Spares = 0;
                    foreach (var d in net.Devices) if (Network.DonorRank(d.Def.Role) >= 0 && d.Has(Part.Mechanical)) net.TakePart(d.Id, Part.Mechanical);
                }
                if (aux) foreach (var d in net.Devices.Where(d => d.Type is "AuxGenerator" or "GrowRack" or "Autoclave")) net.SetOn(d.Id, true);
                s.Run(3);
                float demand = (float)net.Demanded;
                if (Debug) Console.WriteLine($"      3틱: 켜진 장치 {string.Join(",", net.Devices.Where(d => net.On[d.Id] != 0).Select(d => d.Type + ":" + d.Def.Kw))} · 수요 {net.Demanded:0.0}");
                s.Run(27);
                s.Run((int)(Time.Hours(1) - 20));
                if (Debug) Console.WriteLine($"    정제기 {(broken ? "망가짐" : "멀쩡")} · 발전기 {aux}: {string.Join(" | ", wk.Blocks)} · 켜짐 {net.On[rf]} · 수요(처음) {demand:0.0} · 공급 {net.Supplied:0.0} 수요 {net.Demanded:0.0} 용량 {net.Capacity():0.0}");
                return (string.Join(" | ", wk.Blocks), Lines(s), sm.Journal.Count > 0 ? sm.Journal[^1].text : "", net.On[rf] != 0, demand);
            }
            var br = Run(lay, true, false); var nopower = Run(lay, false, false); var first = Run(layG, false, true);
            Check("막힌 곳: 수리 꾸러미 ← 원료가 없다 ← 정제기가 망가졌다 까지 거슬러 찾아 방송 · 일지 · 멀쩡해도 발전기가 없는 배면 '꺼 두었다 (전력 여유가 없다)'",
                br.blocks.Contains("수리 꾸러미 ← 원료가 없다 ← 정제기가 망가졌다") && br.log.Contains("막힌 곳: 수리 꾸러미") && br.journal.Contains("막힌 곳:") && nopower.blocks.Contains("정제기를 꺼 두었다 (전력 여유가 없다)") && !nopower.blocks.Contains("망가졌다"),
                $"망가짐: \"{br.blocks.Split(" | ").FirstOrDefault(b => b.StartsWith("수리"))}\" / 발전기 없음: \"{nopower.blocks.Split(" | ").FirstOrDefault(b => b.StartsWith("수리"))}\"");
            Check("막힌 곳이 전력 여유면 95%까지 켠다: 보조 발전기 25kW · 다른 짐(재배 선반 · 소독기)이 있어 80%로는 못 켜던 정제기 12kW가 켜져 원료를 만든다",
                first.on && first.demand + 12 > 0.8 * 25 && first.demand + 12 < 0.95 * 25 && first.blocks.Contains("정제기가 만드는 중"),
                $"처음 짐 {first.demand:0.0}kW (+12 = {first.demand + 12:0.0} · 80% 20 · 95% 23.8) · 켜짐 {first.on} · \"{first.blocks.Split(" | ").FirstOrDefault(b => b.StartsWith("수리"))}\"");
        }

        // ── 물 회수율: 새 거르개 94% · 낡은 거르개(90%)는 소금물을 덜 짜내 86% 안팎으로 · 흐름도 준다 ──
        {
            (double rate, double processed) Run(double wear)
            {
                var s = Worlds.Full(lay, 6);
                var net = s.Get<Network>();
                int rc = net.Devices.First(d => d.Type == "WaterRecycler").Id;
                if (wear > 0) net.Age(rc, Part.Filter, wear);
                net.WasteWater += 2000;
                double p0 = net.WaterProcessed, r0 = net.WaterRecovered;
                s.Run((int)Time.Hours(1));
                double p = net.WaterProcessed - p0, r = net.WaterRecovered - r0;
                return (p > 0 ? r / p : 0, p);
            }
            var fresh = Run(0); var worn = Run(0.9);
            Check("물 회수율: 새 거르개 94% · 낡은 거르개(수명의 90%)는 소금물을 덜 짜내 86% 안팎 · 흐름도 준다 (ISS 물 회수 장치 — 오줌 처리만이면 85%)",
                Math.Abs(fresh.rate - 0.94) < 0.003 && worn.rate < 0.875 && worn.rate > 0.85 && worn.processed < fresh.processed * 0.8,
                $"새 {fresh.rate * 100:0.0}% ({fresh.processed:0}몰) · 낡음 {worn.rate * 100:0.0}% ({worn.processed:0}몰)");
        }
    }
}
