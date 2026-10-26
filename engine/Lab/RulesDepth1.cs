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
    }
}
