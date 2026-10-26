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

            // 원인이 다른 두 얼룩 (왼쪽 · 오른쪽 물관 샘) 사이를 10분마다 오간다 (두 시간 · 열두 번 — 중간에 저장 · 되살림): 알림은 얼룩마다 한 번 — 다쳤다 둘
            //   (DF1 검토 고침 — 예전: 마지막에 알린 얼룩 하나만 기억해 오갈 때마다 다시 '다쳤다' 열두 번 · 원망 · 기록 소음)
            {
                var sw = new Sim(new SimSetup(Worlds.Ascii(Worlds.Symmetric), 1, sim => { var ff = new Field(sim.Layout, sim.Tables); sim.Add(ff); sim.Add(new Life(ff)); }));
                var f = sw.Get<Field>(); f.Adiabatic = true; f.FillAir(humidity: 0.7);
                var l = sw.Get<Life>();
                int left = sw.Causes.Add(CauseKind.Leak, "왼쪽 물관 이음매가 샜다", sw.Tick), right = sw.Causes.Add(CauseKind.Leak, "오른쪽 물관 이음매가 샜다", sw.Tick);
                for (int x = 1; x <= 9; x++)
                    for (int y = 1; y <= 5; y++)
                        if (x <= 3 || x >= 7) { int c = Worlds.At(sw.Layout, x, y); f.AddStore(c, f.T.sWater, 80); f.WetCause[c] = x <= 3 ? left : right; l.Mold[c] = 0.4f; }
                sw.Run(130);
                int body = l.AddBody(Worlds.At(sw.Layout, 2, 3));
                for (int k = 0; k < 12; k++)
                {
                    if (k == 6) { sw = sw.Clone(); l = sw.Get<Life>(); }
                    l.Move(body, Worlds.At(sw.Layout, k % 2 == 0 ? 2 : 8, 3));
                    sw.Run(Time.Minutes(10));
                }
                var hurts = sw.Facts.Since(0).Where(x => x.Kind == FactKind.Hurt && x.Actor == body).Select(x => sw.Causes.Story(x.Cause)).ToList();
                Check("원인이 다른 두 얼룩 사이를 열두 번 오가도 (중간에 저장 · 되살림) '다쳤다'는 얼룩마다 한 번 — 둘 (왼쪽 물관 샘 · 오른쪽 물관 샘에 하나씩)",
                    hurts.Count == 2 && hurts.Count(h => h.StartsWith("왼쪽 물관")) == 1 && hurts.Count(h => h.StartsWith("오른쪽 물관")) == 1,
                    $"다쳤다 {hurts.Count}번 · {string.Join(" / ", hurts.Take(3))}");
            }
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

            // (다-2) 맑은 20℃ 방에서 곪은 상처 (감염 0.95) · 앓음의 열: 설정점이 40℃를 넘어 쓰러진다 — '열이 너무 올라 쓰러졌다' · 사슬이 곪은 상처에 닿는다
            //   (DF1 검토 고침 — 예전: 체온만 보고 '몸이 너무 뜨거워졌다' — 뿌리 없음 · 둘레에 달군 장치 · 멈춘 냉각이 있으면 그것을 탓했다)
            var sf = BodyWorld(); var lf = sf.Get<Life>(); int bf = lf.AddBody(Worlds.At(sf.Layout, 5, 3));
            int sore = sf.Causes.Add(CauseKind.Biology, "다리 상처가 곪았다", sf.Tick);
            for (long k = 0; k < Time.Hours(12) && lf[bf].Conscious != 0; k++) { lf[bf].Infect = 0.95f; lf[bf].InfectCause = sore; lf[bf].Ill = 2; sf.Step(); }
            var downF = sf.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Collapsed && x.Actor == bf);
            string sfS = downF.Cause != 0 ? sf.Causes.Story(downF.Cause) : "";
            Audit(sf, "열");
            Check("감염 · 앓음의 열로 쓰러지면 (맑은 20℃ 방) '열이 너무 올라 쓰러졌다' — 사슬이 곪은 상처에 닿는다 · '몸이 너무 뜨거워'(바깥 열)가 아니다",
                downF.Kind == FactKind.Collapsed && sfS.StartsWith("다리 상처가 곪았다") && sfS.Contains("열이 너무 올라") && !sfS.Contains("너무 뜨거워"),
                $"'{sfS}' (체온 {lf[bf].CoreK - 273.15:0.0}℃ · {Time.Clock(sf.Tick)})");
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

        // ── DF3 막힌 곳 (보고서 §4-1 · MIND 8단 당김): 막힘을 사건이 아니라 문 · 방 · 연장선 · 계획에 붙여 기억한다 — 사건 이름이 바뀌어도 이어진다 ──
        if (Do("막힌 곳"))
        {
            Console.WriteLine("\n[막힌 곳 — 같은 까닭으로 같은 곳에 거듭 보내지 않는다]");
            bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
            // 기압 차 문: 왼쪽 방 외판에 구멍 · 그 방 침대에 자는 사람 (숨이 막혀 쓰러지고 심장이 멎는다 — 같은 방에 사건이 잇달아 선다) · 로봇 둘은 오른쪽 방
            {
                var legend = new Dictionary<char, string> { ['x'] = "OxygenMaskBox", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed", ['h'] = "HeatExchanger" };
                var lay = Worlds.Ascii(NoAirlockShip, legend);
                int At(int x, int y) => Worlds.At(lay, x, y);
                int door = lay.DoorAt[At(11, 2)];
                var s = Worlds.Full(lay, 3);
                var f = s.Get<Field>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>(); var net = s.Get<Network>(); var life = s.Get<Life>();
                int p = m.AddPerson(s, At(3, 3), bed: At(3, 3));
                m.AddRobot(s, At(22, 3), AgentKind.Robot, At(22, 3)); m.AddRobot(s, At(19, 2), AgentKind.Robot, At(19, 2));
                s.Run(2);
                net.AirReserve = 2e6;
                life[m[p].C.Body].Asleep = 1;
                long f0 = s.Facts.Total;
                f.SetHole(At(3, 0), 0.02f);
                s.Run((int)Time.Minutes(25));
                var bl = s.Facts.Since(f0).Where(x => x.Kind == FactKind.Blocked && x.Other == door && x.Actor >= 0 && !m[x.Actor].IsPerson).ToList();
                if (Debug)
                {
                    foreach (var x in bl) Console.WriteLine($"    {Time.Clock(x.Tick)} {m.NameOf(x.Actor)} {(SnagKind)(int)x.Size} · {s.Causes.Text(x.Cause)}");
                    foreach (var l in sm.Log) Console.WriteLine($"    {Time.Clock(l.tick)} {l.text}");
                }
                bool devised = s.Facts.Since(f0).Any(x => x.Kind is FactKind.Devised or FactKind.Decision);
                Check("같은 기압 문에 두 번 넘게 보내지 않는다: 우주로 트인 방에 사건이 잇달아 서도 (빠졌다 · 쓰러졌다 · 심정지) 그 문에서 막힌 로봇은 둘까지 · 주컴퓨터는 다른 길을 궁리한다",
                    bl.Count <= 2 && devised, $"그 문에서 막힘 {bl.Count}번 · 궁리 {devised}");
            }

            // 같은 구멍 막기 계획 (운석 장면 — 디딤호 · 1.5kg 운석이 침실 둘을 뚫는다 · 둘레 통로는 외판이 없고 빠진 침실 둘 쪽으로 샌다): 두 갈래 실패 —
            //   (가) 둘레 통로 '외판 구멍 막기'는 다 해도 그대로 샌다 (막을 구멍이 외판에 없다) — 다 마쳤는데 같은 사건이 그대로면 두 번째에 그 걸음을 통하지 않은 것으로 (씨 7)
            //   (나) 미리 돌려 본 세계에서 문이 안 열렸다 — 이 세계의 문은 멀쩡해 '막힌 까닭이 그대로인가'가 곧 풀려 같은 계획을 1분에 여덟 번 (씨 6) → 10분은 그 걸음을 막힌 것으로
            {
                // (DF7 뒤 씨 2 → 1: 그 밤 깨어 있던 이 · 있던 곳이 달라져 씨 2는 궁리할 일이 없다. 이 장면은 이제 두 갈래 고침을 함께 꺼도 씨 1~64 어느 것도 같은 계획을 되풀이하지 않는다 —
                //  DF7 앞 코드도 씨 1~24에서 같다 · 막힌 곳 기억 · 같은 방의 다음 안이 먼저 막는다. 여기서는 궁리가 서고 되풀이되지 않는지를 본다)
                // (DF10 뒤 씨 1 · 3 → 6 · 5: 깬 이가 나쁜 공기에서 다시 잠들지 못하고 · 산소 포화 곡선과 숨참을 알아채는 문턱이 바뀌어 그 밤의 일이 달라졌다 —
                //  씨 1 · 3은 첫 방법으로 다 막아 궁리할 일이 없다 · 씨 6은 궁리 둘 · 씨 5는 셋 · 씨 1~14 어느 것도 같은 계획을 둘 넘게 세우지 않는다)
                var runs = new[] { 6UL, 5UL }.AsParallel().AsOrdered().Select(seed =>
                {
                    var s = ShipWorld.Crewed("Didim", seed, 6, 3);
                    s.Run(Time.Minutes(80));   // 새벽 한 시 스무 분 — 모두 잠들었다
                    long f0 = s.Facts.Total;
                    int rock = s.Causes.Add(CauseKind.Setup, "운석 하나가 배를 향해 온다", s.Tick);
                    s.Get<Outside>().AddRock(1.5, 6000, -1, 0.15, 2, 25_000, rock);
                    for (int k = 0; k < 10 && !s.Facts.Since(f0).Any(x => x.Kind == FactKind.Impact); k++) s.Step();
                    // 판단 시험이라 상황을 그대로 차린다: 옛 충돌 셈이 낸 구멍 (침실 둘 외판 0.35m² · 둘레 통로 쪽 벽 14cm²) — DF9 뒤 1.5kg 운석은 외판에 운석 지름의 구멍 (170cm²)만 낸다 (씨도 DF9 뒤 다시 골랐다 — 씨 7은 이제 궁리할 일이 없다 · DF6 뒤 씨 10 → 3: 재배대 · 물 재생기가 바뀌어 씨 10은 궁리할 일이 없다 — 씨 3은 궁리 여섯 번)
                    int hit = s.Facts.Since(f0).Where(x => x.Kind == FactKind.Impact).Select(x => x.Cell).DefaultIfEmpty(-1).First();
                    var stc = s.Get<Structure>();
                    int torn = hit >= 0 ? s.Causes.Add(CauseKind.Impact, "운석이 외판을 크게 찢었다", s.Tick, hit, rock) : rock;
                    if (hit >= 0) { stc.Tear(s, hit, 0.35f, torn); stc.Tear(s, hit - 10, 0.0014f, torn); }
                    s.Run(Time.Minutes(15) - 2);
                    var dv = s.Facts.Since(f0).Where(x => x.Kind == FactKind.Devised).Select(x => s.Causes.Text(x.Cause)).ToList();
                    int most = dv.Count == 0 ? 0 : dv.GroupBy(t => t).Max(g => g.Count());
                    if (Debug) foreach (var t in dv) Console.WriteLine($"    ({seed}) 궁리: {t}");
                    return (seed, n: dv.Count, most);
                }).ToList();
                Check("같은 구멍 막기 계획을 세 번 세우지 않는다: 다 마쳤는데 그대로 새면 그 걸음은 통하지 않은 것으로 · 미리 돌려 보니 안 된 걸음은 10분 막힌 것으로 (같은 계획 둘까지 — 운석 장면 둘)",
                    runs.All(r => r.most <= 2 && r.n > 0), string.Join(" / ", runs.Select(r => $"씨 {r.seed}: 궁리 {r.n}번 · 같은 계획 가장 많이 {r.most}번")));
            }

            // 녹은 연장선: 왼쪽 생명유지실 (산소 발생기 · 대형 냉각 펌프 · 냉각 펌프 둘 — 짐 51A가 연장선 정격 32A의 1.5배를 넘는다) · 제 급전선 길이 젖어 다시 올릴 수 없다
            //   → 로봇이 오른쪽 방에서 연장선을 끌어온다 → 1분 넘게 넘쳐 피복이 녹는다 (오른쪽 급전선까지 끊긴다) — 같은 짐으로 같은 길을 다시 깔지 않는다 (짐을 줄여서 다시)
            //   (검토 고침) 일곱 시간 전에 그 방 연장선이 짐이 넘쳐 녹은 기록이 있어도 (막힌 곳이 이미 풀렸다) 새 녹음은 첫 번 — 짐을 줄여 보지도 않고 '또 녹았다'로 그만두지 않는다
            {
                const string HeavyShip = @"
##################
#p..O..k...#c...b#
#..W..Uu...D..q..#
#....u..s..#r...w#
##################";
                (int laid, int melted, bool lit, int shed, bool relit, float maxA) Melt(bool oldBurn)
                {
                    var lay = Worlds.Ascii(HeavyShip, new Dictionary<char, string>(NetLegend) { ['U'] = "LargeCoolantPump", ['u'] = "CoolantPump" });
                    int At(int x, int y) => Worlds.At(lay, x, y);
                    int left = lay.Room[At(4, 2)], right = lay.Room[At(13, 2)];
                    var s = Worlds.Full(lay, 1);
                    var net = s.Get<Network>(); var m = s.Get<Mind>(); var f = s.Get<Field>(); var sm = s.Get<ShipMind>();
                    m.AddRobot(s, At(12, 3), AgentKind.Robot, At(12, 3));
                    s.Run(3);
                    if (oldBurn)
                    {   // ⑥의 기록: 일곱 시간 전에 오른쪽 방에서 끌어온 연장선이 짐이 넘쳐 녹았다 (짐 막힌 곳은 여섯 시간 — 이미 풀렸다)
                        net.Burns.Add(new JumperBurn { Seq = ++net.BurnSeq, Tick = s.Tick - Time.Hours(7), From = right, To = left, Cell = At(9, 2), Why = 1, Amps = 51 });
                        s.Run(5);
                        m[0].C.Battery = 1;
                    }
                    var path = net.Feeders[net.RoomFeeder[left]].Path;
                    var jp = net.JumperPath(right, left)!;
                    f.AddStore(path.Skip(1).First(c => Array.IndexOf(jp, c) < 0), f.T.sWater, 60);
                    long f0 = s.Facts.Total;
                    float maxA = 0;
                    bool relit = false;   // 짐을 줄인 채 연장선으로 밝았다 (제 급전선이 살아나면 연장선은 걷힌다)
                    for (int k = 0; k < 120; k++)
                    {
                        s.Run((int)Time.Minutes(0.5));
                        foreach (var j in net.Jumpers) maxA = Math.Max(maxA, j.Amps);
                        if (sm.LineSheds > 0 && net.Jumpers.Count == 1 && net.RoomPowered(left) && !net.OwnFeederLive(left)) relit = true;
                    }
                    var facts = s.Facts.Since(f0).ToList();
                    int laid = facts.Count(x => x.Kind == FactKind.Jumper && x.Other == 1);
                    int melted = facts.Count(x => x.Kind == FactKind.Short && s.Causes.Story(x.Cause).Contains("정격을 넘게"));
                    int shed = facts.Count(x => x.Kind == FactKind.LoadShed && net.Devices[x.Other].Room == left);
                    bool lit = net.RoomPowered(left) && net.Jumpers.Count == 1;
                    if (Debug) { Console.WriteLine($"    (옛 녹음 {oldBurn}) 가장 큰 전류 {maxA:0}A · 끝에 연장선 {net.Jumpers.Count} · 제 급전선 {net.OwnFeederLive(left)}"); foreach (var l in sm.Log.Where(l => !l.text.Contains("작업대가 없다") && !l.text.Contains("원료"))) Console.WriteLine($"    {Time.Clock(l.tick)} {l.text}"); }
                    return (laid, melted, lit, shed, relit, maxA);
                }
                var oldT = Task.Run(() => Melt(true));
                var now = Melt(false); var old = oldT.Result;
                Check("녹은 연장선 길을 다시 깔지 않는다: 짐이 넘쳐 녹은 뒤에는 그 방 짐을 줄여 (덜 급한 것을 끄고) 다시 잇는다 — 한 시간에 녹음 하나 · 끝에 그 방은 연장선으로 밝다",
                    now.melted <= 1 && now.laid >= 2 && now.lit, $"깐 연장선 {now.laid} · 넘쳐 녹음 {now.melted} · 짐 줄임 {now.shed} · 끝에 밝음 {now.lit} · 가장 큰 전류 {now.maxA:0}A");
                Check("풀린 막힌 곳은 번을 다시 센다: 일곱 시간 전에 녹은 기록이 있어도 새 녹음은 첫 번 — 짐을 줄여 다시 잇고 그 방은 연장선으로 밝다 (예전엔 '또 녹았다'로 연장선을 그만뒀다)",
                    old.melted <= 1 && old.laid >= 2 && old.shed >= 1 && old.relit, $"깐 연장선 {old.laid} · 넘쳐 녹음 {old.melted} · 짐 줄임 {old.shed} · 줄인 채 연장선으로 밝음 {old.relit}");
            }

            // 갈 길이 없는 것은 몸의 사정일 수 있다 (검토 고침 — 평가 결함의 문 없는 방 배): 아래 통로 식사기 밑 선이 끊겼다 · 위 오른쪽 문 없는 방에 갇힌 로봇 (하나 · 둘) ·
            //   아래 통로 로봇은 전지가 거의 없다 (25분 뒤에 찬다) — 갇힌 로봇이 거듭 막혀도 (같은 몸이 두 번 · 서로 다른 몸 둘) 막힌 적 없는 로봇이 닿으면 보낸다 → 선을 잇는다
            //   (예전엔 같은 몸의 막힘 둘로 '맡긴 이 둘이 막혔다' · 그 방 일을 한 시간 막아 60분이 지나도록 아무도 가지 않았다)
            {
                (long sentAt, bool fixedLine, bool saidTwo) Trapped(int trapped)
                {
                    var lay = Worlds.Ascii(Worlds.MindShipComputer.Replace("#..p.b....D", "#..p.b....#").Replace("#####D########D##########", "#####D###################").Replace("#.........#.............#", "#.........#........pb...#"), Worlds.MindLegend);
                    int At(int x, int y) => Worlds.At(lay, x, y);
                    var s = Worlds.Full(lay, 34);
                    var m = s.Get<Mind>(); var sm = s.Get<ShipMind>(); var net = s.Get<Network>();
                    for (int i = 0; i < trapped; i++) m.AddRobot(s, At(17 + 3 * i, 3), AgentKind.Robot, At(17 + 3 * i, 3));   // 문 없는 방에 갇힌 로봇
                    int late = m.AddRobot(s, At(20, 7), AgentKind.Robot, At(20, 7));                                              // 아래 통로 · 전지가 거의 없다
                    s.Run(12);
                    long f0 = s.Facts.Total; int l0 = sm.Log.Count;
                    int cell = net.Devices.First(d => d.Type == "MealDispenser").Cell;
                    net.CutLines(s, cell, 1, s.Causes.Add(CauseKind.Setup, "식사기 밑 전선을 쥐가 갉았다", s.Tick));
                    for (int k = 0; k < Time.Minutes(60); k++) { m[late].C.Battery = k < Time.Minutes(25) ? 0.1f : k == Time.Minutes(25) ? 1f : m[late].C.Battery; s.Step(); }
                    long sentAt = s.Facts.Since(f0).Where(x => x.Kind == FactKind.ComputerAssign && x.Actor == late).Select(x => x.Tick).DefaultIfEmpty(-1).Min();
                    if (Debug) foreach (var l in sm.Log.Skip(l0).Where(l => !l.text.Contains("원료"))) Console.WriteLine($"    (갇힘 {trapped}) {Time.Clock(l.tick)} {l.text}");
                    return (sentAt, net.LineCut[cell] == 0, sm.Log.Skip(l0).Any(l => l.text.Contains("맡긴 이 둘이")));
                }
                var twoT = Task.Run(() => Trapped(2));
                var one = Trapped(1); var two = twoT.Result;
                Check("갇힌 몸의 막힘으로 그 방을 막지 않는다: 문 없는 방에 갇힌 로봇이 같은 일에 두 번 막혀도 '맡긴 이 둘'이 아니다 · 전지가 찬 로봇을 보내 선을 잇는다 · 갇힌 로봇 둘이 막혀도 막힌 적 없는 로봇은 닿으면 보낸다",
                    one.sentAt >= 0 && one.fixedLine && !one.saidTwo && two.sentAt >= 0 && two.fixedLine,
                    $"갇힌 로봇 하나: 보냄 {one.sentAt >= 0} · 이음 {one.fixedLine} · '맡긴 이 둘' {one.saidTwo} / 둘: 보냄 {two.sentAt >= 0} · 이음 {two.fixedLine}");
            }
        }

        // ── DF4 처지를 본다 (보고서 §4-8): 부탁 · 맡김은 지금 처지를 본다 — 숨이 찬 이 · 사건 방 안의 이 · 당사자에게는 부탁하지 않는다 · 손 없는 로봇은 손일에 뽑지 않고 도착 시간으로 고른다 ·
        //    막 깬 이 · 되살아난 이는 잠기운 · 기운 없음 (⑦이 내는 값)으로 손발이 느리다 ──
        if (Do("처지 보기"))
        {
            Console.WriteLine("\n[처지 보기 — 부탁 · 맡김은 지금 처지를 본다]");
            bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
            // 부탁: 로봇 없는 배 · 오른쪽 위 방에 불 (그 방 문 둘은 잠겼다 — 안의 이는 나오지 못한다) — 불난 방 안의 이 · 잠긴 문 밖 통로의 숨이 찬 이 (폐가 상했다 — 깨어 있다) · 왼쪽 방의 멀쩡한 이
            {
                var lay = Worlds.Ascii(Worlds.MindShipComputer.Replace("w", "."), Worlds.MindLegend);   // 작업대 없이 (일감 만들기로 자리를 뜨지 않게)
                int At(int x, int y) => Worlds.At(lay, x, y);
                var s = Worlds.Full(lay, 5);
                var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>(); var st = s.Get<Structure>();
                int pin = m.AddPerson(s, At(20, 3)), pbreath = m.AddPerson(s, At(14, 7)), pfar = m.AddPerson(s, At(3, 2));
                s.Run(3);
                foreach (int door in new[] { At(10, 3), At(14, 6) }) st.LockDoor(lay.DoorAt[door], s.Tick + Time.Hours(1));
                life[m[pbreath].C.Body].LungDamage = 0.5f;
                m[pbreath].C.SkFire = 1; m[pfar].C.SkFire = 0.2f;   // 숨이 찬 이가 불을 가장 잘 끈다 — 처지를 보지 않으면 그에게 부탁한다 (DF7 덤: 예전 차림은 처지를 보지 않아도 길이 멀어 그를 고르지 않았다)
                long f0 = s.Facts.Total;
                int seed = s.Causes.Add(CauseKind.Setup, "의자 밑에서 불씨가 튀었다", s.Tick);
                foreach (var (x, y) in new[] { (15, 1), (16, 1), (15, 2) }) ch.Ignite(At(x, y), 3e6, seed);
                for (int k = 0; k < Time.Minutes(4); k++) { s.Step(); foreach (int q in new[] { pin, pbreath, pfar }) { ref var c = ref m[q].C; c.Hunger = 0; c.Thirst = 0; c.Sleepy = 0; c.Bored = 0; } }
                var asks = s.Facts.Since(f0).Where(x => x.Kind == FactKind.Decision && x.Other == (int)GoalKind.FightFire && x.Actor >= 0).ToList();
                if (Debug) foreach (var l in sm.Log.Where(l => l.text.Contains("님,") || l.text.Contains(": \""))) Console.WriteLine($"    {Time.Clock(l.tick)} {l.text}");
                int bin = asks.Count(x => x.Actor == pin), bbreath = asks.Count(x => x.Actor == pbreath), good = asks.Count(x => x.Actor == pfar);
                // 빠진 방에 갇힌 이 (운석 장면의 꼴 — 왼쪽 방 외판에 구멍 · 그 방 침대에 자던 이 · 로봇 둘은 다른 사건에 바쁘다): 숨이 막혀 가는 이에게 옆 방 일을 부탁하지 않는다
                var vlay = Worlds.Ascii(NoAirlockShip, new Dictionary<char, string> { ['x'] = "OxygenMaskBox", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed", ['h'] = "HeatExchanger" });
                var vs = Worlds.Full(vlay, 3);
                var vm = vs.Get<Mind>();
                int victim = vm.AddPerson(vs, Worlds.At(vlay, 3, 3), bed: Worlds.At(vlay, 3, 3));
                vm.AddRobot(vs, Worlds.At(vlay, 22, 3), AgentKind.Robot, Worlds.At(vlay, 22, 3)); vm.AddRobot(vs, Worlds.At(vlay, 19, 2), AgentKind.Robot, Worlds.At(vlay, 19, 2));
                vs.Run(2);
                vs.Get<Network>().AirReserve = 2e6; vs.Get<Life>()[vm[victim].C.Body].Asleep = 1;
                long v0 = vs.Facts.Total;
                vs.Get<Field>().SetHole(Worlds.At(vlay, 3, 0), 0.02f);
                vs.Run((int)Time.Minutes(5));
                int vasks = vs.Facts.Since(v0).Count(x => x.Kind == FactKind.Decision && x.Actor == victim && vs.Causes.Text(x.Cause).Contains("부탁"));
                Check("숨이 찬 이 · 사건 방 안의 이에게 부탁 0: 불난 방 안의 이 · 숨이 찬 이 (가까워도) 대신 멀쩡한 이에게 부탁한다 · 빠진 방에 갇혀 숨이 막혀 가는 이에게 옆 방 일을 부탁하지 않는다",
                    bin + bbreath + vasks == 0 && good >= 1, $"불난 방 안의 이에게 부탁 {bin} · 숨이 찬 이에게 {bbreath} · 멀쩡한 이에게 {good} / 빠진 방에 갇힌 이에게 {vasks}");
            }

            // 맡김: 왼쪽 방 산소 발생기 밑 전선이 끊겼다 — 로봇 셋: 곁의 로봇 1은 팔이 상해 손을 못 쓰고 (팔 0.6 · 다리 0.88) · 로봇 2는 다리가 상해 느리다 (0.51배) · 로봇 3은 멀쩡하고 오른쪽 방
            {
                var lay = Worlds.Ascii(NetShip, NetLegend);
                int At(int x, int y) => Worlds.At(lay, x, y);
                var s = Worlds.Full(lay, 2);
                var m = s.Get<Mind>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
                int r1 = m.AddRobot(s, At(6, 2), AgentKind.Robot, At(12, 3)), r2 = m.AddRobot(s, At(9, 3), AgentKind.Robot, At(12, 3)), r3 = m.AddRobot(s, At(13, 3), AgentKind.Robot, At(12, 3));
                s.Run(3);
                ref var b1 = ref net.Robot(m[r1].C.Mech); b1.Arms = 0.6f; b1.Legs = 0.88f;
                net.Robot(m[r2].C.Mech).Legs = 0.7f;
                foreach (int r in new[] { r1, r2, r3 }) m[r].C.Battery = 1;
                long f0 = s.Facts.Total;
                int o2 = net.Devices.First(d => d.Type == "OxygenGenerator").Id;
                net.CutLines(s, net.Devices[o2].Cell, 1, s.Causes.Add(CauseKind.Setup, "산소 발생기 밑 전선을 쥐가 갉았다", s.Tick));
                s.Run((int)Time.Minutes(3));
                var sent = s.Facts.Since(f0).Where(x => x.Kind == FactKind.ComputerAssign && x.Other == (int)IncidentKind.LineCut).Select(x => x.Actor).ToList();
                if (Debug) foreach (var l in sm.Log.Where(l => l.text.Contains("→"))) Console.WriteLine($"    {Time.Clock(l.tick)} {l.text}");
                Check("손 없는 로봇은 선 잇기에 뽑히지 않는다 · 도착 시간으로 고른다: 곁의 팔 상한 로봇 · 가깝지만 다리가 상해 느린 로봇 대신 멀쩡한 로봇 3",
                    sent.Count >= 1 && sent[0] == r3 && !sent.Contains(r1), $"맡긴 차례 {string.Join(" → ", sent.Select(m.NameOf))}");
            }

            // 잠기운: 오른쪽 위 방 의자에 불 — 그 방에서 40분 자다 불에 깬 이와 깨어 있던 이 (같은 사람 · 같은 씨) — 불을 알아챈 때부터 끌 때까지
            {
                (double sec, bool done) Fight(bool slept)
                {
                    var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
                    int At(int x, int y) => Worlds.At(lay, x, y);
                    var s = Worlds.Full(lay, 9);
                    var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var life = s.Get<Life>();
                    int p = m.AddPerson(s, At(18, 2));
                    m[p].C.Brave = 0.9f; m[p].C.SkFire = 0.8f;
                    for (int k = 0; k < Time.Minutes(40); k++)
                    {
                        ref var c = ref m[p].C; c.Hunger = 0; c.Thirst = 0; c.Bored = 0;
                        if (slept) { life[c.Body].Asleep = 1; c.Sleepy = 0.5f; } else { life[c.Body].Asleep = 0; c.Sleepy = 0; }
                        s.Step();
                    }
                    long f0 = s.Facts.Total;
                    ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Setup, "의자 밑에서 불씨가 튀었다", s.Tick));
                    for (int k = 0; k < Time.Minutes(10) && !s.Facts.Since(f0).Any(x => x.Kind == FactKind.Extinguished && x.Actor == p); k++) s.Step();
                    var fs = s.Facts.Since(f0).ToList();
                    long seen = fs.Where(x => x.Actor == p && x.Kind is FactKind.Noticed or FactKind.Warned).Select(x => x.Tick).DefaultIfEmpty(-1).Min();
                    long outAt = fs.Where(x => x.Kind == FactKind.Extinguished && x.Actor == p).Select(x => x.Tick).DefaultIfEmpty(-1).Min();
                    return (seen >= 0 && outAt >= 0 ? Time.ToSeconds(outAt - seen) : -1, outAt >= 0);
                }
                var wokeT = Task.Run(() => Fight(true)); var awake = Fight(false); var woke = wokeT.Result;
                Check("막 깬 이의 소화는 깨어 있던 이보다 느리다: 40분 자다 깬 이는 잠기운으로 손발이 느려 (⑦이 내는 값 — 몇 분에 걸쳐 풀린다) 불을 알아챈 뒤 끄기까지 더 걸린다",
                    woke.done && awake.done && woke.sec > awake.sec * 1.3, $"깬 이 {woke.sec:0}초 · 깨어 있던 이 {awake.sec:0}초");
            }
        }
    }
}
