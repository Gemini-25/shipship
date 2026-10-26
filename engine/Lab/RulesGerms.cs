using ShipEngine;

namespace Lab;

/// <summary>일상 물리 — 손 · 겉면 장염 균 (5단 C21 — docs/STAGE5.md): 만지면 손과 겉면이 30%씩 맞바꾼다 · 씻으면 준다 · 먹을 때 옮는다 · 배탈 · 토함 (C22).</summary>
public static partial class Program
{
    private static void GermRules()
    {
        if (!Do("일상 물리")) return;
        Console.WriteLine("\n[일상 물리 — 손 균 (C21)]");
        var legend = new Dictionary<char, string> { ['e'] = "Bed", ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['m'] = "MealDispenser", ['p'] = "PowerPanel", ['b'] = "Battery" };
        var lay = Worlds.Ascii(WashShip.Replace("#e.....#T", "#e....m#T"), legend);

        // (가) 배탈 앓는 A가 변기를 쓰고 · 한 시간 뒤 B가 같은 변기를 쓰고 안 씻고 먹으면 (옮길 몫 ×10) 잠복 — 원인 사슬에 '변기' · A의 배탈 /
        //     C는 같은 변기를 쓰고 세면대에서 씻은 뒤 먹는다 — 손 0.05 아래 · 옮지 않는다
        {
            var s = Worlds.Full(lay, 5);
            var life = s.Get<Life>(); var f = s.Get<Field>();
            var toilet = lay.Furniture.First(x => x.Type == "Toilet"); var sink = lay.Furniture.First(x => x.Type == "Sink"); var meal = lay.Furniture.First(x => x.Type == "MealDispenser");
            int tc = toilet.Cells[0];
            int a = life.AddBody(tc), b = life.AddBody(tc), c = life.AddBody(tc);
            s.Run(2);
            life.GutInfect = 4;   // 옮길 몫 0.4 × 10
            ref var ba = ref life[a]; ba.Gut = 2; ba.GutCause = s.Causes.Add(CauseKind.Biology, "김하늘이 상한 음식을 먹고 배탈이 났다", s.Tick, tc);
            life[a].Bowel = 0.2; life.Flush(s, a, toilet.Id, tc);
            float surfA = life.SurfGerm[toilet.Id];
            s.Run(Time.Hours(1));
            life[b].Bladder = 10; life.Flush(s, b, toilet.Id, tc);
            float handB = life[b].HandGerm;
            life.EatBy(s, b, 10, meal.Id);
            string story = life[b].Gut == 1 ? s.Causes.Story(life[b].GutCause) : "";
            life[c].Bladder = 10; life.Flush(s, c, toilet.Id, tc);
            life.HandWash(s, c, 30, sink.Cells[0]);
            float handC = life[c].HandGerm;
            life.EatBy(s, c, 10, meal.Id);
            Check("손 균 (C21): 배탈 앓는 A가 쓴 변기를 한 시간 뒤 B가 쓰고 안 씻고 먹으면 옮는다 (잠복 · 원인 사슬에 '변기' · A의 배탈) / C는 쓰고 씻은 뒤 먹어 손 0.05 아래 · 옮지 않는다",
                surfA > 0.5 && handB > 0.05 && life[b].Gut == 1 && story.Contains("변기") && story.Contains("김하늘") && handC < 0.05 && life[c].Gut == 0,
                $"변기 겉면 {surfA:0.00} · B 손 {handB:0.000} → 배탈 {life[b].Gut} '{story}' / C 손 {handC:0.000} → 배탈 {life[c].Gut}");
        }

        // (나) 줄어듦 셈을 직접 6일치: 깨끗한 변기 겉면 1 → 0.25 (반감 3일) · 때 1인 변기 → 0.63 (반감 9일) · 손 2시간이면 반
        {
            var s = Worlds.Full(lay, 5);
            var life = s.Get<Life>();
            var toilets = lay.Furniture.Where(x => x.Type == "Toilet").ToArray();
            int h = life.AddBody(toilets[0].Cells[0]);
            s.Run(1);
            life.SurfGerm[toilets[0].Id] = 1; life.Grime[toilets[0].Id] = 0;
            life.SurfGerm[toilets[1].Id] = 1; life.Grime[toilets[1].Id] = 1;
            life[h].HandGerm = 1;
            life.GermStep(Time.Hours(2) * Time.SecondsPerTick);
            float hand = life[h].HandGerm;
            for (int k = 0; k < 6; k++) life.GermStep(86400 - (k == 0 ? Time.Hours(2) * Time.SecondsPerTick : 0));
            float clean = life.SurfGerm[toilets[0].Id], dirty = life.SurfGerm[toilets[1].Id];
            Check("손 균 (C21) 줄어듦: 6일 — 깨끗한 변기 겉면 1 → 0.25 · 때 1인 변기 → 0.63 (더러운 변기에서 오래 산다) · 손은 2시간에 반",
                Math.Abs(clean - 0.25) < 0.01 && Math.Abs(dirty - 0.63) < 0.01 && Math.Abs(hand - 0.5) < 0.01,
                $"깨끗한 변기 {clean:0.000} · 때 1 {dirty:0.000} · 손 2시간 {hand:0.000}");
        }

        // (다) C22 배탈 (주컴퓨터 없음): 배탈을 앓는 P (토할 몫 ×10)와 성한 Q를 3시간 — P는 변기를 두 번 넘게 (Q는 한 번 아래) · 메스꺼움 · 병 '배탈' · 토함 사실 ·
        //     토한 칸 물의 원인 '배탈로 토했다' · P가 쓴 변기 겉면 0.5 넘게
        {
            var s = Worlds.Full(lay, 5);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>();
            int p = m.AddPerson(s, Worlds.At(lay, 2, 2)), q = m.AddPerson(s, Worlds.At(lay, 3, 2));
            s.Run(2);
            life.GutVomitRate = 2;
            ref var bp = ref life[m[p].C.Body];
            bp.Gut = 2; bp.GutHours = 0; bp.GutCause = s.Causes.Add(CauseKind.Biology, "시험: 장염 균이 옮았다", s.Tick);
            void Calm(int x) { ref var c = ref m[x].C; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Lonely = 0; c.Bored = 0; c.Unfit = 0; }
            long tp = 0, tq = 0, f0 = s.Facts.Total;
            for (int k = 0; k < (int)Time.Hours(3); k++)
            {
                Calm(p); Calm(q);
                long pAt = m[p].C.ToiletAt, qAt = m[q].C.ToiletAt;
                s.Step();
                if (m[p].C.ToiletAt != pAt) tp++;
                if (m[q].C.ToiletAt != qAt) tq++;
            }
            var vomit = s.Facts.Since(f0).Where(x => x.Kind == FactKind.Vomit && x.Actor == m[p].C.Body).ToList();
            string vomitWhy = vomit.Count > 0 && f.WetCause[vomit[0].Cell] != 0 ? s.Causes.Text(f.WetCause[vomit[0].Cell]) : "";
            var bpNow = life[m[p].C.Body];
            float surf = lay.Furniture.Where(x => x.Type == "Toilet").Max(x => life.SurfGerm[x.Id]);
            Check("배탈 (C22): 배탈을 앓는 P와 성한 Q를 3시간 — P는 변기를 두 번 넘게 (Q는 한 번 아래) · 메스꺼움 · 병 '배탈' · 토함 사실 (몸 번호) · 토한 칸 물의 원인 '배탈로 토했다' · P가 쓴 변기 겉면 0.5 넘게",
                tp >= 2 && tq <= 1 && (Life.Symptoms(bpNow) & Sym.Nausea) != 0 && Life.Ailment(bpNow) == Ail.Gut && vomit.Count >= 1 && vomitWhy.Contains("배탈로 토했다") && surf > 0.5,
                $"변기 P {tp} · Q {tq} · 메스꺼움 {(Life.Symptoms(bpNow) & Sym.Nausea) != 0} · 병 {Life.AilName(Life.Ailment(bpNow))} · 토함 {vomit.Count} · '{vomitWhy}' · 변기 겉면 {surf:0.00}");
        }

        // (라) C22 단계: 잠복 24시간 → 앓음 48시간 (사실 '병' 크기 4 · '배탈이 났다') → 나음 — 단계 셈을 큰 dt로 직접
        {
            var s = Worlds.Full(lay, 5);
            var life = s.Get<Life>();
            int b = life.AddBody(Worlds.At(lay, 2, 2));
            s.Run(1);
            life[b].Gut = 1; life[b].GutHours = 0; life[b].GutCause = s.Causes.Add(CauseKind.Biology, "시험: 장염 균이 옮았다", s.Tick);
            long f0 = s.Facts.Total;
            life.GutStage(s, b, 23 * 3600); byte g23 = life[b].Gut;
            life.GutStage(s, b, 2 * 3600); byte g25 = life[b].Gut;
            var sick = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Sick && x.Actor == b);
            string why = sick.Kind == FactKind.Sick ? s.Causes.Text(sick.Cause) : "";
            life.GutStage(s, b, 47 * 3600); byte g72 = life[b].Gut;
            life.GutStage(s, b, 2 * 3600); byte g74 = life[b].Gut;
            Check("배탈 (C22) 단계: 잠복 24시간 → 앓음 (사실 '병' 크기 4 · '배탈이 났다') → 48시간 뒤 나음",
                g23 == 1 && g25 == 2 && sick.Kind == FactKind.Sick && sick.Size == 4 && why.Contains("배탈이 났다") && g72 == 2 && g74 == 3,
                $"23시간 {g23} · 25시간 {g25} ('{why}' 크기 {sick.Size}) · 72시간 {g72} · 74시간 {g74}");
        }
    }
}
