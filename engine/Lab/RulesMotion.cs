using ShipEngine;

namespace Lab;

/// <summary>⑤ 기동 · 다른 배 · 항로 위험 (깊이 바퀴 2 · docs/ENGINE.md ⑤): 로켓 방정식 · 호만 Δv · 점화에 넘어짐 · 운석 비키기 · 미룬 점화 · 놓친 도착 · 조난선 구조 · 난파선 · 위험 구간 · 관성 미끄러짐.</summary>
public static partial class Program
{
    /// <summary>왼쪽 방(엔진 둘 · 배전반 · 배터리 둘 · 주컴퓨터 · 항법 컴퓨터 · 침대) | 오른쪽 방(의자 넷 · 레이더) · 아래 통로(작업대) · 오른쪽 끝 에어락(바깥문 A).</summary>
    private const string HelmShip = @"
###########################
#E.p.bb.cn#...............#
#.........#..a....a....R..#
#E........D...............#
#....e....#..a....a.......#
#####D##########D##########
#..w..................#...#
#.....................D...A
#.....................#...#
###########################";

    private static Dictionary<char, string> HelmLegend => new()
    {
        ['E'] = "EngineCore", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['n'] = "NavComputer", ['e'] = "Bed",
        ['a'] = "Seat", ['R'] = "SensorArray", ['w'] = "Workbench",
    };

    private static void MotionLaws()
    {
        if (!Do("기동")) return;
        Console.WriteLine("\n[⑤ 기동 — 궤도 역학 · 로켓 방정식]");
        // 호만 Δv (활력 방정식): 1 → 1.524AU 2.94 + 2.65km/s (실제 지구 → 화성)
        var (dv1, dv2) = Outside.HohmannDv(1, 1.524);
        Check("호만 전이 Δv (활력 방정식): 1 → 1.524AU 출발 2.94 · 도착 2.65km/s (실제 지구 → 화성 값)", Math.Abs(dv1 - 2945) < 25 && Math.Abs(dv2 - 2649) < 25, $"출발 {dv1:0}m/s · 도착 {dv2:0}m/s");

        // 로켓 방정식 · 낸 Δv · 바깥 물체의 상대 속도
        var lay = Worlds.Ascii(HelmShip, HelmLegend);
        var s = Worlds.Full(lay, 1);
        var o = s.Get<Outside>();
        o.AddRock(1, 100, -1, 0, 0, 1e9, 0);          // 멀리 있는 돌 하나 (상대 속도를 본다)
        double m0 = o.MassKg, vx0 = o.Rocks[0].Vx;
        double want = 400, a = o.MaxAccel(o.Engines);
        o.Burn(s, BurnKind.Correct, o.AxisX, o.AxisY, a, want, 0, 0, false, o.Engines, 0);
        long start = s.Tick; int ticks = 0;
        while (o.Burning != BurnKind.None && ticks < 5000) { s.Step(); ticks++; }
        double m1 = o.MassKg;
        double rocket = 1 - m1 / m0, expect = 1 - Math.Exp(-want / (Tables.Default.Space.Ship.Isp * Outside.G0));
        double dvRock = vx0 - o.Rocks[0].Vx;
        double sec = ticks * Time.SecondsPerTick;
        Check("로켓 방정식: 400m/s를 내면 질량이 1 − e^(−Δv/(Isp·g₀))만큼 준다 · 낸 Δv가 요청과 같다 · 걸린 시간 ≈ Δv/a",
            Math.Abs(rocket / expect - 1) < 0.01 && Math.Abs(o.LastBurnDv - want) < 1e-6 && Math.Abs(sec - want / a) <= 2 * Time.SecondsPerTick,
            $"줄어든 몫 {rocket:0.00000} (식 {expect:0.00000}) · 낸 Δv {o.LastBurnDv:0.000}m/s · {sec:0}초 (Δv/a {want / a:0}초) · 엔진 {o.Engines} · {a / Outside.G0:0.00}g");
        Check("배가 가속하면 바깥 물체의 상대 속도가 그만큼 바뀐다 (배를 가운데 둔 좌표)", Math.Abs(dvRock - want) < 1e-6 * want, $"돌의 상대 속도 {dvRock:0.000}m/s 바뀜");
        Check("주엔진은 엔진 칸 → 배 무게중심 쪽으로 민다 (왼쪽 끝 엔진 → +x)", o.AxisX == 1 && o.AxisY == 0, $"미는 쪽 ({o.AxisX}, {o.AxisY})");
    }

    private static void MotionRules()
    {
        if (!Do("기동")) return;
        Console.WriteLine("\n[⑤ 기동 · 다른 배]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(HelmShip, HelmLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int[] spots = { At(12, 1), At(16, 1), At(12, 3), At(16, 3) };

        (Sim s, Outside o, Mind m, ShipMind sm) Ship(ulong seed, double tons = 600)
        {
            var s = Worlds.Full(lay, seed);
            var o = s.Get<Outside>(); var m = s.Get<Mind>();
            o.DryKg = tons * 500; o.PropellantKg = o.PropellantStart = tons * 500;   // 질량을 맞춰 가속을 정한다
            foreach (int c in spots) { int id = m.AddPerson(s, c); ref var cc = ref m[id].C; cc.ShiftHour = 20; cc.Sleepy = 0; cc.Hunger = 0; cc.Thirst = 0; } // 자정이 근무 한가운데 — 깨어 서 있다
            s.Run(5);
            return (s, o, m, s.Get<ShipMind>());
        }

        // ── 점화에 넘어짐: 정해진 점화(3분 전 방송 · 20초에 걸쳐 올림)에는 아무도 안 넘어지고 · 갑자기 다 켜면 서 있던 사람이 넘어진다 · 1분 전에 '붙잡으라'는 방송을 들었으면 덜 ──
        {
            (long falls, long braced, double g, string story) Run(int how)
            {
                var (s, o, m, sm) = Ship(3);
                int c = s.Causes.Add(CauseKind.Route, "항로가 조금 틀어졌다", s.Tick);
                if (how == 0) { o.AddPlan(new PlannedBurn { Kind = BurnKind.Correct, Due = s.Tick + Time.Minutes(4), Deadline = s.Tick + Time.Hours(3), Dv = 60, Cause = c }); s.Run(Time.Minutes(7)); }
                else
                {
                    if (how == 2) foreach (var a in m.Agents) m.Learn(a, s.Tick, BeliefKind.Burn, -1, (int)s.Tick, 1, BeliefSource.Screen, c);
                    s.Run(Time.Minutes(1));
                    if (Debug) foreach (var a in m.Agents.Where(a => a.IsPerson)) Console.WriteLine($"    {m.NameOf(a.Id)} 목표 {a.C.Goal} 단계 {(a.C.StepIndex < a.Plan.Count ? a.Plan[a.C.StepIndex].Kind : StepKind.None)} 깸 {a.C.Up} 잠 {s.Get<Life>()[a.C.Body].Asleep}");
                    o.Burn(s, BurnKind.Dodge, 1, 0, 10, 60, 0, 0, false, o.Engines, c);
                    s.Run(Time.Minutes(1));
                }
                string story = s.Facts.Since(0).Where(f => f.Kind == FactKind.Fell).Select(f => s.Causes.Story(f.Cause)).FirstOrDefault() ?? "";
                return (m.Falls, m.BracedOnBurn, o.MaxAccel(o.Engines) / Outside.G0, story);
            }
            var planned = Run(0); var sudden = Run(1); var warned = Run(2);
            Check("정해진 점화는 3분 전에 방송하고 20초에 걸쳐 올려 아무도 넘어지지 않는다 · 갑자기 다 켜면 서 있던 사람이 넘어져 다친다 (원인은 점화) · 1분 전에 붙잡으라는 말을 들었으면 덜 넘어진다",
                planned.falls == 0 && sudden.falls >= 2 && sudden.story.Contains("점화에 넘어졌다") && warned.falls < sudden.falls && warned.braced > 0,
                $"{sudden.g:0.00}g · 넘어진 사람: 정해진 점화 {planned.falls} · 갑자기 {sudden.falls} · 미리 듣고 붙잡음 {warned.falls} · \"{sudden.story}\"");
        }

        // ── 운석 비키기: 레이더가 2kg 운석을 잡고(약 10초 앞) 주엔진으로 비켜 맞지 않는다 — 대신 서 있던 사람이 넘어진다 · 레이더가 멈춘 배는 모른 채 맞는다 · 비키지 않는 방침이면 맞는다 ──
        {
            (long hits, long tried, long avoided, long falls, string log) Run(int how)
            {
                var (s, o, m, sm) = Ship(5);
                if (how == 1) { var net = s.Get<Network>(); foreach (var d in net.Devices) if (d.Type == "SensorArray") net.Broken[d.Id * 4 + (int)Part.Electric] = 1; }
                if (how == 2) sm.Policy.Dodge = 0;
                int c = s.Causes.Add(CauseKind.Setup, "2kg 운석", s.Tick);
                o.AddRock(2, 3000, 0, 1, 0, 45_000, c);    // 아래(−y)에서 위로 · 배 한가운데로 · 15초 거리
                s.Run(20);
                return (o.RockHits, sm.DodgesTried, sm.DodgesAvoided, m.Falls, string.Join(" / ", sm.Log.TakeLast(3).Select(x => x.text)));
            }
            var dodge = Run(0); var blind = Run(1); var stay = Run(2);
            Check("레이더가 운석을 잡고 주엔진으로 비켜 맞지 않는다 (서 있던 사람은 넘어진다) · 레이더가 멈춘 배는 모른 채 맞는다 · 비키지 않는 방침이면 맞는다",
                dodge.hits == 0 && dodge.tried == 1 && dodge.avoided == 1 && dodge.falls >= 1 && blind.hits == 1 && blind.tried == 0 && stay.hits == 1,
                $"비킴: 맞음 {dodge.hits} · 비킴 {dodge.tried}/{dodge.avoided} · 넘어짐 {dodge.falls} · \"{dodge.log}\" / 레이더 멈춤: 맞음 {blind.hits} / 방침 끔: 맞음 {stay.hits}");
        }

        // ── 미루기: 점화 시각에 불이 나 있으면 10분씩 미룬다 (원인 마디 '미뤘다') ──
        {
            var (s, o, m, sm) = Ship(7);
            int c = s.Causes.Add(CauseKind.Route, "항로가 조금 틀어졌다", s.Tick);
            s.Get<Chemistry>().Ignite(At(13, 4), 3e6, s.Causes.Add(CauseKind.Setup, "의자 밑 불씨", s.Tick));
            s.Run(Time.Minutes(1));
            o.AddPlan(new PlannedBurn { Kind = BurnKind.Correct, Due = s.Tick + Time.Minutes(1), Deadline = s.Tick + Time.Hours(3), Dv = 30, Cause = c });
            s.Run(Time.Minutes(2));
            bool held = sm.BurnsDelayed >= 1 && o.BurnCount == 0;
            string why = s.Facts.Since(0).Where(f => f.Kind == FactKind.Decision).Select(f => s.Causes.Text(f.Cause)).FirstOrDefault(t => t.Contains("미뤘다")) ?? "";
            Check("점화 시각에 불이 나 있으면 주컴퓨터가 점화를 10분 미룬다 (원인 마디에 까닭)", held && why.Contains("불"), $"미룸 {sm.BurnsDelayed} · 점화 {o.BurnCount} · \"{why}\"");
        }

        // ── 엔진이 멈추면 도착 점화를 못 해 지나쳐 간다 — 한 바퀴 뒤 붙잡기 (Δv 1.5배) ──
        {
            var (s, o, m, sm) = Ship(9);
            var net = s.Get<Network>();
            foreach (var d in net.Devices) if (d.Type == "EngineCore") net.Broken[d.Id * 4 + (int)Part.Mechanical] = 1;
            o.StartVoyage(s, 1.0001); o.VoyagePlanned = 1;
            int c = s.Causes.Add(CauseKind.Route, "도착이 가깝다", s.Tick);
            o.AddPlan(new PlannedBurn { Kind = BurnKind.Arrive, Due = s.Tick + Time.Minutes(4), Deadline = s.Tick + Time.Minutes(12), Dv = 2650, Cause = c });
            s.Run(Time.Minutes(15));
            var cap = o.Plan.FirstOrDefault(p => p.Kind == BurnKind.Capture);
            Check("엔진이 멈춰 있으면 도착 점화를 못 하고 지나쳐 간다 — 전이 궤도를 한 바퀴 돌아 다시 (붙잡기 Δv 1.5배)",
                o.Missed == 1 && cap.Kind == BurnKind.Capture && Math.Abs(cap.Dv - 2650 * 1.5) < 1 && sm.Log.Any(x => x.text.Contains("엔진이 멈춰")),
                $"놓침 {o.Missed} · 붙잡기 {cap.Dv:0}m/s · \"{sm.Log.LastOrDefault(x => x.text.Contains("엔진")).text}\"");
        }

        // ── 조난선: 주컴퓨터가 가기로 하고(추진제 · 공기) 다가가 붙인다 → 생존자 둘이 에어락으로 넘어온다 · 방침이 '가지 않는다'면 그대로 지나친다 ──
        {
            (int people, long rescued, long declined, string story) Run(byte policy)
            {
                var (s, o, m, sm) = Ship(11);
                sm.Policy.AnswerDistress = policy;
                int c = s.Causes.Add(CauseKind.Setup, "고장 난 배", s.Tick);
                o.AddContact(s, ContactKind.Distress, 40_000, 1, 0, 30, 3000, 2, 8, c);
                for (int k = 0; k < 6 && sm.Rescued < 2; k++) s.Run(Time.Hours(1));
                string story = s.Facts.Since(0).Where(f => f.Kind == FactKind.Contact && f.Other == 6).Select(f => s.Causes.Story(f.Cause)).FirstOrDefault() ?? "";
                if (Debug) Console.WriteLine("    " + string.Join("\n    ", sm.Log.TakeLast(12).Select(x => $"{Time.Clock(x.tick)} {x.text}")));
                return (m.Agents.Count(a => a.IsPerson), sm.Rescued, sm.Declined, story);
            }
            var go = Run(1); var no = Run(0);
            Check("조난 신호: 주컴퓨터가 가기로 하고 맞추는 점화 → 다가감 → 멈추는 점화 → 붙인다 → 생존자 둘이 에어락으로 넘어온다 · 방침이 '가지 않는다'면 지나친다",
                go.people == 6 && go.rescued == 2 && go.story.Contains("넘어왔다") && no.people == 4 && no.declined == 1,
                $"구함: 사람 {go.people} · 넘어옴 {go.rescued} · \"{go.story}\" / 안 감: 사람 {no.people} · 거절 {no.declined}");
        }

        // ── 공기 없는 에어락: 붙었어도 에어락이 진공이면 생존자는 그 배에서 기다린다 · 공기가 돌아오면 넘어와 산다 (고치기 전: 진공 에어락으로 넘어와 그대로 숨졌다) ──
        {
            var (s, o, m, sm) = Ship(11);
            sm.Policy.AnswerDistress = 1;
            int c = s.Causes.Add(CauseKind.Setup, "고장 난 배", s.Tick);
            o.AddContact(s, ContactKind.Distress, 40_000, 1, 0, 30, 3000, 2, 8, c);
            var f = s.Get<Field>(); var life = s.Get<Life>();
            var lockRoom = s.Layout.Rooms[s.Layout.Room[At(24, 7)]].Cells;
            long waitedWhileVacuum = 0; long rescuedInVacuum = 0;
            for (int k = 0; k < Time.Hours(6) && sm.Rescued < 2; k++)
            {
                bool docked = o.Contacts.Any(x => x.State == ContactState.Docked);
                if (docked && waitedWhileVacuum < Time.Minutes(40))
                {
                    foreach (int x in lockRoom) for (int g = 0; g < f.G; g++) f.RemoveGas(x, g, f.N[x * f.G + g] * 0.5);   // 에어락이 진공이다 (바깥문이 끼었다)
                    waitedWhileVacuum++;
                    rescuedInVacuum = sm.Rescued;
                }
                s.Step();
            }
            int dead = m.Agents.Count(a => a.IsPerson && life.Bodies[a.C.Body].Alive == 0);
            Check("공기 없는 에어락: 붙었어도 에어락이 진공이면 생존자는 그 배에서 기다리고 · 공기가 돌아오면 넘어와 산다",
                waitedWhileVacuum >= Time.Minutes(40) && rescuedInVacuum == 0 && sm.Rescued == 2 && dead == 0,
                $"진공 동안 넘어옴 {rescuedInVacuum} ({waitedWhileVacuum * 60.0 / Time.TicksPerHour:0}분) · 끝에 넘어옴 {sm.Rescued} · 죽음 {dead} · \"{sm.Log.LastOrDefault(x => x.text.Contains("에어락에 공기가 없다")).text}\"");
        }

        // ── 난파선(불이 났던 배): 붙어서 해치를 열면 매연(일산화탄소)이 에어락으로 들고 · 수리 꾸러미를 건진다 ──
        {
            var (s, o, m, sm) = Ship(13);
            int c = s.Causes.Add(CauseKind.Setup, "불탄 배", s.Tick);
            o.AddContact(s, ContactKind.Derelict, 3000, 1, 0, 2, 200, 0, 0, c, fire: 1);
            var f = s.Get<Field>();
            int lockCell = At(24, 7);
            double coMax = 0;
            for (int k = 0; k < 24 && sm.Salvaged == 0; k++) s.Run(Time.Minutes(10));
            for (int k = 0; k < 10; k++) { s.Step(); coMax = Math.Max(coMax, f.Ppm(f.T.gCO, lockCell)); }
            Check("난파선에 붙어 해치를 열면 그 배의 매연(일산화탄소)이 에어락으로 들고 · 수리 꾸러미를 건져 작업대에 둔다",
                sm.Salvaged >= 2 && coMax > 300,
                $"건짐 {sm.Salvaged} · 에어락 일산화탄소 {coMax:0}ppm · \"{sm.Log.LastOrDefault().text}\"");
        }

        // ── 교통량의 조난선: 가만히 떠 있는 배(시험 — 지나는 배 없음)에는 오지 않고 · 항해 중 지나는 조난선의 신호를 받으면 주컴퓨터가 갈지 정한다 (교통 → 신호 → 결정) ──
        {
            var (s, o, m, sm) = Ship(17);
            s.Run(Time.Hours(2));
            bool stayNo = o.ContactSeq == 0;
            o.TrafficOn = true;
            int c0 = s.Causes.Add(CauseKind.Contact, "3명이 탄 배가 고장 나 떠돈다", s.Tick);
            o.AddContact(s, ContactKind.Distress, 400 * 8 * 3600, 1, 0, 400, 5000, 3, 20, c0);   // 교통량이 놓는 것과 같은 모양 (8시간 거리 · 신호기)
            s.Run(3);
            string story = s.Facts.Since(0).Where(f => f.Kind == FactKind.Decision).Select(f => s.Causes.Story(f.Cause)).FirstOrDefault(t => t.Contains("조난")) ?? "";
            Check("교통량의 조난선: 가만히 떠 있는 배에는 지나는 배가 없고 · 항해 중 지나는 조난선의 신호를 받으면 주컴퓨터가 갈지 정한다 (교통 → 신호 → 결정)",
                stayNo && o.Contacts.Count == 1 && story.Contains("고장 나 떠돈다") && (sm.GoingTo >= 0 || sm.Declined == 1),
                $"떠 있는 배 두 시간 지나는 배 {(stayNo ? "없음" : "있음")} · \"{story}\"");
        }

        // ── 관성: 점화 0.2g에 마른 바닥의 상자(마찰 0.3)는 그대로 · 젖은 바닥(0.1)이면 뒤로 미끄러진다 · 0.4g면 마른 바닥에서도 ──
        {
            (int moved, string story) Run(double g, bool wet)
            {
                var s = Worlds.Full(lay, 15);
                var o = s.Get<Outside>(); var m = s.Get<Mind>(); var f = s.Get<Field>();
                o.DryKg = o.PropellantKg = o.Engines * o.EngineN / (g * Outside.G0) / 2;
                byte box = (byte)m.ItemIndex("식량 상자");
                int start = At(12, 7);
                Worlds.PutItem(s, box, start);
                if (wet) f.AddStore(start, f.T.sWater, 300);
                s.Run(3);
                int c = s.Causes.Add(CauseKind.Route, "항로 고침", s.Tick);
                o.Burn(s, BurnKind.Correct, 1, 0, 100, 0, 30, 0, false, o.Engines, c);
                s.Run(15);
                var it = Worlds.Items(s).FirstOrDefault(x => x.Item == box, new Worlds.FloorItem(0, 0, -1, 0, 0));
                return (it.Cell < 0 ? 99 : lay.X(start) - lay.X(it.Cell), "");
            }
            var dry2 = Run(0.2, false); var wet2 = Run(0.2, true); var dry4 = Run(0.4, false);
            Check("점화의 관성: 0.2g에 마른 바닥 상자(마찰 0.3)는 그대로 · 젖은 바닥(0.1)이면 엔진 반대쪽으로 미끄러진다 · 0.4g면 마른 바닥에서도",
                dry2.moved == 0 && wet2.moved > 0 && dry4.moved > 0, $"0.2g 마름 {dry2.moved}칸 · 0.2g 젖음 {wet2.moved}칸 · 0.4g 마름 {dry4.moved}칸 (뒤로)");
        }

        // ── 항로의 위험 구간: 작은 바위 띠(1.12 ~ 1.30AU)를 지나는 이틀 동안 운석이 온다 · 띠 밖(1AU)에서는 안 온다 ──
        {
            (int rocks, int clouds) Run(double au)
            {
                var small = Worlds.Ascii(Worlds.Symmetric);
                var s = new Sim(new SimSetup(small, 3, sim => { var f = new Field(sim.Layout, sim.Tables); sim.Add(f); sim.Add(new Outside(f, sim.Seed)); }));
                s.Get<Field>().FillAir();
                var o = s.Get<Outside>(); o.Distance = au;
                int clouds = 0;
                for (int k = 0; k < Time.Days(2); k++) { int n0 = o.Clouds.Count; s.Step(); if (o.Clouds.Count > n0) clouds++; }
                return (o.RockSeq, clouds);
            }
            var inBelt = Run(1.2); var outBelt = Run(1.0);
            Check("항로의 위험 구간: 작은 바위 띠(1.12 ~ 1.30AU)를 지나는 이틀 동안 바위가 온다 (하루 평균 3) · 띠 밖(1AU)에서는 오지 않는다",
                inBelt.rocks >= 2 && outBelt.rocks == 0 && outBelt.clouds == 0, $"띠 안: 바위 {inBelt.rocks} · 구름 {inBelt.clouds} / 띠 밖: 바위 {outBelt.rocks} · 구름 {outBelt.clouds}");
        }

        // ── 운석 줄: 맞은 틱의 '맞음' 보기 기록 (찍지 않는다 · 틱 안의 때 Sub) — 화면은 틱 사이 그 때를 지나는 장부터 들어온 쪽 화면 끝에서 맞은 칸까지 줄 · 잔상 0.25초 (화면 시간) · 그 운석에 난 부스러기도 그 때부터 · 구름 맞음마다 짧은 줄 ──
        {
            var dl = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            // 줄 하나만 둔 그림 − 줄 없는 그림: 다른 도트 (같은 화가 · 같은 세계)
            static List<(int x, int y)> Diff(ShipView.Painter p, Sim s, int x0, int y0, int w, int h, Action strip)
            {
                var keep = p.Strikes;
                var with = p.Frame(s, x0, y0, w, h, 0);
                strip();
                var bare = p.Frame(s, x0, y0, w, h, 0);
                p.Strikes = keep;
                var d = new List<(int, int)>();
                for (int y = 0; y < with.H; y++)
                    for (int x = 0; x < with.W; x++)
                    {
                        int i = (y * with.W + x) * 4;
                        if (with.Px[i] != bare.Px[i] || with.Px[i + 1] != bare.Px[i + 1] || with.Px[i + 2] != bare.Px[i + 2]) d.Add((x, y));
                    }
                return d;
            }
            var s = Worlds.Full(dl, 2);
            var o = s.Get<Outside>(); var st = s.Get<Structure>();
            s.Run(3);
            var show = new ShipView.LiveShow(s, "운석") { SpeedOverride = 1, Follow = false };
            o.AddRock(20.0, 6000, 1, 0, 0, 6000 * 1.2, 0);   // 왼쪽에서 · 틱 가운데에 맞게 (20kg — 운석 지름의 구멍 (DF9)이라 1kg은 외판에 14cm 구멍만 내 부스러기가 없다 · 충격파가 벽을 뜯는 무게)
            int ran = show.Advance(1.0);
            int struck = o.Struck.Count;
            double sub = struck > 0 ? o.Struck[0].Sub / 255.0 : double.NaN;
            int wx0 = -14, wy0 = 0, ww = 20, wh = dl.H;
            var p = show.Painter;
            List<(int x, int y)> Line() => Diff(p, s, wx0, wy0, ww, wh, () => p.Strikes = new());
            List<(int x, int y)> Bits()   // 부스러기 하나도 없는 그림과 (한 틱에 수십 m — 넓게)
            {
                var all = st.Shards.ToList(); var props = st.Props.ToList();   // 나는 판 조각 (PH15)도 부스러기로 그린다
                var d = Diff(p, s, -70, -40, 80, 90, () => { st.Shards.Clear(); st.Props.RemoveAll(q => q.Kind == 4 && Structure.Flying(q)); });
                st.Shards.Clear(); st.Shards.AddRange(all); st.Props.Clear(); st.Props.AddRange(props);
                return d;
            }
            bool ok = !double.IsNaN(sub);
            ran += show.Advance(ok ? sub - 0.2 : 0.3); show.Render();
            var early = Line(); var earlyBits = Bits();
            ran += show.Advance(0.21); show.Render();
            var hitLine = Line(); var hitBits = Bits();
            ran += show.Advance(0.25); show.Render();
            var after = Line();
            int cell = struck > 0 ? o.Struck[0].Cell : -1;
            var (hx, hy) = cell >= 0 ? st.ShipFrameOf(cell) : (0.0, 0.0);
            int inSide = hitLine.Count(q => (wx0 + (q.x + 0.5) / ShipView.Painter.T) < hx);   // 들어온 쪽 (왼쪽)
            // 파편 구름 속: 맞은 파편마다 그 칸에서 들어온 쪽으로 짧은 줄
            var s2 = Worlds.Full(dl, 2);
            var o2 = s2.Get<Outside>(); var st2 = s2.Get<Structure>();
            s2.Run(3);
            o2.AddCloud("rubble", 0, 100_000, 4e-5, 1, 0, 0);   // 배가 구름 속 (한 틱에 맞음 6쯤)
            long h0 = o2.DebrisHits;
            s2.Step();
            var p2 = new ShipView.Painter(dl);
            var cl = Diff(p2, s2, -4, 0, dl.W + 4, dl.H, () => p2.Strikes = new());
            int lined = 0;
            foreach (var h in o2.Struck)
            {
                var (cx, cy) = st2.ShipFrameOf(h.Cell);
                double bx = cx - h.Ux * 1.0, by = cy - h.Uy * 1.0;   // 맞은 칸 가운데에서 들어온 쪽으로 1m
                if (cl.Count(q => Math.Abs(-4 + (q.x + 0.5) / ShipView.Painter.T - bx) < 0.6 && Math.Abs((q.y + 0.5) / ShipView.Painter.T - by) < 0.6) >= 3) lined++;
            }
            Check("운석 줄: 맞은 틱에 '맞음' 하나 · 틱 안의 때 0.5 ± 0.1 · 그 때를 지나는 장부터 들어온 쪽 화면 끝에서 맞은 칸까지 줄 (20도트 넘게) · 0.2 앞 장에는 없다 · 잔상 0.25초 뒤 없다 · 그 운석에 난 부스러기도 그 때부터 · 파편 구름 속에서는 맞은 파편마다 짧은 줄",
                ran == 1 && struck == 1 && Math.Abs(sub - 0.5) <= 0.1 && early.Count == 0 && inSide >= 20 && after.Count == 0 &&
                st.FlyingDebris().Count > 0 && earlyBits.Count == 0 && hitBits.Count > 0 &&
                o2.Struck.Count == o2.DebrisHits - h0 && o2.Struck.Count >= 2 && lined == o2.Struck.Count,
                $"맞음 {struck} · 때 {sub:0.000} · 줄 도트 (때 − 0.2 · + 0.01 들어온 쪽 · 0.25초 뒤) {early.Count} · {inSide}/{hitLine.Count} · {after.Count} · " +
                $"부스러기 {st.FlyingDebris().Count} (때 − 0.2 · + 0.01 그린 도트 {earlyBits.Count} · {hitBits.Count}) · 구름 맞음 {o2.Struck.Count}/{o2.DebrisHits - h0} · 줄 있는 {lined}");
        }
    }
}
