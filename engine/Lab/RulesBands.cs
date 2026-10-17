using ShipEngine;

namespace Lab;

/// <summary>⑨ 사회 (바퀴 2) — 저절로 생기는 무리 · 지도자: 정의 그물에서 무리가 생기고 · 존경받는 이가 이끈다 · 그의 외침은 더 믿고 · 다툼을 말리고 · 무리는 제 사람을 감싼다. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void BandRules()
    {
        if (!Do("무리")) return;
        Console.WriteLine("\n[⑨ 무리 · 지도자]");
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        void Mutual(Society so, int a, int b, float v) { so.Bond[a * so.N + b] = v; so.Bond[b * so.N + a] = v; }
        void Calm(Mind m, params int[] ps) { foreach (int p in ps) { m[p].C.Hunger = 0; m[p].C.Thirst = 0; m[p].C.Sleepy = 0; } }

        // ── 무리가 생긴다: 서로 정이 0.4 넘는 셋 (남들이 가장 믿는 이가 이끈다 · 이름은 정을 가장 많이 주고받는 이) — 낯선 이는 끼지 않는다 · 정이 식으면 흩어진다 ──
        {
            var s = Worlds.Full(lay, 3);
            var m = s.Get<Mind>(); var so = s.Get<Society>();
            int a = m.AddPerson(s, At(13, 2)), b = m.AddPerson(s, At(16, 2)), c = m.AddPerson(s, At(19, 3)), d = m.AddPerson(s, At(4, 2));
            s.Run(1);
            Mutual(so, a, b, 0.7f); Mutual(so, a, c, 0.7f); Mutual(so, b, c, 0.5f); Mutual(so, a, d, 0.2f);
            foreach (int j in new[] { a, b, d }) so.TrustOf[j * so.N + c] = 0.9f;   // 모두 c를 믿는다
            for (int k = 0; k < Time.Hours(1); k++) { Calm(m, a, b, c, d); s.Step(); }
            var made = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Relationship && x.Size == 2);
            string text = made.Kind == FactKind.Relationship ? s.Causes.Text(made.Cause) : "";
            var band = so.Bands.Count == 1 ? so.Bands[0] : default;
            bool formed = so.Bands.Count == 1 && band.Size == 3 && so.BandIdOf(d) < 0 && band.Leader == c && band.Center == a && so.SameBand(a, c)
                && text.Contains(m.NameOf(a) + "네") && text.Contains(m.NameOf(c));
            // 정이 식는다 → 다음 한 시간에 흩어진다
            Mutual(so, a, b, 0.1f); Mutual(so, b, c, 0.1f);
            for (int k = 0; k < Time.Hours(1); k++) { Calm(m, a, b, c, d); s.Step(); }
            var broke = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Relationship && x.Size == -2);
            Check("무리: 서로 정이 0.4 넘는 셋이 무리가 된다 — '○○네' (정을 가장 많이 주고받는 이) · 남들이 가장 믿는 이가 이끈다 · 낯선 이는 빠진다 · 정이 식으면 흩어진다",
                formed && so.Bands.Count == 0 && broke.Kind == FactKind.Relationship && s.Causes.Text(broke.Cause).Contains("흩어졌다"),
                $"\"{text}\" · 무리 {band.Size}명 · 이끄는 이 {(band.Size > 0 ? m.NameOf(band.Leader) : "-")} · 낯선 이 {so.BandIdOf(d)} · 흩어짐 \"{(broke.Kind == FactKind.Relationship ? s.Causes.Text(broke.Cause) : "")}\"");
        }

        // ── 지도자의 외침: 불을 본 이가 외친다 — 듣는 이를 이끄는 사람이면 더 믿는다 (같은 거리 · 같은 소리) ──
        {
            (float conf, int leader, long shouts) Shout(bool band)
            {
                var s = Worlds.Full(lay, 7);
                var m = s.Get<Mind>(); var so = s.Get<Society>(); var ch = s.Get<Chemistry>();
                int sp = m.AddPerson(s, At(12, 2)), h = m.AddPerson(s, At(13, 7)), t = m.AddPerson(s, At(2, 1));
                s.Run(1);
                m[sp].C.SkFire = 1;                                       // 솜씨가 가장 좋다 → 무리가 생기면 이끈다
                if (band) { Mutual(so, sp, h, 0.6f); Mutual(so, sp, t, 0.6f); Mutual(so, h, t, 0.6f); }
                so.Regroup(s); s.Run(2);
                if (Debug) Console.WriteLine($"    불 앞: 외칠 이 칸 {lay.X(m[sp].C.Cell)},{lay.Y(m[sp].C.Cell)} {m.Thought(sp)} · 들을 이 {lay.X(m[h].C.Cell)},{lay.Y(m[h].C.Cell)} {m.Thought(h)}");
                ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Story, "의자 밑에서 불씨가 튀었다", s.Tick));
                float conf = 0;
                for (int k = 0; k < Time.Minutes(2); k++)
                {
                    s.Step();
                    foreach (var bl in m[h].Beliefs) if (bl.Kind == BeliefKind.Fire && bl.Src == BeliefSource.Told) conf = Math.Max(conf, bl.Conf);
                }
                if (Debug) Console.WriteLine($"    {(band ? "무리" : "남")}: 들은 불 {conf:0.000} · 이끄는 이 {so.LeaderOf(h)} · 믿음 {string.Join(" ", m[h].Beliefs.Select(x => $"{x.Kind}/{x.Src}/{x.Conf:0.00}"))}");
                return (conf, so.LeaderOf(h), s.Facts.CountOf(FactKind.Warned));
            }
            var led = Shout(true); var plain = Shout(false);
            Check("지도자의 외침: 같은 외침도 나를 이끄는 이가 하면 더 믿는다 (말한 이를 믿는 만큼 · 지도자면 더)",
                led.leader == 0 && plain.leader < 0 && led.shouts > 0 && plain.shouts > 0 && plain.conf > 0.05f && led.conf >= plain.conf * 1.15f,
                $"이끄는 이의 외침 → 믿음 {led.conf:0.00} / 남의 외침 → {plain.conf:0.00} · 외침 {led.shouts} · {plain.shouts}");
        }

        // ── 지도자가 다툼을 말린다: 겁 많고 덜 다정한 이도 제 무리 사람이 맞으면 나서서 말린다 (무리가 아니면 지켜만 본다) ──
        {
            (bool stopped, float blunt, float trust) Fight(bool band)
            {
                var s = Worlds.Full(lay, 5);
                var m = s.Get<Mind>(); var so = s.Get<Society>(); var life = s.Get<Life>();
                int x = m.AddPerson(s, At(4, 2)), y = m.AddPerson(s, At(6, 2)), l = m.AddPerson(s, At(7, 4)), t = m.AddPerson(s, At(20, 4));
                m.SetPersonality(x, 0.5f, 0.3f, 0.6f, 0.05f, 0.95f);
                m.SetPersonality(y, 0.5f, 0.5f, 0.6f, 0.5f, 0.3f);
                m.SetPersonality(l, 0.5f, 0.5f, 0.5f, 0.3f, 0.5f);
                s.Run(1);
                m[l].C.Brave = 0.2f; m[l].C.SkMedic = 1;
                if (band) { Mutual(so, y, l, 0.6f); Mutual(so, y, t, 0.6f); Mutual(so, l, t, 0.6f); }
                so.Regroup(s); s.Run(2);
                so.Grudge[x * so.N + y] = 0.8f;
                m[x].C.Stress = 0.9f; m[x].C.Anger = 0.9f;
                float trust0 = so.TrustIn(y, l);
                for (int k = 0; k < Time.Minutes(40); k++) { s.Step(); Calm(m, x, y, l); m[l].C.Bored = 0; }
                bool stopped = so.Sayings.Any(q => q.Speaker == l && q.Topic == Topic.Fight);
                if (Debug) Console.WriteLine($"    {(band ? "무리" : "남")}: 싸움 {so.Fights} · 말림 {stopped} · 타박 {life[m[y].C.Body].Blunt:0.00} · 이끄는 이 {so.LeaderOf(y)} · 지도자 {m.Thought(l)}");
                return (stopped && so.Fights > 0, life[m[y].C.Body].Blunt, so.TrustIn(y, l) - trust0);
            }
            var led = Fight(true); var alone = Fight(false);
            Check("지도자가 말린다: 제 무리 사람이 맞으면 겁 많은 지도자도 나서 말린다 → 덜 다치고 · 말린 이를 더 믿는다 (무리가 아니면 나서지 않는다)",
                led.stopped && !alone.stopped && led.blunt < alone.blunt && led.trust > 0,
                $"무리: 말림 {led.stopped} · 타박 {led.blunt:0.00} · 믿음 +{led.trust:0.00} / 남: 말림 {alone.stopped} · 타박 {alone.blunt:0.00}");
        }

        // ── 감싸기: 같은 무리 사람의 험담은 덜 다정한 이도 막아선다 · 무리가 아니면 소문이 퍼진다 ──
        {
            (int defend, int spread) Gossip(bool band)
            {
                var s = Worlds.Full(lay, 9);
                var m = s.Get<Mind>(); var so = s.Get<Society>();
                int a = m.AddPerson(s, At(14, 2)), b = m.AddPerson(s, At(16, 3)), x = m.AddPerson(s, At(4, 2)), t = m.AddPerson(s, At(3, 4));
                m.SetPersonality(a, 0.5f, 0.5f, 0.9f, 0.05f, 0.5f);
                m.SetPersonality(b, 0.5f, 0.5f, 0.9f, 0.3f, 0.5f);
                s.Run(1);
                Mutual(so, b, x, 0.45f);
                if (band) { Mutual(so, b, t, 0.45f); Mutual(so, x, t, 0.45f); }
                so.Regroup(s); s.Run(2);
                so.Grudge[a * so.N + x] = 0.9f;
                so.Known[a * so.N + b] = so.Known[b * so.N + a] = 0.8f;
                for (int k = 0; k < Time.Hours(3); k++) { Calm(m, a, b, x, t); s.Step(); }
                int defend = so.Sayings.Count(q => q.Topic == Topic.Gossip && q.End == TalkEnd.Defend), spread = so.Sayings.Count(q => q.Topic == Topic.Gossip && q.End == TalkEnd.Spread);
                if (Debug) Console.WriteLine($"    {(band ? "무리" : "남")}: 감쌈 {defend} · 퍼짐 {spread} · 대화 {so.Talks} · 같은 무리 {so.SameBand(b, x)}");
                return (defend, spread);
            }
            var mine = Gossip(true); var other = Gossip(false);
            Check("감싸기: 같은 무리 사람의 험담은 (덜 다정한 이도) 막아선다 · 무리가 아니면 소문이 퍼진다",
                mine.defend > 0 && mine.spread == 0 && other.spread > 0 && other.defend == 0,
                $"같은 무리: 감쌈 {mine.defend} · 퍼짐 {mine.spread} / 아님: 감쌈 {other.defend} · 퍼짐 {other.spread}");
        }

        // ── 무리 사이: 서로 원한이 쌓인 두 무리 — '사이가 벌어졌다' · 두 지도자가 만나면 다정한 이들은 화해하고 (원한이 크게 준다) · 거친 이들은 맞선다 (골이 깊어진다) ──
        {
            (bool rift, int peace, int clash, float before, float after, string text) Feud(float agree, float neuro)
            {
                var s = Worlds.Full(lay, 11);
                var m = s.Get<Mind>(); var so = s.Get<Society>();
                int la = m.AddPerson(s, At(13, 2)), lb = m.AddPerson(s, At(16, 3));
                int a2 = m.AddPerson(s, At(3, 2)), a3 = m.AddPerson(s, At(4, 4)), b2 = m.AddPerson(s, At(20, 7)), b3 = m.AddPerson(s, At(6, 7));
                foreach (int p in new[] { la, lb }) m.SetPersonality(p, 0.5f, 0.5f, 0.9f, agree, neuro);
                s.Run(1);
                m[la].C.SkRepair = 1; m[lb].C.SkMedic = 1;                 // 솜씨로 무리를 이끈다
                int[] A = { la, a2, a3 }, B = { lb, b2, b3 };
                foreach (var g in new[] { A, B }) for (int x = 0; x < 3; x++) for (int y = x + 1; y < 3; y++) Mutual(so, g[x], g[y], 0.6f);
                foreach (int x in A) foreach (int y in B) { so.Grudge[x * so.N + y] = 0.4f; so.Grudge[y * so.N + x] = 0.4f; }
                so.Known[la * so.N + lb] = so.Known[lb * so.N + la] = 0.8f;
                so.Regroup(s);
                int g1 = so.BandIdOf(la), g2 = so.BandIdOf(lb);
                bool rift = so.Rival(la, lb) && s.Facts.Since(0).Any(f => f.Kind == FactKind.Relationship && f.Size == -3);
                float before = so.RiftOf(g1, g2);
                for (int k = 0; k < Time.Hours(8) && !so.Sayings.Any(q => q.Topic == Topic.Peace); k++) { Calm(m, la, lb, a2, a3, b2, b3); s.Step(); }
                var pf = s.Facts.Since(0).FirstOrDefault(f => f.Kind == FactKind.Relationship && Math.Abs(f.Size) == 4);
                if (Debug) foreach (var q in so.Sayings.Where(q => q.Topic != Topic.Small)) Console.WriteLine($"      {q.Tick} {m.NameOf(q.Speaker)} → {m.NameOf(q.Listener)} {q.Topic}/{q.End} · 지도자 {la},{lb} · 사이 {so.Rival(la, lb)}");
                if (Debug) Console.WriteLine($"    친화 {agree}: 대화 {so.Talks} · 화해 말 {so.Sayings.Count(q => q.Topic == Topic.Peace)} · 앙금 {before:0.00} → {so.RiftOf(g1, g2):0.00} · 이끄는 이 {so.LeaderOf(a2)}/{so.LeaderOf(b2)}");
                return (rift, s.Facts.Since(0).Count(f => f.Kind == FactKind.Relationship && f.Size == 4), s.Facts.Since(0).Count(f => f.Kind == FactKind.Relationship && f.Size == -4),
                    before, so.RiftOf(g1, g2), pf.Kind == FactKind.Relationship ? s.Causes.Text(pf.Cause) : "");
            }
            var kind = Feud(0.9f, 0.2f); var rough = Feud(0.1f, 0.8f);
            Check("무리 사이: 원한이 쌓인 두 무리는 '사이가 벌어졌다' — 두 지도자가 만나 다정하면 화해하고(원한이 반으로) · 거칠면 맞선다(골이 깊어진다)",
                kind.rift && rough.rift && kind.peace == 1 && kind.after < kind.before * 0.7f && kind.text.Contains("화해했다") && rough.clash == 1 && rough.after > rough.before && rough.text.Contains("맞섰다"),
                $"다정: \"{kind.text}\" · 앙금 {kind.before:0.00} → {kind.after:0.00} / 거침: \"{rough.text}\" · 앙금 {rough.before:0.00} → {rough.after:0.00}");
        }

        // ── 복제 세계 (주컴퓨터가 앞일을 그려 볼 때): 무리가 있고 외침을 들어도 원래 세계와 똑같이 흘러간다 (예전엔 복제 세계에서 멈췄다) ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var so = s.Get<Society>(); var ch = s.Get<Chemistry>();
            int sp = m.AddPerson(s, At(12, 2)), h = m.AddPerson(s, At(13, 7)), t = m.AddPerson(s, At(2, 1));
            s.Run(1);
            m[sp].C.SkFire = 1;
            Mutual(so, sp, h, 0.6f); Mutual(so, sp, t, 0.6f); Mutual(so, h, t, 0.6f);
            so.Regroup(s); s.Run(2);
            ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Story, "의자 밑에서 불씨가 튀었다", s.Tick));
            var w = s.Clone();
            string err = "";
            float heedA = 0, heedB = -1;
            try
            {
                heedA = so.Heed(h, sp); heedB = w.Get<Society>().Heed(h, sp); // 복제한 바로 그 틱 (사회 엔진이 아직 한 번도 돌지 않았다)
                s.Run(Time.Minutes(2)); w.Run(Time.Minutes(2));
            }
            catch (Exception e) { err = e.GetType().Name; }
            Check("복제 세계: 무리 · 외침이 있어도 복제한 세계가 원래와 바이트까지 같게 흘러간다",
                err == "" && heedA == heedB && heedA > 1.2f && s.Hash() == w.Hash() && s.Facts.CountOf(FactKind.Warned) > 0,
                err != "" ? $"멈춤 {err}" : $"믿는 정도 {heedA:0.00} / {heedB:0.00} · 지문 {s.Hash():x} / {w.Hash():x} · 외침 {s.Facts.CountOf(FactKind.Warned)}");
        }
    }
}
