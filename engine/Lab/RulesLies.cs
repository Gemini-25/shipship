using ShipEngine;

namespace Lab;

/// <summary>⑧ · ⑨ 바퀴 3 — 거짓말과 속임: 숨긴 일은 남이 모른다 · 남 탓 · 반쯤 믿는다 · 본 이 · 주컴퓨터 정비 기록 (docs/ENGINE.md ⑨ 바퀴 3).</summary>
public static partial class Program
{
    private static void LieRules()
    {
        if (!Do("거짓말")) return;
        Console.WriteLine("\n[⑧ · ⑨ 거짓말과 속임]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // 디딤호 한 척: x가 장치를 손보다 상하게 하고 (곁에 본 이가 있거나 없거나) · 그 전에 sc가 그 장치를 고쳤다 · 망가지는 순간 곁에 듣는 이들
        (Sim s, Mind m, Society so, int x, int sc, int dev, int c0, List<int> ears) Scene(ulong seed, int witnessIdx, bool scapeNear)
        {
            var s = ShipWorld.Crewed("Didim", seed, 6, 2);
            s.Run(10);
            var m = s.Get<Mind>(); var so = s.Get<Society>(); var net = s.Get<Network>();
            var people = m.Agents.Where(a => a.IsPerson).Select(a => a.Id).ToList();
            int x = people[0], sc = people[1], witness = witnessIdx >= 0 ? people[witnessIdx] : -1;
            var d = net.Devices.First(v => v.Room >= 0 && net.On[v.Id] != 0 && Enum.GetValues<Part>().Any(p => v.Life(p) > 0 && !net.IsBroken(v.Id, p)));
            var here = s.Layout.Rooms[d.Room].Cells.Where(c => s.Layout.Kind[c] == CellKind.Floor).ToList();
            var away = s.Layout.Rooms.Where(r => r.Id != d.Room && r.Cells.Length > 8).OrderByDescending(r => m.Dist(r.Cells[0], d.Cell)).First().Cells.Where(c => s.Layout.Kind[c] == CellKind.Floor).ToList();
            var life = s.Get<Life>();
            void Put(int a, int cell) { m[a].C.Cell = cell; life[m[a].C.Body].Cell = cell; m[a].Path.Clear(); m[a].PathIdx = 0; }   // 몸의 칸이 자리다
            void Wake() { foreach (int p in people) { m[p].C.Sleepy = 0; life[m[p].C.Body].Asleep = 0; } }   // 모두 깨어 있다 (자는 이는 보지도 듣지도 못한다)
            m.SetPersonality(x, 0.4f, 0.05f, 0.6f, 0.1f, 0.8f);   // 덜 성실 · 덜 다정 · 불안 — 털어놓지 않고 둘러댄다
            // sc가 조금 전에 그 장치를 고쳤다 (배 사람들이 안다 · 주컴퓨터 기록에도)
            s.Facts.Emit(s.Tick, FactKind.Repaired, d.Cell, 0, 0, sc, d.Id);
            s.Run(Time.Minutes(10));
            // x가 손보다 상하게 했다 — 그 방에는 x와 (있다면) 본 이만
            for (int i = 0; i < people.Count; i++) Put(people[i], away[(i * 3) % away.Count]);
            Wake();
            Put(x, here[0]);
            if (witness >= 0) Put(witness, here[Math.Min(1, here.Count - 1)]);
            int c0 = s.Causes.Add(CauseKind.Person, $"{Ko.I(m.NameOf(x))} {Ko.Eul(d.Name)} 손보다 부품을 상하게 했다 — 아무에게도 말하지 않았다", s.Tick, d.Cell, 0, 0, x);
            s.Facts.Emit(s.Tick, FactKind.Mistake, d.Cell, Network.HandledSize, c0, x, d.Id);
            s.Step();
            s.Run(Time.Minutes(5));
            // 망가지는 순간: x 곁에 듣는 이들 (sc는 곁에 있거나 멀리)
            var ears = people.Where(p => p != x && p != sc).ToList();
            for (int i = 0; i < people.Count; i++) Put(people[i], away[(i * 3) % away.Count]);
            Wake();
            Put(x, here[0]);
            foreach (int e in ears) Put(e, here[(1 + ears.IndexOf(e)) % here.Count]);
            if (scapeNear) Put(sc, here[(1 + ears.Count) % here.Count]);
            return (s, m, so, x, sc, d.Id, c0, ears);
        }
        string life0(Sim s, Mind m, int a) { var b = s.Get<Life>().Bodies[m[a].C.Body]; return $"산 {b.Alive} 의식 {b.Conscious} 잠 {b.Asleep} 일어섬 {m[a].C.Up}"; }
        void Fail(Sim s, int dev, int c0)
        {
            int c1 = s.Causes.Add(CauseKind.Wear, "부품이 깨졌다", s.Tick, s.Get<Network>().Devices[dev].Cell, c0);
            s.Facts.Emit(s.Tick, FactKind.MachineFail, s.Get<Network>().Devices[dev].Cell, 0, c1, -1, dev);
            s.Step();
        }

        // ── 숨긴 일은 남이 모른다: 숨긴 실수로 망가진 장치에 다친 이는 숨긴 이를 탓하지 못한다 (원인 사슬에 있어도 모른다) ──
        {
            var (s, m, so, x, sc, dev, c0, ears) = Scene(3, -1, false);
            int v = ears[0];
            int c1 = s.Causes.Add(CauseKind.Wear, "부품이 깨졌다", s.Tick, s.Get<Network>().Devices[dev].Cell, c0);
            int c2 = s.Causes.Add(CauseKind.Biology, "튄 조각에 다쳤다", s.Tick, m[v].C.Cell, c1);
            s.Facts.Emit(s.Tick, FactKind.Hurt, m[v].C.Cell, 0.3f, c2, m[v].C.Body);
            s.Step();
            float g = so.GrudgeOf(v, x);
            bool blamed = s.Facts.Since(0).Any(f => f.Kind == FactKind.Blamed && f.Other == x);
            Check("숨긴 일은 남이 모른다: 숨긴 실수로 망가진 장치에 다친 이는 숨긴 이를 탓하지 못한다 (원인 사슬에 있어도 — 아는 것만 탓한다)",
                g < 0.01f && !blamed, $"다친 이의 원망 {g:0.00} · 탓 사실 {blamed}");
        }

        // ── 남 탓 · 반쯤 믿는다: 덜 정직한 이는 그 장치를 고친 이에게 떠넘기고 · 듣는 이는 반쯤 믿는다 (속임 알아채기 47% — Bond & DePaulo 2006) ──
        {
            int lies = 0, toScape = 0, believed = 0, doubted = 0, blameX = 0; string text = "";
            var runs = Enumerable.Range(0, 12).Select(k => Task.Run(() =>
            {
                var (s, m, so, x, sc, dev, c0, ears) = Scene((ulong)(20 + k), -1, false);
                long f0 = s.Facts.Total;
                if (Debug && k == 0) Console.WriteLine($"    x 방 {m.RoomOf(m[x].C.Cell)} {life0(s, m, x)} · 듣는 이: " + string.Join(" ", ears.Select(e => $"{m.RoomOf(m[e].C.Cell)}:{life0(s, m, e)}")));
                Fail(s, dev, c0);
                if (Debug && k == 0) Console.WriteLine($"    뒤: x 방 {m.RoomOf(m[x].C.Cell)} · 듣는 이: " + string.Join(" ", ears.Select(e => $"{m.RoomOf(m[e].C.Cell)}:{life0(s, m, e)}")) + $" · 들은 이 {so.Heard.Count}");
                var lie = s.Facts.Since(f0).FirstOrDefault(f => f.Kind == FactKind.Lie);
                int b = so.Heard.Count(h => h.Believed != 0), d = so.Heard.Count(h => h.Believed == 0);
                int bx = ears.Count(e => so.GrudgeOf(e, x) > 0.05f);
                if (Debug && lie.Kind != FactKind.Lie) Console.WriteLine($"    시드 {20 + k}: 둘러대지 않음 — 숨긴 실수 {so.Hidden.Count} · 털어놓음 {so.Confessions} · 숨김 {so.SecretsKept} · x 깨어 있음 {life0(s, m, x)}");
                return (lie.Kind == FactKind.Lie, lie.Other == sc, b, d, bx, lie.Kind == FactKind.Lie ? s.Causes.Text(lie.Cause) : "");
            })).ToList();
            foreach (var t in runs)
            {
                var r = t.Result;
                if (r.Item1) lies++; if (r.Item2) toScape++; believed += r.b; doubted += r.d; blameX += r.bx;
                if (text == "" && r.Item6 != "") text = r.Item6;
            }
            double share = believed + doubted > 0 ? believed / (double)(believed + doubted) : 0;
            Check("남 탓 · 반쯤 믿는다: 덜 정직한 이(성실 0.05 · 다정 0.1)는 털어놓지 않고 그 장치를 고친 이에게 떠넘긴다 · 듣는 이는 반쯤 믿는다 (믿으면 탓 듣는 이를 원망)",
                lies == 12 && toScape == 12 && share >= 0.3 && share <= 0.75 && blameX == 0,
                $"\"{text}\" · 둘러댐 {lies}/12 (고친 이에게 {toScape}) · 믿음 {believed} · 의심 {doubted} ({share:P0}) · 둘러댄 이를 원망 {blameX}");
        }

        // ── 본 이: 그때 곁에서 그 사람이 손보는 것을 본 이가 그 말을 들으면 들통난다 — 믿음이 크게 무너지고 · 누명 쓴 이는 깊게 원망한다 ──
        {
            var (sw, mw, sow, xw, scw, devw, c0w, earsw) = Scene(5, 2, false);
            int w = mw.Agents.Where(a => a.IsPerson).Select(a => a.Id).ToList()[2];
            float before = earsw.Where(e => e != w).Average(e => sow.TrustIn(e, xw));
            long f0 = sw.Facts.Total;
            Fail(sw, devw, c0w);
            var ex = sw.Facts.Since(f0).FirstOrDefault(f => f.Kind == FactKind.LieExposed);
            float after = earsw.Where(e => e != w).Average(e => sow.TrustIn(e, xw));
            float scG = sow.GrudgeOf(scw, xw);
            if (Debug) Console.WriteLine($"    {(ex.Kind != FactKind.None ? sw.Causes.Story(ex.Cause) : "들통 없음")}");
            Check("본 이: 그때 곁에서 손보는 것을 본 이가 그 말을 들으면 들통난다 — 들은 이의 믿음이 크게 무너지고 · 누명 쓴 이는 깊게 원망한다",
                ex.Kind == FactKind.LieExposed && ex.Size == 1 && ex.Other == w && before - after >= 0.2f && scG >= 0.4f,
                $"\"{(ex.Kind != FactKind.None ? sw.Causes.Text(ex.Cause) : "들통 없음")}\" · 믿음 {before:0.00} → {after:0.00} · 누명 쓴 이의 원망 {scG:0.00}");
        }

        // ── 주컴퓨터 기록: 누명 쓴 이가 정비 기록을 묻는다 → 망가지기 전에 연 이로 둘러댄 이가 나온다 → 들통 / 그때 주컴퓨터가 적지 못했으면 드러나지 않는다 ──
        {
            (bool exposed, long asked, string text) Run(bool logged)
            {
                var (s, m, so, x, sc, dev, c0, ears) = Scene(7, -1, true);
                m.SetPersonality(sc, 1f, 1f, 0.5f, 0.5f, 0.3f);   // 꼼꼼하고 궁금한 이 — 틀림없이 기록을 묻는다
                var sm = s.Get<ShipMind>();
                if (!logged) sm.Accesses.RemoveAll(a => a.Agent == x);   // 주컴퓨터가 멈춰 있어 적지 못했다
                long f0 = s.Facts.Total;
                Fail(s, dev, c0);
                for (int k = 0; k < Time.Hours(3) && !so.Lies.Any(l => l.Exposed != 0); k++) s.Step();
                var ex = s.Facts.Since(f0).FirstOrDefault(f => f.Kind == FactKind.LieExposed);
                return (ex.Kind == FactKind.LieExposed && ex.Size == 2, so.LogChecks, ex.Kind != FactKind.None ? s.Causes.Story(ex.Cause) : "");
            }
            var offT = Task.Run(() => Run(false)); var on = Run(true); var off = offT.Result;
            Check("주컴퓨터 기록: 누명 쓴 이가 정비 기록을 묻고 · 망가지기 전에 연 이로 둘러댄 이가 나오면 들통난다 / 그때 주컴퓨터가 적지 못했으면 드러나지 않는다",
                on.exposed && on.asked >= 1 && !off.exposed && off.asked >= 1,
                $"기록 있음: 물음 {on.asked} · \"{Trim(on.text, 150)}\" / 기록 없음: 물음 {off.asked} · 들통 {off.exposed}");
        }

        // ── 판단 이관 (라): 둘러대기 · 들은 말 · 기록 묻기는 사람의 행동 — 둘러댄 사실마다 같은 틱에 그 사람의 '둘러댐' · 기록을 물은 사실마다 '묻는다' 행동 /
        //    기록을 물으려던 이가 잠들면 깬 뒤에 묻는다 (옛 사회 엔진은 잠든 이의 물음을 잊었다) ──
        {
            (int lies, int lieActs, int asks, int askActs, int heard, int hearActs, bool exposed, long asked) Run(bool sleep)
            {
                var (s, m, so, x, sc, dev, c0, ears) = Scene(7, -1, true);
                m.SetPersonality(sc, 1f, 1f, 0.5f, 0.5f, 0.3f);   // 꼼꼼하고 궁금한 이 — 틀림없이 기록을 묻는다
                var life = s.Get<Life>();
                var acts = new List<SocialAct>();
                long cur = m.ActsTotal, f0 = s.Facts.Total;
                void Step(bool asleep)
                {
                    s.Step();
                    acts.AddRange(m.ActsSince(cur)); cur = m.ActsTotal;
                    if (asleep) { m[sc].C.Sleepy = 1; life[m[sc].C.Body].Asleep = 1; }
                }
                int c1 = s.Causes.Add(CauseKind.Wear, "부품이 깨졌다", s.Tick, s.Get<Network>().Devices[dev].Cell, c0);
                s.Facts.Emit(s.Tick, FactKind.MachineFail, s.Get<Network>().Devices[dev].Cell, 0, c1, -1, dev);
                Step(false);
                if (sleep) { for (int k = 0; k < Time.Hours(4); k++) Step(true); m[sc].C.Sleepy = 0; life[m[sc].C.Body].Asleep = 0; }   // 물을 때가 지나도록 자고 · 깬다
                for (int k = 0; k < Time.Hours(4) && !so.Lies.Any(l => l.Exposed != 0); k++) Step(false);
                var facts = s.Facts.Since(f0).ToList();
                var lieF = facts.Where(f => f.Kind == FactKind.Lie).ToList();
                var askF = facts.Where(f => f.Kind == FactKind.LogAsked).ToList();
                int lieActs = lieF.Count(f => acts.Any(a => a.Kind == SocialActKind.TellLie && a.Tick == f.Tick && a.Actor == f.Actor && a.Cause == f.Cause));
                int askActs = askF.Count(f => acts.Any(a => a.Kind == SocialActKind.AskLog && a.Tick == f.Tick && a.Actor == f.Actor && a.Cause == f.Cause));
                return (lieF.Count, lieActs, askF.Count, askActs, so.Heard.Count, acts.Count(a => a.Kind == SocialActKind.HearLie), so.Lies.Any(l => l.Exposed != 0), so.LogChecks);
            }
            var awake = Task.Run(() => Run(false)); var slept = Run(true); var aw = awake.Result;
            Check("판단 이관 (라): 둘러댄 사실마다 같은 틱에 그 사람의 '둘러댐' 행동 · 들은 이마다 '들은 거짓말' 행동 · 기록을 물은 사실마다 '묻는다' 행동 / 물으려던 이가 잠들면 깬 뒤에 묻는다",
                aw.lies == 1 && aw.lieActs == 1 && aw.asks >= 1 && aw.askActs == aw.asks && aw.heard == aw.hearActs && aw.exposed
                && slept.lies == 1 && slept.asked >= 1 && slept.exposed,
                $"깨어 있음: 둘러댐 {aw.lies} (행동 {aw.lieActs}) · 들음 {aw.heard} (행동 {aw.hearActs}) · 물음 {aw.asks} (행동 {aw.askActs}) · 들통 {aw.exposed} / 잠든 뒤: 물음 {slept.asked} · 들통 {slept.exposed}");
        }
    }
}
