using ShipEngine;

namespace Lab;

/// <summary>⑫ 이야기꾼 (바퀴 2) — 막의 리듬 · 큰 일 뒤 여파 · 배의 형편 · 몸 씨앗 · 비밀 씨앗 · 이야기의 실 · 헛도는 씨앗. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void ArcRules()
    {
        if (!Do("이야기의 막")) return;
        Console.WriteLine("\n[⑫ 이야기의 막]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        string Clock(long t) => $"{t / (double)Time.Hours(1):0.0}시";

        // ── 막의 리듬: 조짐(작은 씨앗) → 절정 → 여파(심지 않는다) · 막을 거듭할수록 절정이 커진다 (긴장을 0으로 묶어 리듬만 본다 · 몰아치는 이야기꾼 · 가혹함 8) ──
        {
            var s = ShipWorld.Crewed("Hwasal", 8, 3, 1);
            var st = s.Get<Storyteller>(); st.Enabled = true; st.Level = 8; st.Style = StoryStyle.Relentless;   // 막이 짧은 성격 (조짐 30분 · 여파 1.4시간)
            for (long t = 0; t < Time.Hours(9); t++) { st.Tension = 0; st.Pending = 0; s.Step(); }
            var seeds = st.Seeds.Where(x => x.Kind != SeedKind.GoodNews).ToList();
            bool shape = seeds.Count >= 3 && seeds[0].Act == Storyteller.ActBuild; int climaxes = 0; long minRest = long.MaxValue;
            for (int i = 0; i < seeds.Count; i++)
            {
                if (seeds[i].Act != Storyteller.ActClimax) continue;
                climaxes++;
                if (i == 0 || seeds[i - 1].Act != Storyteller.ActBuild) shape = false;
                if (i + 1 < seeds.Count) minRest = Math.Min(minRest, seeds[i + 1].Tick - seeds[i].Tick);
            }
            bool rested = minRest == long.MaxValue || minRest >= Time.Hours(st.AfterHours) - Storyteller.ThinkEvery;
            // 같은 순간 · 같은 난수 — 첫 막의 절정과 다섯 번째 막의 절정 (운석)
            float Climax(int arc)
            {
                var w = ShipWorld.Crewed("Hwasal", 8, 3, 1);
                var t2 = w.Get<Storyteller>(); t2.Level = 4;
                w.Run(5);
                t2.Act = Storyteller.ActClimax; t2.Arc = arc;
                t2.Plant(w, SeedKind.Rock);
                return t2.Seeds[^1].Size;
            }
            float first = Climax(0), fifth = Climax(5);
            Check("막의 리듬: 조짐(작은 씨앗) 뒤에 절정 · 절정 뒤에는 여파 동안 심지 않는다 · 막을 거듭할수록 절정이 커진다",
                shape && climaxes >= 1 && rested && fifth > first * 1.3f,
                $"{string.Join(" → ", seeds.Select(x => $"{Clock(x.Tick)} {Storyteller.ActName(x.Act)} {Storyteller.SeedName(x.Kind)}"))} · 절정 뒤 쉼 {(minRest == long.MaxValue ? "-" : $"{minRest / (double)Time.Hours(1):0.0}시간")} (여파 {st.AfterHours:0.0}) · 첫 막 운석 {first:0.00} → 다섯째 {fifth:0.00}kg");
        }

        // ── 큰 일 뒤 여파: 죽음이 나면 막이 어디에 있든 여파 — 무너진 배를 더 때리지 않는다 · 두 시간 뒤 고향의 반가운 소식 (슬픔이 풀린다) ──
        {
            (List<Seed> seeds, long death, float sadBefore, float sadAfter, string news, double after) Run(bool kill)
            {
                var s = ShipWorld.Crewed("Hwasal", 8, 3, 1);
                var st = s.Get<Storyteller>(); st.Enabled = true; st.Level = 6;
                var m = s.Get<Mind>(); var life = s.Get<Life>();
                int victim = m.Agents.First(a => a.IsPerson).Id;
                long death = -1; float sb = 0, sa = 0; string news = "";
                float Sad() => (float)m.Agents.Where(a => a.IsPerson && life[a.C.Body].Alive != 0).Average(a => a.C.Sad);
                for (long t = 0; t < Time.Hours(6.5); t++)
                {
                    if (kill && t == Time.Hours(2.1)) life.Strike(s, m[victim].C.Body, 3f, 0.5f, s.Causes.Add(CauseKind.Setup, "시험: 크게 다쳤다", s.Tick));
                    if (death < 0 && life.Deaths > 0) death = s.Tick;
                    st.Tension = 0; st.Pending = 0;
                    if (Environment.GetEnvironmentVariable("ARC_DEBUG") == "1" && !m.Agents.Any(a => a.IsPerson && life[a.C.Body].Alive != 0)) break;
                    int n = st.Seeds.Count; float before = Sad();
                    s.Step();
                    if (st.Seeds.Count > n && st.Seeds[^1].Kind == SeedKind.GoodNews) { sb = before; sa = Sad(); news = s.Causes.Text(st.Seeds[^1].Cause); }
                }
                if (Environment.GetEnvironmentVariable("ARC_DEBUG") == "1")
                {
                    foreach (var x in s.Facts.Since(0).Where(x => x.Kind is FactKind.Died or FactKind.Collapsed or FactKind.StorySeed)) Console.WriteLine($"    {kill} {Time.Clock(x.Tick)} {x.Kind} · {s.Causes.Story(x.Cause)}");
                    foreach (var (tk, tx) in s.Get<ShipMind>().Log.TakeLast(25)) Console.WriteLine($"    {kill} 주컴 {Time.Clock(tk)} {tx}");
                    foreach (var a in m.Agents) Console.WriteLine($"    {kill} {m.NameOf(a.Id)} 칸 ({s.Layout.X(a.C.Cell)},{s.Layout.Y(a.C.Cell)}) {m.RoomOf(a.C.Cell)} 문대기 {a.C.WaitDoor} · {m.Thought(a.Id)}");
                }
                return (st.Seeds, death, sb, sa, news, st.AfterHours);
            }
            var calmT = Task.Run(() => Run(false)); var hit = Run(true); var calm = calmT.Result;
            long d = hit.death, end = d + Time.Hours(hit.after);
            int inAfter = hit.seeds.Count(x => x.Kind != SeedKind.GoodNews && x.Tick > d && x.Tick < end);
            int calmIn = calm.seeds.Count(x => x.Tick > d && x.Tick < end);
            var newsSeed = hit.seeds.FirstOrDefault(x => x.Kind == SeedKind.GoodNews);
            Check("큰 일 뒤 여파: 사람이 죽으면 여파 동안 새 원인을 심지 않는다 (죽음이 없던 배는 그 사이에 심는다) · 두 시간 뒤 고향의 반가운 소식에 슬픔이 풀린다",
                d > 0 && inAfter == 0 && calmIn >= 1 && newsSeed.Kind == SeedKind.GoodNews && newsSeed.Tick >= d + Time.Hours(2) && hit.sadAfter < hit.sadBefore - 0.05f,
                $"죽음 {Clock(d)} · 여파 {hit.after:0.0}시간 동안 심은 것 {inAfter} (죽음 없는 배 {calmIn}) · {Clock(newsSeed.Tick)} \"{hit.news}\" · 슬픔 {hit.sadBefore:0.00} → {hit.sadAfter:0.00}");
        }

        // ── 배의 형편 (자비): 다친 이가 많으면 쉰다 — 가혹함 4는 여섯 중 셋이 다친 배에 심지 않고 · 8 '끝까지'는 봐주지 않는다 · 가혹함 3은 한 사람만 다쳐도 쉰다 ──
        {
            (int seeds, long mercies) Run(byte level, int hurt)
            {
                var s = ShipWorld.Crewed("Didim", 9, 6, 2);
                var st = s.Get<Storyteller>(); st.Enabled = true; st.Level = level;
                var m = s.Get<Mind>(); var life = s.Get<Life>();
                s.Run(10);
                foreach (var p in m.Agents.Where(a => a.IsPerson).Take(hurt)) life.Scald(s, p.C.Body, 0.55f, s.Causes.Add(CauseKind.Setup, "시험: 크게 데었다", s.Tick));   // 쓰러지지 않는 큰 상처 (아픔 0.88) (쓰러짐은 큰 일 — 여파로 간다 · 덮어 둬도 형편 0.6)
                for (long t = 0; t < Time.Hours(6); t++) { st.Tension = 0; st.Pending = 0; s.Step(); }
                if (Debug) Console.WriteLine($"    가혹함 {level} · 다침 {hurt}: 씨앗 {string.Join(", ", st.Seeds.Select(x => $"{Clock(x.Tick)} {Storyteller.ActName(x.Act)} {Storyteller.SeedName(x.Kind)}"))} · 여파 {st.Aftermaths} · 쉼 {st.Mercies} · 형편 {st.Hardship(s, out _):0.00} · 쓰러짐 {s.Facts.CountOf(FactKind.Collapsed)}");
                return (st.Seeds.Count, st.Mercies);
            }
            var t4 = Task.Run(() => Run(4, 3)); var t8 = Task.Run(() => Run(8, 3)); var t3 = Task.Run(() => Run(3, 1)); var one = Run(4, 1);
            var four = t4.Result; var eight = t8.Result; var three = t3.Result;
            Check("배의 형편: 여섯 중 셋이 다친 배에 가혹함 4는 심지 않고 (한 사람이면 심는다) · 가혹함 8은 봐주지 않는다 · 가혹함 3은 한 사람만 다쳐도 쉰다",
                four.seeds == 0 && four.mercies > 0 && one.seeds >= 1 && eight.seeds >= 2 && three.seeds == 0,
                $"셋 다침: 가혹함 4 씨앗 {four.seeds} (쉰 생각 {four.mercies}) · 가혹함 8 씨앗 {eight.seeds} / 한 사람 다침: 가혹함 4 씨앗 {one.seeds} · 가혹함 3 씨앗 {three.seeds}");
        }

        // ── 몸 씨앗: 일하다 다침 → 로봇이 돌본다 (돌봄의 원인 사슬 뿌리가 그 씨앗) · 감기 → 한 사람이 잠복에 든다 (뿌리가 그 씨앗) ──
        {
            var s = ShipWorld.Crewed("Didim", 9, 6, 2);
            var st = s.Get<Storyteller>(); var m = s.Get<Mind>(); var life = s.Get<Life>();
            s.Run(Time.Minutes(10));
            bool injured = st.Plant(s, SeedKind.Injury);
            var inj = st.Seeds[^1];
            Fact treated = default;
            for (long t = 0; t < Time.Hours(1.5) && treated.Kind != FactKind.Treated; t++)
            {
                s.Step();
                foreach (var f in s.Facts.Since(0)) if (f.Kind == FactKind.Treated && s.Causes.Root(f.Cause) == inj.Cause) { treated = f; break; }
            }
            bool cold = st.Plant(s, SeedKind.Cold);
            var cs = st.Seeds[^1];
            var sick = m.Agents.FirstOrDefault(a => a.IsPerson && life[a.C.Body].Ill == 1 && s.Causes.Root(life[a.C.Body].IllCause) == cs.Cause);
            Check("몸 씨앗: 일하다 다친 사람을 로봇이 돌본다 (돌봄의 뿌리가 그 씨앗 · 씨앗이 열매를 맺었다) · 감기 씨앗은 한 사람을 잠복에 들게 한다",
                injured && treated.Kind == FactKind.Treated && st.Seeds[^2].Fruit == 1 && cold && sick != null,
                $"\"{s.Causes.Text(inj.Cause)}\" → {(treated.Kind == FactKind.Treated ? $"{(treated.Tick - inj.Tick) / 25.0:0}분 뒤 \"{s.Causes.Text(treated.Cause)}\"" : "돌봄 없음")} · \"{s.Causes.Story(life[sick?.C.Body ?? 0].IllCause)}\"");
        }

        // ── 비밀 — 짝사랑: 한쪽만 남몰래 좋아하게 둔다 → 그가 고백한다 (상대는 끌리지 않아 거절) · 고백의 원인 사슬에 그 마음 / 씨앗이 없으면 여섯 시간 고백 없음 ──
        {
            var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            int At(int x, int y) => Worlds.At(lay, x, y);
            ulong good = 1;                                     // ⑨ 고백 시험과 같은 둘 (함께 쉬며 이야기를 나누는 사이)
            while (Rng.Unit(good, Rng.Key("끌림"), 0, 1) < 0.85) good++;
            (long confess, long rejected, bool linked, string text) Run(bool crush)
            {
                var s = Worlds.Full(lay, good);
                var m = s.Get<Mind>(); var so = s.Get<Society>(); var st = s.Get<Storyteller>();
                int a = m.AddPerson(s, At(13, 2)), b = m.AddPerson(s, At(15, 3));
                s.Run(1);
                int N = so.N;
                so.Bond[a * N + b] = so.Bond[b * N + a] = 0.55f;
                so.Known[a * N + b] = so.Known[b * N + a] = 0.8f;
                int c0 = 0;
                if (crush) { st.Plant(s, SeedKind.Crush); c0 = st.Seeds[^1].Cause; }
                for (long k = 0; k < Time.Hours(6) && so.Couples + so.Rejections == 0; k++)
                {
                    foreach (int p in new[] { a, b }) { m[p].C.Hunger = 0; m[p].C.Thirst = 0; m[p].C.Sleepy = 0; }
                    s.Step();
                }
                var talk = s.Facts.Since(0).LastOrDefault(x => x.Kind == FactKind.Talk && (TalkEnd)(int)x.Size is TalkEnd.Reject or TalkEnd.Accept);
                if (Debug) Console.WriteLine($"    짝사랑 {crush}: 끌림 {so.AttractOf(a, b):0.00}/{so.AttractOf(b, a):0.00} · 정 {so.BondOf(a, b):0.00}/{so.BondOf(b, a):0.00} · 대화 {so.Talks} · {m.Thought(a)} / {m.Thought(b)}");
                bool linked = crush && talk.Kind == FactKind.Talk && s.Causes.Chain(talk.Cause).Contains(c0);
                return (so.Couples + so.Rejections, so.Rejections, linked, crush ? s.Causes.Text(c0) : "");
            }
            var noneT = Task.Run(() => Run(false)); var yes = Run(true); var none = noneT.Result;
            Check("비밀 — 짝사랑: 남몰래 품은 마음이 고백으로 터진다 (상대가 끌리지 않으면 거절) · 고백의 원인 사슬에 그 마음 · 씨앗이 없으면 여섯 시간 고백하지 않는다",
                yes.confess == 1 && yes.rejected == 1 && yes.linked && none.confess == 0,
                $"\"{yes.text}\" → 고백 {yes.confess} · 거절 {yes.rejected} · 사슬에 이음 {yes.linked} / 씨앗 없음: 고백 {none.confess}");
        }

        // ── 비밀 — 숨긴 실수: 손보다 부품을 상하게 하고 말하지 않았다 → 고장 나면 그 사람만 제 탓인 줄 안다 — 성실 · 다정한 배는 털어놓고 (조금 원망하지만 더 믿는다) · 그렇지 않은 배는 숨긴다 (아무도 탓하지 않고 그 사람 마음만 무겁다) ──
        {
            (long confess, long kept, float trust, float grudge, float stress, string text) Run(bool honest)
            {
                var s = ShipWorld.Crewed("Didim", 9, 6, 2);
                var st = s.Get<Storyteller>(); st.Level = 8;
                var m = s.Get<Mind>(); var so = s.Get<Society>(); var net = s.Get<Network>();
                s.Run(10);
                foreach (var a in m.Agents.Where(a => a.IsPerson)) { a.C.Consc = honest ? 0.95f : 0.05f; a.C.Agree = honest ? 0.9f : 0.1f; a.C.Neuro = honest ? 0.1f : 0.8f; }
                st.Plant(s, SeedKind.HiddenFault);
                var sd = st.Seeds[^1];
                var mf = s.Facts.Since(0).Last(x => x.Kind == FactKind.Mistake && x.Size >= 2);
                int who = mf.Actor, dev = mf.Other, N = so.N;
                var others = m.Agents.Where(a => a.IsPerson && a.Id != who).Select(a => a.Id).ToList();
                float trust0 = others.Average(o => so.TrustOf[o * N + who]), stress0 = m[who].C.Stress;
                foreach (var p in Enum.GetValues<Part>()) net.AgeLeft(dev, p, 0.01, sd.Cause);   // 몇 시간 뒤가 아니라 곧 망가지게
                s.Run(Time.Minutes(15));
                var told = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Confessed || x.Kind == FactKind.Mistake && x.Size >= 3);
                if (Debug) Console.WriteLine($"    {(honest ? "정직" : "숨김")}: {s.Causes.Text(sd.Cause)} · {(told.Kind != FactKind.None ? s.Causes.Story(told.Cause) : "드러남 없음")}");
                return (so.Confessions, so.SecretsKept, others.Average(o => so.TrustOf[o * N + who]) - trust0, (float)others.Average(o => so.GrudgeOf(o, who)),
                    m[who].C.Stress - stress0, told.Kind != FactKind.None ? s.Causes.Text(told.Cause) : "");
            }
            var hideT = Task.Run(() => Run(false)); var tell = Run(true); var hide = hideT.Result;
            Check("비밀 — 숨긴 실수: 고장 나면 그 사람만 제 탓인 줄 안다 — 정직한 배는 털어놓는다 (조금 원망하지만 더 믿는다) · 아닌 배는 숨긴다 (아무도 탓하지 않고 그 사람 마음만 무겁다)",
                tell.confess == 1 && tell.kept == 0 && tell.trust > 0.05f && tell.grudge > 0
                && hide.confess == 0 && hide.kept == 1 && hide.grudge < 0.01f && hide.stress > 0.2f,
                $"정직: \"{tell.text}\" · 믿음 +{tell.trust:0.00} · 원망 {tell.grudge:0.00} / 숨김: \"{hide.text}\" · 원망 {hide.grudge:0.00} · 긴장 +{hide.stress:0.00}");
        }

        // ── 이야기의 실 · 헛도는 씨앗: 실이 굵은 사람이 사람 씨앗의 주인공이 된다 / 주컴퓨터가 번번이 막는 씨앗(켜 둔 조리대)은 덜 심고 · 불이 된 씨앗은 더 심는다 ──
        {
            var s = ShipWorld.Crewed("Didim", 9, 6, 2);
            var st = s.Get<Storyteller>(); var m = s.Get<Mind>();
            s.Run(10);
            var people = m.Agents.Where(a => a.IsPerson).ToList();
            int hero = people[3].Id;
            int Picks() { int n = 0; for (int k = 0; k < 300; k++) if (st.PickPerson(people, _ => 1)?.Id == hero) n++; return n; }
            int before = Picks();
            st.Thread = new float[m.Agents.Count]; st.Thread[hero] = 2;
            int after = Picks();

            (float trust, byte fruit, int share) Learn(bool computer)
            {
                var w = ShipWorld.Crewed("Didim", 9, 6, 2);
                var t2 = w.Get<Storyteller>(); var net = w.Get<Network>();
                if (!computer) net.Age(net.Devices.First(d => d.Type == "MainComputer").Id, Part.Electric, 1.0);
                w.Run(Time.Minutes(10));
                t2.Plant(w, SeedKind.Mistake);
                int idx = t2.Seeds.Count - 1;
                w.Run(Time.Hours(3.6));
                int share = 0; for (int k = 0; k < 400; k++) if (t2.Choose(w, 1) == SeedKind.Mistake) share++;
                return (t2.KindTrust[(int)SeedKind.Mistake], t2.Seeds[idx].Fruit, share);
            }
            var aloneT = Task.Run(() => Learn(false)); var watched = Learn(true); var alone = aloneT.Result;
            Check("이야기의 실: 다치고 · 다투고 · 씨앗에 닿은 사람(실이 굵은 사람)이 다음 사람 씨앗의 주인공이 된다",
                after > before * 2, $"여섯 중 한 사람이 뽑힌 수 (300번): 실 없음 {before} · 실 2 {after}");
            Check("헛도는 씨앗: 주컴퓨터가 막아 아무 일도 안 된 '켜 둔 조리대'는 덜 고르고 · 불이 된 배에서는 더 고른다",
                watched.fruit == 0 && watched.trust < 0.7f && alone.fruit == 1 && alone.trust > 1.1f && watched.share < alone.share,
                $"주컴퓨터 있음: 열매 {watched.fruit} · 무게 {watched.trust:0.00} · 작은 씨앗 400번 중 {watched.share} / 주컴퓨터 멈춤: 열매 {alone.fruit} · 무게 {alone.trust:0.00} · {alone.share}");
        }
    }
}
