using ShipEngine;

namespace Lab;

/// <summary>깊이 고침 1차 (docs/DEPTH_FIX.md · 근거 docs/ENGINE_DEPTH_REVIEW.md) — 틀린 법칙 · 빠진 원인 사슬 · 빈 이름.</summary>
public static partial class Program
{
    private static void Depth1Rules()
    {
        // ── DF1 곰팡이 포자 (보고서 §4-2 · §5): 0.3 계단 독 → 포자 농도에 비례해 날 단위로 천천히 · 이름 '곰팡이 포자를 마셨다' · 원인은 그 칸의 젖음 → 곰팡이 ──
        if (Do("곰팡이 포자"))
        {
            Console.WriteLine("\n[곰팡이 포자 — 기침은 곧 · 폐 손상은 날 단위로]");
            // 젖은 바닥 (물관이 샌 물 — 원인 마디) · 곰팡이 0.4인 방에서 하룻밤 (8시간) 잔다
            (Sim s, Life life, int b, int leak) Night(float mold, double hours, bool wet = true)
            {
                var sw = new Sim(new SimSetup(Worlds.Ascii(Worlds.Symmetric), 1, sim => { var ff = new Field(sim.Layout, sim.Tables); sim.Add(ff); sim.Add(new Life(ff)); }));
                var f = sw.Get<Field>(); f.Adiabatic = true; f.FillAir(humidity: wet ? 0.7 : 0.4);
                var l = sw.Get<Life>();
                int why = sw.Causes.Add(CauseKind.Leak, "물관 이음매가 샜다", sw.Tick);
                for (int c = 0; c < f.Cells; c++)
                    if (sw.Layout.IsSpace(c) && wet) { f.AddStore(c, f.T.sWater, 80); f.WetCause[c] = why; l.Mold[c] = mold; }
                sw.Run(130);   // 곰팡이 단계 (125틱마다)가 핀 칸에 원인 마디를 단 뒤 들어가 잔다
                int body = l.AddBody(Worlds.At(sw.Layout, 5, 3));
                l[body].Asleep = 1;
                for (long k = 0; k < Time.Hours(hours); k++) { l[body].Asleep = 1; l[body].ActivityW = 77; sw.Step(); }
                return (sw, l, body, why);
            }
            var (s, life, bd, leak0) = Night(0.4f, 8);
            var hurt = s.Facts.Since(0).Where(x => x.Kind == FactKind.Hurt && x.Actor == bd).ToList();
            string story = hurt.Count > 0 ? s.Causes.Story(hurt[0].Cause) : "";
            bool smoke = s.Facts.Since(0).Any(x => x.Cause != 0 && s.Causes.Story(x.Cause).Contains("연기를 마셨다"));
            var sym = Life.Symptoms(life[bd]);
            Check("곰팡이 0.4 방에서 하룻밤 (8시간): 죽지도 쓰러지지도 않고 기침만 (기침 증상 · 폐 손상 0.02 아래 · 산소 포화 0.9 위) · '곰팡이 포자를 마셨다' — 원인이 그 칸의 젖음 (물관 샘) → 곰팡이까지 · 불 없는 '연기를 마셨다' 없음 · 알림은 얼룩에 한 번 (앞서 다친 일 자리는 비워 둔다)",
                life[bd].Alive != 0 && life[bd].Conscious != 0 && (sym & Sym.Cough) != 0 && life[bd].LungDamage < 0.02f && life[bd].SpO2 > 0.9f
                && story.Contains("곰팡이 포자를 마셨다") && story.Contains("물관 이음매가 샜다") && story.Contains("곰팡이") && !smoke && hurt.Count == 1 && life[bd].HurtCause == 0,
                $"살아 있음 {life[bd].Alive} · 깨어 있음 {life[bd].Conscious} · 증상 {sym} · 폐 {life[bd].LungDamage:0.0000} · 포화 {life[bd].SpO2:0.00} · 포자 {life[bd].Spore:0.00} · '{story}' · 연기 {smoke} · 다쳤다 {hurt.Count}번 · 앞서 다친 일 {life[bd].HurtCause}");

            // 농도에 비례: 곰팡이 0.1 방은 0.8 방보다 폐 손상이 훨씬 적다 (같은 하룻밤) · 맑은 방은 0
            var lo = Night(0.1f, 8); var hi = Night(0.8f, 8); var none = Night(0f, 8, wet: false);
            float dLo = lo.life[lo.b].LungDamage, dHi = hi.life[hi.b].LungDamage, d0 = none.life[none.b].LungDamage;
            Check("포자 농도에 비례: 곰팡이 0.8 방의 폐 손상이 0.1 방의 네 배 넘게 · 곰팡이 없는 마른 방은 손상 0 (계단이 아니다 — 0.31이든 1.0이든 같던 독)",
                dHi > dLo * 4 && dLo > 0 && d0 == 0, $"0.1 → {dLo:0.00000} · 0.8 → {dHi:0.00000} · 0 → {d0:0.00000}");
        }

        // ── DF1 원인 사슬 (보고서 §4-15): 쓰러짐 · 죽음마다 원인 마디가 있고 · 그 까닭을 이름으로 · 뿌리까지 / 빈 이름을 채운다 ──
        if (Do("원인 채움"))
        {
            Console.WriteLine("\n[원인 채움 — 쓰러짐 · 죽음 · 합선 · 빈 이름]");
            Sim BodyWorld(double temp = 293.15) { var sw = new Sim(new SimSetup(Worlds.Ascii(Worlds.Symmetric), 1, sim => { var ff = new Field(sim.Layout, sim.Tables); sim.Add(ff); sim.Add(new Life(ff)); })); var f0 = sw.Get<Field>(); f0.Adiabatic = true; f0.FillAir(temp: temp); return sw; }
            var audit = new List<string>();
            // 쓰러짐 · 죽음 사실: 원인 마디 > 0 · 그 마디의 글이 그냥 '쓰러졌다'가 아니다 (까닭이 있다)
            void Audit(Sim s, string tag)
            {
                foreach (var x in s.Facts.Since(0).Where(x => x.Kind is FactKind.Collapsed or FactKind.Died))
                    if (x.Cause == 0 || s.Causes.Text(x.Cause) == "쓰러졌다" || s.Causes.Chain(x.Cause).Any(id => s.Causes.Text(id) == "쓰러졌다"))
                        audit.Add($"{tag} {FactNames.Of(x.Kind)} '{(x.Cause != 0 ? s.Causes.Story(x.Cause) : "원인 없음")}'");
            }

            // (가) 날아든 것에 맞아 (타박 1.6 — ④가 Strike로 알린다): 쓰러짐 · 죽음의 사슬이 그 맞음에 닿는다 (예전: '쓰러졌다 → 죽었다 — 크게 다쳤다')
            var sa = BodyWorld(); var la = sa.Get<Life>(); int ba = la.AddBody(Worlds.At(sa.Layout, 5, 3));
            int shelf = sa.Causes.Add(CauseKind.Setup, "선반이 넘어져 덮쳤다", sa.Tick);
            sa.Step();
            la.Strike(sa, ba, 1.6f, 0.2f, shelf);
            sa.Run(5);
            var downA = sa.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Collapsed && x.Actor == ba);
            var deadA = sa.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Died && x.Actor == ba);
            string sd = downA.Cause != 0 ? sa.Causes.Story(downA.Cause) : "", sx = deadA.Cause != 0 ? sa.Causes.Story(deadA.Cause) : "";
            Audit(sa, "맞음");
            Check("맞아 쓰러진 이 (타박 1.6): 쓰러짐 · 죽음의 원인 사슬이 맞은 일 ('선반이 넘어져 덮쳤다')에 닿는다 · 쓰러짐에 까닭 (그냥 '쓰러졌다'가 아니다)",
                downA.Kind == FactKind.Collapsed && deadA.Kind == FactKind.Died && sd.StartsWith("선반이 넘어져 덮쳤다") && sx.StartsWith("선반이 넘어져 덮쳤다") && sa.Causes.Text(downA.Cause) != "쓰러졌다",
                $"쓰러짐 '{sd}' · 죽음 '{sx}'");

            // (나) 앞서 쌓인 산소 부족 (지금 공기는 맑다): '숨이 모자라 쓰러졌다' — 앞서 다친 일에 잇는다
            var sb = BodyWorld(); var lb = sb.Get<Life>(); int bb = lb.AddBody(Worlds.At(sb.Layout, 5, 3));
            lb[bb].HurtCause = sb.Causes.Add(CauseKind.Setup, "연기 찬 방에 오래 갇혀 있었다", sb.Tick);
            lb[bb].Hypoxia = 0.6f;
            sb.Run(6);   // 생명은 5틱마다
            var downB = sb.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Collapsed && x.Actor == bb);
            string sbS = downB.Cause != 0 ? sb.Causes.Story(downB.Cause) : "";
            Audit(sb, "산소");

            // (다) 뜨거운 방 (50℃ · 습함)에서 힘든 일: 체온 40℃를 넘어 쓰러진다 — '몸이 너무 뜨거워졌다' (예전: 까닭 없는 '쓰러졌다')
            var sc = BodyWorld(323.15); var lc = sc.Get<Life>(); int bc = lc.AddBody(Worlds.At(sc.Layout, 5, 3));
            for (long k = 0; k < Time.Hours(3) && lc[bc].Conscious != 0; k++) { lc[bc].ActivityW = 400; sc.Step(); }
            var downC = sc.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Collapsed && x.Actor == bc);
            string scS = downC.Cause != 0 ? sc.Causes.Story(downC.Cause) : "";
            Audit(sc, "더위");
            Check("까닭을 이름으로: 앞서 쌓인 산소 부족 → '숨이 모자라 쓰러졌다' (앞서 다친 일에 잇는다) · 뜨거운 방의 힘든 일 → 체온이 올라 '몸이 너무 뜨거워' 쓰러진다",
                sbS.StartsWith("연기 찬 방에 오래 갇혀 있었다") && sbS.Contains("숨이 모자라") && downC.Kind == FactKind.Collapsed && scS.Contains("뜨거워"),
                $"산소 '{sbS}' · 더위 '{scS}' (체온 {lc[bc].CoreK - 273.15:0.0}℃)");
            Check("모든 쓰러짐 · 죽음 사실에 원인 마디가 있고 그 글이 까닭을 말한다 (그냥 '쓰러졌다' 마디가 사슬에 없다)", audit.Count == 0,
                audit.Count == 0 ? "빠진 것 없음" : string.Join(" / ", audit.Take(4)));

            // (라) 오래 정격을 넘는 전류에 달궈진 급전선 피복이 녹아 합선: 사슬이 그 과부하에 닿는다 (예전: '전선 피복이 뜨거워 녹았다'가 뿌리)
            var sd2 = ShipWorld.Create("Didim", 1); var net = sd2.Get<Network>(); var fd2 = sd2.Get<Field>();
            sd2.Run(3);
            var feeder = net.Feeders.Where(x => x.Path.Length > 2 && net.Amps[x.Id] > 5 && net.Breaker[x.Id] != 0).OrderByDescending(x => net.Amps[x.Id]).First();
            feeder.RatingA = net.Amps[feeder.Id] / 1.1;   // 정격보다 10% 많이 흐른다 (1.25배 아래 — 차단기는 버틴다)
            int hot = feeder.Path[feeder.Path.Length / 2];
            Fact shortF = default;
            for (int k = 0; k < 40 && shortF.Kind != FactKind.Short; k++)
            {
                fd2.Tf[hot] = 480;
                sd2.Step();
                shortF = sd2.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Short);
            }
            string shortS = shortF.Kind == FactKind.Short ? sd2.Causes.Story(shortF.Cause) : "";
            var chain = shortF.Kind == FactKind.Short ? sd2.Causes.Chain(shortF.Cause) : new List<int>();
            Check("달궈진 급전선 피복이 녹은 합선: '전선 피복이 뜨거워 녹았다'에 뿌리가 있다 — 그 급전선에 정격보다 많은 전류",
                shortS.Contains("뜨거워 녹았다") && chain.Count >= 3 && shortS.Contains("정격보다 많은 전류"), $"'{shortS}'");

            // (마) 빈 이름: 사실 갈래마다 이름 (덜컹 · 앞일 알림이 빈칸이었다) · 관이 터지면 '관이 터졌다' (냉각관 · 물관 — 전선만 '선이 끊겼다')
            var blank = Enum.GetValues<FactKind>().Where(k => k != FactKind.None && FactNames.Of(k).Length == 0).ToList();
            var pipeFact = new Fact { Kind = FactKind.CableCut, Other = 2 }; var powerFact = new Fact { Kind = FactKind.CableCut, Other = 1 };
            var sp = ShipWorld.Create("Didim", 1); var np = sp.Get<Network>(); var smp = sp.Get<ShipMind>();
            sp.Run(3);
            int pipe = Enumerable.Range(0, sp.Layout.N).First(c => (np.LineAt[c] & 2) != 0 && (np.LineAt[c] & 1) == 0 && sp.Layout.Room[c] >= 0);
            int logFrom = smp.Log.Count;
            np.CutLines(sp, pipe, 2, sp.Causes.Add(CauseKind.Setup, "냉각관이 낡아 터졌다", sp.Tick));
            sp.Run(Time.Minutes(2));
            var said = smp.Log.Skip(logFrom).Select(x => x.Item2).Where(t => t.Contains("관이 터졌다") || t.Contains("선이 끊겼다")).ToList();
            Check("빈 이름 없음: 사실 갈래마다 이름 (덜컹 · 앞일 알림) · 관만 끊긴 사실은 '관이 터졌다' (전선은 '선이 끊겼다') · 주컴퓨터도 냉각관이 터지면 '관이 터졌다'",
                blank.Count == 0 && FactNames.Of(pipeFact) == "관이 터졌다" && FactNames.Of(powerFact) == "선이 끊겼다" && said.Count > 0 && said.All(t => t.Contains("관이 터졌다")),
                $"빈 이름 {(blank.Count == 0 ? "없음" : string.Join(",", blank))} · 관 '{FactNames.Of(pipeFact)}' · 전선 '{FactNames.Of(powerFact)}' · 주컴퓨터 '{string.Join(" / ", said.Take(2))}'");
        }

        // ── DF2 사회 버그 둘 (보고서 §4-9): 축하 기억이 '두 무리의 화해'로 읽혀 무리 없는 배의 모든 쌍에 원한이 번졌다 · 따짐은 원한만 키우고 줄이는 길이 없었다 ──
        if (Do("사회 버그"))
        {
            Console.WriteLine("\n[사회 버그 — 축하는 좋은 기억 · 앙금은 줄 수 있다]");
            var legend = new Dictionary<char, string> { ['m'] = "MealDispenser", ['a'] = "Seat", ['e'] = "Bed" };
            var lay = Worlds.Ascii(CultureShip, legend);
            int At(int x, int y) => Worlds.At(lay, x, y);
            int mess = lay.Room[At(14, 2)], lounge = lay.Room[At(24, 2)], beds = lay.Room[At(2, 2)];
            lay.Rooms[beds].Name = "침실"; lay.Rooms[mess].Name = "식당"; lay.Rooms[lounge].Name = "휴게실";
            lay.Rooms[mess].Type = "Lounge"; lay.Rooms[lounge].Type = "Lounge";
            void Keep(Sim s, Mind m, List<int> p) { foreach (int a in p) { ref var c = ref m[a].C; c.Sleepy = 0; c.Thirst = 0; c.Bored = 0; c.Lonely = 0; c.Hunger = 0; s.Get<Life>()[c.Body].Asleep = 0; } }
            float GrudgeSum(Society soc, List<int> p) { float g = 0; foreach (int a in p) foreach (int b in p) if (a != b) g += soc.GrudgeOf(a, b); return g; }

            // (가) 무리 없는 배 (다섯 · 모두 다정): 위기를 넘기고 축하한 뒤 네 시간 함께 쉰다 — 축하 이야기는 웃음 · 원한 합이 늘지 않는다 · '두 무리' 이야기가 없다
            {
                var s = Worlds.Full(lay, 9); var m = s.Get<Mind>(); var soc = s.Get<Society>();
                var p = new List<int>();
                for (int i = 0; i < 5; i++) { int a = m.AddPerson(s, At(12 + (i % 4) * 2, 1 + (i / 4) * 2)); m.SetPersonality(a, 0.5f, 0.6f, i == 1 ? 0.95f : 0.6f, 0.9f, 0.2f); p.Add(a); }
                s.Run(2);
                int fire = s.Causes.Add(CauseKind.Fire, "식당 조리대에 불이 붙었다", s.Tick, At(14, 2));
                s.Facts.Emit(s.Tick, FactKind.Ignition, At(14, 2), 1, fire, -1, -1);
                s.Facts.Emit(s.Tick, FactKind.FireSpread, At(15, 2), 1, fire, -1, -1);
                s.Facts.Emit(s.Tick, FactKind.Extinguished, At(15, 2), 1, fire, p[2], -1);
                s.Run(Time.Minutes(2));
                m.CallGatheringNow(s);
                for (int k = 0; k < Time.Minutes(26); k++) { s.Step(); Keep(s, m, p); }
                float g0 = GrudgeSum(soc, p); long t0 = s.Tick;
                for (int k = 0; k < Time.Hours(4); k++) { s.Step(); Keep(s, m, p); }
                float g1 = GrudgeSum(soc, p);
                int peace = soc.Sayings.Count(y => y.Tick > t0 && y.Topic == Topic.Peace);
                bool blankBand = s.Facts.Since(0).Any(x => x.Kind == FactKind.Relationship && x.Cause != 0 && (s.Causes.Text(x.Cause).StartsWith("와 ") || s.Causes.Text(x.Cause).StartsWith("과 ")));
                int joy = soc.Sayings.Count(y => y.Tick > t0 && y.Topic == Topic.Joy);
                Check("무리 없는 배 (다섯): 축하 뒤 네 시간 — 원한 합이 늘지 않는다 · 축하는 '좋은 일' 이야기로 웃는다 · 무리 이름 빈 '와 가 맞섰다' · 두 무리 이야기가 없다 (예전: 축하 기억을 '화해'로 읽어 모든 쌍에 원한 +0.05)",
                    soc.Celebrations == 1 && g1 <= g0 + 1e-4 && peace == 0 && !blankBand && joy > 0,
                    $"축하 {soc.Celebrations} · 원한 합 {g0:0.000} → {g1:0.000} · 두 무리 이야기 {peace} · 빈 이름 {blankBand} · 좋은 일 이야기 {joy}");
            }

            // (나) 같은 일로 이미 두 번 다툰 둘 (앙금 0.8): 다시 따지는 일이 넷이 되거나 앙금이 풀릴 때까지 (많아야 하루) 함께 쉰다 — 다툼마다 원한이 끝까지 차지 않고 (0.9 아래 —
            //     이미 깊은 앙금에는 덜 보탠다) · 거듭 다툰 이는 숙여 사과가 나온다 (예전: 다툴 때마다 +0.15 · 사과는 늘 7분의 1 — 디딤호 사흘에 1.00 · 사과 0)
            {
                var s = Worlds.Full(lay, 4); var m = s.Get<Mind>(); var soc = s.Get<Society>();
                int a = m.AddPerson(s, At(22, 1)), b = m.AddPerson(s, At(24, 1));
                m.SetPersonality(a, 0.5f, 0.5f, 0.7f, 0.4f, 0.6f); m.SetPersonality(b, 0.5f, 0.5f, 0.5f, 0.5f, 0.4f);
                var p = new List<int> { a, b };
                s.Run(2);
                int past = s.Causes.Add(CauseKind.Society, "지난 항해에 둘이 크게 다퉜다", s.Tick, At(22, 1));
                soc.Grudge[a * soc.N + b] = 0.8f;
                soc.Remember(s, a, Topic.Blame, b, past, 0.6f, At(22, 1));
                for (int k = 0; k < 2; k++)
                {
                    int fought = s.Causes.Add(CauseKind.Society, $"둘이 그 일로 또 다퉜다 ({k + 1})", s.Tick, At(22, 1), past);
                    soc.Remember(s, a, Topic.Fight, b, fought, 0.5f, At(22, 1)); soc.Remember(s, b, Topic.Fight, a, fought, 0.5f, At(24, 1));
                }
                long ap0 = soc.Apologies, ar0 = soc.Arguments;
                float top = 0;
                for (int k = 0; k < Time.Hours(24) && soc.Apologies + soc.Arguments - ap0 - ar0 < 4 && soc.GrudgeOf(a, b) >= 0.3f; k++) { s.Step(); Keep(s, m, p); top = Math.Max(top, soc.GrudgeOf(a, b)); }
                Check("같은 일로 두 번 다툰 둘 (앙금 0.8)이 다시 따진다 (넷까지 · 풀릴 때까지): 원한이 끝까지 차지 않는다 (0.9 아래) · 거듭 다툰 이는 숙여 사과가 나와 앙금이 준다 (예전: 다툴 때마다 +0.15 · 사과는 늘 7분의 1)",
                    top < 0.9f && soc.Apologies > ap0 && soc.GrudgeOf(a, b) < 0.8f,
                    $"원한 가장 높이 {top:0.00} · 끝 {soc.GrudgeOf(a, b):0.00} · 다툼 {soc.Arguments - ar0} · 사과 {soc.Apologies - ap0} · {Time.Clock(s.Tick)}");

                // 따로 지내면 (말을 나누지 않는다) 앙금은 천천히 흐려진다 — 두 주 반감 (여섯 시간에 0.5 → 0.494)
                var s2 = Worlds.Full(lay, 4); var m2 = s2.Get<Mind>(); var soc2 = s2.Get<Society>();
                int x = m2.AddPerson(s2, At(2, 2)), y = m2.AddPerson(s2, At(26, 2));
                s2.Run(2);
                soc2.Grudge[x * soc2.N + y] = 0.5f;
                for (int k = 0; k < Time.Hours(6); k++) { s2.Step(); Keep(s2, m2, new List<int> { x, y }); m2[x].C.Lonely = 0; }
                float want = 0.5f * (float)Math.Pow(0.5, 6.0 / (14 * 24));
                Check("따로 지내는 둘의 앙금은 천천히 흐려진다 (두 주 반감 — 여섯 시간에 0.5 → 0.494)", Math.Abs(soc2.GrudgeOf(x, y) - want) < 0.002f,
                    $"원한 0.5 → {soc2.GrudgeOf(x, y):0.0000} (반감대로면 {want:0.0000})");
            }
        }

        // ── DF2 소식은 들은 이만 (보고서 §4-4 · MIND 14 · 15단 당김): 세상은 '죽었다'만 남긴다 — 슬픔은 본 · 들은 이의 마음이 · 이미 아는 시신은 다시 '발견'하지 않는다 ──
        if (Do("죽음 소식"))
        {
            Console.WriteLine("\n[죽음 소식 — 본 이 · 들은 이만 슬퍼한다]");
            var legend = new Dictionary<char, string> { ['m'] = "MealDispenser", ['a'] = "Seat", ['e'] = "Bed" };
            var lay = Worlds.Ascii(CultureShip, legend);
            int At(int x, int y) => Worlds.At(lay, x, y);
            int lounge = lay.Room[At(24, 2)];
            lay.Rooms[lay.Room[At(2, 2)]].Name = "침실"; lay.Rooms[lay.Room[At(14, 2)]].Name = "식당"; lay.Rooms[lounge].Name = "휴게실";
            var s = Worlds.Full(lay, 7); var m = s.Get<Mind>(); var soc = s.Get<Society>(); var life = s.Get<Life>();
            int dead = m.AddPerson(s, At(24, 1)), saw = m.AddPerson(s, At(26, 2)), sleeper = m.AddPerson(s, At(3, 1), bed: At(3, 1));
            s.Run(2);
            int N = soc.N;
            soc.Bond[sleeper * N + dead] = 0.7f; soc.Bond[saw * N + dead] = 0.5f;
            life[m[sleeper].C.Body].Asleep = 1;
            void Hold() { ref var c = ref m[saw].C; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Bored = 0; life[c.Body].Asleep = 0; ref var z = ref m[sleeper].C; z.Hunger = 0; z.Thirst = 0; life[z.Body].Asleep = 1; }
            life[m[dead].C.Body].Blunt = 2f;   // 휴게실에서 숨진다 — 곁의 이가 본다 · 침실에서 자는 이는 모른다
            float sleepGrief = 0, sleepSad = 0; bool sleeperKnew = false;
            for (int k = 0; k < Time.Minutes(40); k++)
            {
                s.Step(); Hold();
                sleeperKnew |= m.KnowsDeath(sleeper, m[dead].C.Body);
                if (!sleeperKnew) { sleepGrief = Math.Max(sleepGrief, soc.GriefOf(sleeper)); sleepSad = Math.Max(sleepSad, m[sleeper].C.Sad); }
            }
            float sawGrief = soc.GriefOf(saw);
            bool sawKnows = m.KnowsDeath(saw, m[dead].C.Body);
            Check("자는 이는 소식을 듣기 전 슬픔 0 (침실 — 40분) · 곁에서 본 이는 안다 · 슬퍼한다 (예전: 숨진 그 틱에 배의 모두가 슬퍼했다)",
                !sleeperKnew && sleepGrief == 0 && sleepSad < 0.05f && sawKnows && sawGrief > 0.3f,
                $"자는 이 — 앎 {sleeperKnew} · 슬픔 {sleepGrief:0.00} · 서글픔 {sleepSad:0.00} / 본 이 — 앎 {sawKnows} · 슬픔 {sawGrief:0.00}");

            // 깨어 그 방에 들어서면 (시신을 본다) 그때 안다 · 슬퍼한다 — 정이 깊은 만큼
            ref var sc = ref m[sleeper].C;
            life[sc.Body].Asleep = 0; sc.Sleepy = 0;
            int near = At(25, 1); sc.Cell = near; life.Move(sc.Body, near); m[sleeper].Path.Clear(); m[sleeper].PathIdx = 0;
            for (int k = 0; k < 10; k++) { s.Step(); ref var z = ref m[sleeper].C; z.Hunger = 0; z.Sleepy = 0; life[z.Body].Asleep = 0; }
            Check("깨어 시신을 본 이는 그때 안다 · 슬퍼한다 (정 0.7 — 슬픔 0.8 넘게)", m.KnowsDeath(sleeper, m[dead].C.Body) && soc.GriefOf(sleeper) > 0.8f,
                $"앎 {m.KnowsDeath(sleeper, m[dead].C.Body)} · 슬픔 {soc.GriefOf(sleeper):0.00}");

            // 이미 아는 시신: 믿음이 흐려진 뒤 (이틀 — 믿음 칸을 비워 줄인다) 다시 봐도 '알아챘다 · 외쳐 알렸다'를 내지 않는다 (예전: 휴게실 시신을 8번 '발견')
            long f0 = s.Facts.Total;
            m[saw].Beliefs.RemoveAll(b => b.Kind == BeliefKind.Dead);
            for (int k = 0; k < 20; k++) { s.Step(); Hold(); }
            int again = s.Facts.Since(f0).Count(x => x.Kind is FactKind.Noticed or FactKind.Warned && x.Actor == saw && x.Other == (int)BeliefKind.Dead);
            int first = s.Facts.Since(0).Count(x => x.Kind is FactKind.Warned && x.Actor == saw && x.Other == (int)BeliefKind.Dead && x.Tick < f0);
            bool stillBelieves = m[saw].Beliefs.Any(b => b.Kind == BeliefKind.Dead && b.Conf > 0.5f);
            Check("이미 아는 시신은 다시 '발견'하지 않는다: 믿음이 흐려진 뒤 다시 봐도 알아챘다 · 외쳐 알렸다가 없다 (보는 것은 그대로 — 믿음은 되살아난다) · 처음 본 때는 외쳤다",
                again == 0 && stillBelieves && first >= 1, $"다시 본 뒤 사실 {again} · 믿음 {stillBelieves} · 처음 외침 {first}");
        }
    }
}
