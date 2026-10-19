using ShipEngine;

namespace Lab;

/// <summary>⑨ 문화 · 관습 (깊이 바퀴 3 — docs/ENGINE.md ⑨): 되풀이되는 일이 배의 관습 · 금기 · 별명이 되고 · 추모 · 축하로 모이고 · 마찰이 배의 규칙이 된다.</summary>
public static partial class Program
{
    private const string CultureShip = @"
##############################
#..e.e.e.#.........#.........#
#........D....m....D..a...a..#
#..e.e.e.#.a.a.a.a.#.........#
##############################";

    private static void CultureRules()
    {
        if (!Do("문화")) return;
        Console.WriteLine("\n[⑨ 문화 · 관습]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = new Dictionary<char, string> { ['m'] = "MealDispenser", ['a'] = "Seat", ['e'] = "Bed" };
        var lay = Worlds.Ascii(CultureShip, legend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int beds = lay.Room[At(2, 2)], mess = lay.Room[At(14, 2)], lounge = lay.Room[At(24, 2)];
        lay.Rooms[beds].Name = "침실"; lay.Rooms[mess].Name = "식당"; lay.Rooms[lounge].Name = "휴게실";
        lay.Rooms[mess].Type = "Lounge"; lay.Rooms[lounge].Type = "Lounge";   // 식당 겸 쉼터 · 휴게실 — 둘 다 쉴 의자가 있다

        (Sim s, Mind m, Society soc, List<int> p) World(int n, ulong seed, bool withBeds = false)
        {
            var s = Worlds.Full(lay, seed);
            var m = s.Get<Mind>(); var soc = s.Get<Society>();
            var p = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int cell = At(12 + (i % 4) * 2, 1 + (i / 4) * 2);
                int a = withBeds ? m.AddPerson(s, cell, bed: At(3 + (i % 3) * 2, i < 3 ? 1 : 3)) : m.AddPerson(s, cell);
                m.SetPersonality(a, 0.5f, 0.6f, 0.6f, 0.5f, 0.4f); p.Add(a);
            }
            s.Run(2);
            return (s, m, soc, p);
        }
        void Keep(Sim s, Mind m, List<int> p, bool hunger = true)
        {
            foreach (int a in p) { if (s.Get<Life>()[m[a].C.Body].Alive == 0) continue; ref var c = ref m[a].C; c.Sleepy = 0; c.Thirst = 0; c.Bored = 0; c.Lonely = 0; if (hunger) c.Hunger = 0; s.Get<Life>()[c.Body].Asleep = 0; }
        }
        void Run(Sim s, Mind m, List<int> p, double minutes, bool hunger = true) { for (int k = 0; k < Time.Minutes(minutes); k++) { s.Step(); Keep(s, m, p, hunger); } }
        List<string> Lines(Sim s, Society soc, double minutes) => soc.Sayings.Where(y => y.Tick >= s.Tick - Time.Minutes(minutes)).Select(y => s.Causes.Text(y.Line)).ToList();
        Fact LastCulture(Sim s, long f0, CultureEvent e) => s.Facts.Since(f0).LastOrDefault(f => f.Kind == FactKind.Culture && (int)f.Size == (int)e);

        // ── 모이는 곳은 식당: 디딤호는 주방에 냉장고가 먼저 있어도 회의 · 추모는 식당(배식기 · 식탁)에서 연다
        //    (주방을 고르던 결함 — 첫날 불이 난 주방을 위험하다고 믿는 이들이 추모에 오지 않았다) ──
        {
            var s = ShipWorld.Crewed("Didim", 1, 2, 0);
            var m = s.Get<Mind>();
            int r = m.MessRoom();
            Check("모이는 곳은 식당: 디딤호는 주방에 냉장고가 먼저 있어도 회의 · 추모 · 함께 먹는 때는 식당(배식기 · 식탁)으로 친다",
                r >= 0 && s.Layout.Rooms[r].Type == "Mess", $"고른 방 {(r >= 0 ? s.Layout.Rooms[r].Name + " (" + s.Layout.Rooms[r].Type + ")" : "-")}");
        }

        // ── 함께 먹는 때가 관습이 된다: 지난 이틀 같은 시각에 셋 넘게 함께 먹었고 · 오늘도 그 시각에 넷이 식당에서 함께 먹으면 —
        //    그 시각이 배의 관습이 된다 ('밤 12시엔 함께 먹는다 — 이 배의 관습이 되었다') · ⑧이 그 시각을 안다 ──
        {
            var (s, m, soc, p) = World(4, 3);
            soc.SeedMealDays(0, 0, 2);
            long f0 = s.Facts.Total;
            foreach (int a in p) m[a].C.Hunger = 0.75f;
            for (int k = 0; k < Time.Minutes(62); k++) { s.Step(); foreach (int a in p) { ref var c = ref m[a].C; c.Sleepy = 0; c.Thirst = 0; c.Bored = 0; c.Lonely = 0; } }
            var f = LastCulture(s, f0, CultureEvent.Custom);
            Check("함께 먹는 때가 관습이 된다: 지난 이틀 같은 시각에 함께 먹었고 오늘도 넷이 식당에서 함께 먹으면 — 그 시각이 배의 관습이 된다 · ⑧이 그 시각을 안다",
                f.Kind == FactKind.Culture && soc.MealCustom == 0 && m.MealHour == 0 && soc.Customs == 1 && s.Causes.Text(f.Cause).Contains("함께 먹는다"),
                $"관습 {soc.MealCustom}시 (마음 {m.MealHour}) · '{(f.Kind == FactKind.Culture ? s.Causes.Text(f.Cause) : "-")}'");
        }

        // ── 관습의 식사: 1시가 관습인 배 — 0시 50분부터 조금 배고픈 넷(0.4 — 보통은 0.55가 넘어야 간다)도 식당에 가서 함께 먹고 · 기쁨이 오른다 /
        //    관습이 없는 배의 같은 넷은 가지 않는다 ──
        {
            (int ate, long shared, long eats, float joy) Meal(bool custom)
            {
                var (s, m, soc, p) = World(4, 4);
                if (custom) soc.SetCustomNow(1);
                foreach (int a in p) { ref var c = ref m[a].C; c.Hunger = 0.4f; c.Joy = 0; }
                var ate = new HashSet<int>();
                for (int k = 0; k < Time.Minutes(100); k++)
                {
                    s.Step();
                    foreach (int a in p) { ref var c = ref m[a].C; c.Sleepy = 0; c.Thirst = 0; c.Bored = 0; c.Lonely = 0; c.Hunger = Math.Min(c.Hunger, 0.4f); if (c.Goal == GoalKind.Eat && m.RoomOf(c.Cell) == mess) ate.Add(a); }
                }
                return (ate.Count, soc.CustomMealsShared, m.CustomEats, p.Max(a => m[a].C.Joy));
            }
            var withT = Task.Run(() => Meal(true)); var without = Meal(false); var with = withT.Result;
            Check("관습의 식사: 1시가 관습인 배는 0시 50분부터 조금 배고픈 넷(0.4)도 식당에 가 함께 먹고 기쁨이 오른다 · 관습이 없는 배의 같은 넷은 가지 않는다",
                with.ate >= 3 && with.shared >= 2 && with.eats >= 3 && with.joy >= 0.1f && without.ate == 0,
                $"관습 있음: 먹은 이 {with.ate} · 함께 먹은 몫 {with.shared} · 관습으로 간 번 {with.eats} · 기쁨 {with.joy:0.00} / 없음: 먹은 이 {without.ate}");
        }

        // ── 방 금기: 넷 가운데 셋이 휴게실을 꺼리면 (장소의 기억 0.6) '아무도 휴게실에서 쉬지 않는다' — 꺼리지 않는 넷째도 쉴 때 휴게실 의자를 두고 식당 의자로 간다
        //    (금기가 없는 배의 같은 넷째는 휴게실 의자에 앉는다) · 꺼림이 걷히면 (0.05) 금기가 풀린다 ──
        {
            (int seatRoom, Sim s, Society soc, Mind m, List<int> p, long f0) Rest(bool taboo)
            {
                var (s, m, soc, p) = World(3, 5);
                int x = m.AddPerson(s, At(23, 1)); m.SetPersonality(x, 0.5f, 0.6f, 0.6f, 0.5f, 0.4f); m[x].C.Bored = 0; p.Add(x);   // 넷째는 휴게실 의자 곁에 있다
                long f0 = s.Facts.Total;
                if (taboo) for (int i = 0; i < 3; i++) m.SetDread(p[i], lounge, 0.6f);
                Run(s, m, p, 11);   // 아직 쉬고 싶지 않다 (금기가 서기 전에 자리를 잡지 않게)
                for (int k = 0; k < Time.Minutes(15); k++)
                {
                    s.Step();
                    foreach (int a in p) { ref var c = ref m[a].C; c.Sleepy = 0; c.Thirst = 0; c.Hunger = 0; c.Lonely = 0; c.Bored = a == x ? 0.9f : 0; }
                }
                var cx = m[x].C;
                if (Debug) Console.WriteLine($"    금기 {taboo} (마음 {m.Taboo(lounge)} · 꺼림 {m.Dread(x, lounge):0.00} · 휴게실 {lounge} 식당 {mess}) 의자 {string.Join(",", lay.Furniture.Where(fu => fu.Type == "Seat").Select(fu => $"{fu.Cells[0]}@{lay.Room[fu.Cells[0]]}"))}: 넷째 {cx.Goal} 목표 칸 {cx.GoalCell} 자리 ({lay.X(cx.Cell)},{lay.Y(cx.Cell)}) 방 {m.RoomOf(cx.Cell)} · 계획 {string.Join(" ", m[x].Plan.Select(q => q.Kind + ":" + q.Cell))}");
                int room = cx.Goal == GoalKind.Relax ? m.RoomOf(cx.Cell) : -1;
                return (room, s, soc, m, p, f0);
            }
            var plainT = Task.Run(() => Rest(false)); var t = Rest(true); var plain = plainT.Result;
            var made = LastCulture(t.s, t.f0, CultureEvent.Taboo);
            foreach (int a in t.p) t.m.SetDread(a, lounge, 0.05f);
            t.s.Run(Time.Minutes(11));
            var lifted = LastCulture(t.s, t.f0, CultureEvent.TabooLifted);
            Check("방 금기: 넷 가운데 셋이 휴게실을 꺼리면 '아무도 휴게실에서 쉬지 않는다' — 꺼리지 않는 넷째도 쉴 때 휴게실 대신 식당 의자로 간다 (금기 없는 배에선 휴게실) · 꺼림이 걷히면 풀린다",
                made.Kind == FactKind.Culture && t.seatRoom == mess && plain.seatRoom == lounge && lifted.Kind == FactKind.Culture && !t.m.Taboo(lounge) && t.soc.Taboos == 1 && t.soc.TaboosLifted == 1,
                $"'{(made.Kind == FactKind.Culture ? t.s.Causes.Text(made.Cause) : "-")}' · 넷째가 쉬러 간 방: 금기 {(t.seatRoom >= 0 ? lay.Rooms[t.seatRoom].Name : "-")} / 없음 {(plain.seatRoom >= 0 ? lay.Rooms[plain.seatRoom].Name : "-")} · '{(lifted.Kind == FactKind.Culture ? t.s.Causes.Text(lifted.Cause) : "-")}'");
        }

        // ── 숨진 이의 침대: 김이 크게 다쳐 숨지면 '숨진 김의 침대엔 아무도 눕지 않는다' — 침대가 없는 이가 졸려도 바로 곁의 그 침대를 두고 다른 침대로 간다 ──
        {
            var s = Worlds.Full(lay, 6);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var soc = s.Get<Society>();
            int dead = m.AddPerson(s, At(14, 2), bed: At(3, 1));
            int tired = m.AddPerson(s, At(3, 2));
            int other = m.AddPerson(s, At(16, 2), bed: At(7, 3));
            s.Run(5);
            long f0 = s.Facts.Total;
            life[m[dead].C.Body].Blunt = 2f;
            s.Run(Time.Minutes(2));
            var f = LastCulture(s, f0, CultureEvent.BedTaboo);
            m[tired].C.Sleepy = 0.9f;
            int bedGoal = -1;
            for (int k = 0; k < Time.Minutes(10) && bedGoal < 0; k++)
            {
                s.Step();
                ref var c = ref m[tired].C;
                if (c.Goal == GoalKind.Sleep && m[tired].Plan.Count > 0 && m[tired].Plan[0].Kind == StepKind.GoTo) bedGoal = m[tired].Plan[0].Cell;
                if (c.Goal == GoalKind.Sleep && life[c.Body].Asleep != 0 && bedGoal < 0) bedGoal = c.Cell;
            }
            Check("숨진 이의 침대: 동료가 숨지면 '숨진 이의 침대엔 아무도 눕지 않는다' — 침대가 없는 이가 졸려도 바로 곁의 그 침대를 두고 다른 침대로 간다",
                life[m[dead].C.Body].Alive == 0 && f.Kind == FactKind.Culture && m.BedTaboo(At(3, 1)) && bedGoal >= 0 && bedGoal != At(3, 1) && lay.FurnitureAt[bedGoal] >= 0 && soc.BedTaboos == 1,
                $"'{(f.Kind == FactKind.Culture ? s.Causes.Text(f.Cause) : "-")}' · 졸린 이가 간 자리 {(bedGoal >= 0 ? $"({lay.X(bedGoal)},{lay.Y(bedGoal)})" : "-")} (숨진 이의 침대 (3,1))");
        }

        // ── 별명은 승무원이 붙인다: 불을 세 번 끈 이 — 그 불을 함께 겪은 동료가 이야기하다 '불끄기 반장'이라 부르기 시작한다 (← 마지막으로 끈 불) ·
        //    모두 그를 조금 더 믿고 · 그는 기쁘다 / 조리대를 두 번 켜 둔 채 떠난 이는 덜 다정한 동료가 '깜빡이'라 놀린다 — 예민한 그는 부른 이를 조금 원망한다 /
        //    아무도 말을 나누지 않으면 별명은 붙지 않는다 (일을 세는 것만으로는 붙지 않는다) ──
        {
            var (s, m, soc, p) = World(4, 7);
            int hero = p[0], slip = p[2];
            m.SetPersonality(p[1], 0.5f, 0.6f, 0.95f, 0.3f, 0.4f);   // 외향 · 덜 다정
            m.SetPersonality(slip, 0.5f, 0.6f, 0.6f, 0.5f, 0.85f);
            foreach (int a in p) foreach (int b in p) if (a != b) soc.Known[a * soc.N + b] = 0.5f;   // 오래 함께 지낸 사이 — 말을 자주 건다
            float trust0 = soc.TrustIn(p[3], hero);
            int fire = 0;
            for (int i = 0; i < 3; i++)
            {
                fire = s.Causes.Add(CauseKind.Fire, $"작업대에 불이 붙었다 ({i + 1})", s.Tick, At(14, 2));
                s.Facts.Emit(s.Tick, FactKind.Extinguished, At(14, 2), 1, fire, hero, -1);
                s.Run(30);
            }
            for (int i = 0; i < 2; i++)
            {
                int c = s.Causes.Add(CauseKind.Person, $"{m.NameOf(slip)}{(i == 0 ? "" : "가 또")} 조리대를 켜 둔 채 자리를 떴다", s.Tick, At(14, 2), 0, 0, slip);
                s.Facts.Emit(s.Tick, FactKind.Mistake, At(14, 2), 0.1f, c, slip, 0);
                s.Run(30);
            }
            long beforeTalk = soc.Nicknames, talks0 = soc.Talks;
            for (int k = 0; k < Time.Hours(3) && soc.Nicknames < 2; k++) { s.Step(); Keep(s, m, p); }
            var nicks = s.Facts.Since(0).Where(f => f.Kind == FactKind.Culture && (int)f.Size == (int)CultureEvent.Nickname).ToList();
            var hf = nicks.FirstOrDefault(f => f.Actor == hero); var sf = nicks.FirstOrDefault(f => f.Actor == slip);
            string ht = hf.Kind == FactKind.Culture ? s.Causes.Text(hf.Cause) : "-", st = sf.Kind == FactKind.Culture ? s.Causes.Text(sf.Cause) : "-";
            int hb = hero < soc.NickBy.Length ? soc.NickBy[hero] : -1, sb = slip < soc.NickBy.Length ? soc.NickBy[slip] : -1;
            Check("별명은 승무원이 붙인다: 불을 세 번 끈 이를 그 불을 함께 겪은 동료가 이야기하다 '불끄기 반장'이라 부른다 (← 마지막 불) · 모두 그를 조금 더 믿는다 / 조리대를 두 번 켜 둔 이를 덜 다정한 동료가 '깜빡이'라 놀린다 — 예민한 그는 조금 원망한다 / 말을 나누기 전에는 붙지 않는다",
                beforeTalk == 0 && soc.NickOf(hero) == "불끄기 반장" && hb >= 0 && hb != hero && ht.Contains("'불끄기 반장'이라") && s.Causes[hf.Cause].Parent == fire
                && soc.TrustIn(p[3], hero) >= trust0 + 0.05f - 1e-4 && soc.NickOf(slip) == "깜빡이" && sb >= 0 && m[sb].C.Agree < 0.6f && st.Contains("'깜빡이'라") && soc.GrudgeOf(slip, sb) >= 0.05f - 1e-4 && soc.Nicknames == 2 && soc.Talks > talks0,
                $"이야기 전 별명 {beforeTalk} · '{ht}' ← '{(hf.Kind == FactKind.Culture ? s.Causes.Text(s.Causes[hf.Cause].Parent) : "-")}' · 믿음 {trust0:0.00} → {soc.TrustIn(p[3], hero):0.00} / '{st}' · 원망 {(sb >= 0 ? soc.GrudgeOf(slip, sb) : 0):0.00} · 이야기 {soc.Talks - talks0}");
        }

        // ── 추모: 다섯 가운데 하나가 숨지면 — 그와 가장 가까웠던 이가 식당으로 부르고 · 모인 이마다 한 마디 ('늘 먼저 웃던 사람이었다…') ·
        //    모인 이의 슬픔이 반으로 · 곁에서 본 죽음의 충격이 덜어지고 · 함께 슬퍼한 이들끼리 가까워진다 ('추모') ──
        {
            var (s, m, soc, p) = World(5, 8);
            var life = s.Get<Life>();
            int dead = p[4], friend = p[0];
            int N = soc.N;
            soc.Bond[friend * N + dead] = 0.8f; soc.Bond[dead * N + friend] = 0.8f;
            for (int i = 1; i < 4; i++) soc.Bond[p[i] * N + dead] = 0.55f;   // 모두 가까웠다 — 같은 추억의 말 셋을 넷이 나눠 한다
            life[m[dead].C.Body].Blunt = 2f;
            s.Run(Time.Minutes(2));
            var alive = p.Take(4).ToList();
            for (int k = 0; k < Time.Hours(1) && m.NotYet(s, friend) != null; k++) { s.Step(); Keep(s, m, alive); }   // 마음이 가라앉을 때까지 (부르는 이의 판단)
            float grief0 = soc.GriefOf(friend), shock0 = m[friend].C.Shock;
            float bond0 = soc.BondOf(p[1], p[2]);
            long f0 = s.Facts.Total;
            m.CallGatheringNow(s);
            Run(s, m, alive, 14);
            int gathered = alive.Count(a => m.RoomOf(m[a].C.Cell) == mess);
            Run(s, m, alive, 10);
            var f = LastCulture(s, f0, CultureEvent.Memorial);
            var lines = Lines(s, soc, 12);
            var open = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Meeting);
            var said = soc.Sayings.Where(y => y.Topic == Topic.Death && s.Causes[y.Line].Parent == open.Cause).Select(y => s.Causes.Text(y.Line)[(s.Causes.Text(y.Line).IndexOf(':') + 1)..]).ToList();
            bool varied = said.Count >= 3 && said.Zip(said.Skip(1)).All(z => z.First != z.Second);   // 앞사람과 같은 말을 되풀이하지 않는다 (보며 고친 결함)
            if (Debug) foreach (var l in lines) Console.WriteLine($"      {l}");
            Check("추모: 하나가 숨지면 그와 가장 가까웠던 이가 식당으로 부르고 · 모인 이마다 한 마디 · 슬픔이 반으로 · 충격이 덜어지고 · 함께 슬퍼한 이들끼리 가까워진다",
                f.Kind == FactKind.Culture && open.Actor == friend && gathered >= 3 && varied && lines.Any(l => l.Contains("웃던") || l.Contains("밤이 생각난다") || l.Contains("목소리가"))
                && soc.GriefOf(friend) <= grief0 * 0.55f && m[friend].C.Shock <= Math.Max(0, shock0 - 0.05f) + 1e-3 && soc.BondOf(p[1], p[2]) >= bond0 + 0.03f - 1e-4 && soc.Memorials == 1,
                $"'{(f.Kind == FactKind.Culture ? s.Causes.Text(f.Cause) : "-")}' · 부른 이 {(open.Actor >= 0 ? m.NameOf(open.Actor) : "-")} · 모임 {gathered}/4 · 기린 말 {string.Join(" /", said)} · 슬픔 {grief0:0.00} → {soc.GriefOf(friend):0.00} · 충격 {shock0:0.00} → {m[friend].C.Shock:0.00} · 정 {bond0:0.00} → {soc.BondOf(p[1], p[2]):0.00}");
        }

        // ── 추모는 부르는 사람이 정한다: 동료가 숨진 직후 휴게실 의자에 불이 붙었다 (휴게실에 있던 이가 보고 외친다) — 가장 가까웠던 이는 불이 타고 · 동료가 끄러 간 동안에는
        //    '추모를 미뤘다 — 아직 불이 있다고 여긴다'를 남기고 기다리고 · 불이 꺼지고 모두 손을 놓은 뒤에야 식당으로 부른다
        //    (함장이 물은 것: 사고 수습이 다 안 끝났는데 추모하지 않나 — 엔진의 시계가 아니라 승무원의 판단으로) ──
        {
            var (s, m, soc, p) = World(4, 12);
            var life = s.Get<Life>();
            p.Add(m.AddPerson(s, At(24, 1))); m.SetPersonality(p[4], 0.5f, 0.6f, 0.6f, 0.5f, 0.4f);   // 휴게실에서 쉬던 이
            (p[3], p[4]) = (p[4], p[3]);
            s.Run(2);
            int dead = p[4], friend = p[0], N = soc.N;
            soc.Bond[friend * N + dead] = 0.8f;
            life[m[dead].C.Body].Blunt = 2f;
            s.Run(Time.Minutes(1));
            s.Get<Chemistry>().Ignite(At(26, 2), 3e6, s.Causes.Add(CauseKind.Setup, "휴게실 의자 곁 충전기에서 불꽃이 튀었다", s.Tick, At(26, 2)));   // 휴게실에 있던 이가 보고 외친다
            for (int k = 0; k < Time.Minutes(10) && m.NotYet(s, friend) == null; k++) s.Step();   // 불을 알게 될 때까지 (모르는 위험으로는 미루지 않는다)
            long f0 = s.Facts.Total;
            string? why = m.NotYet(s, friend);
            m.CallGatheringNow(s);
            bool early = s.Facts.Since(f0).Any(x => x.Kind == FactKind.Meeting);
            long settled = -1;
            for (int k = 0; k < Time.Minutes(90) && settled < 0; k++)
            {
                s.Step();
                foreach (int a in p.Take(4)) { ref var c = ref m[a].C; c.Hunger = 0; c.Thirst = 0; c.Sleepy = 0; }
                if (k % 25 == 0) { if (m.NotYet(s, friend) == null) settled = s.Tick; else m.CallGatheringNow(s); }
                if (Debug && k % Time.Minutes(10) == 0) Console.WriteLine($"    {Time.Clock(s.Tick)} {m.NameOf(friend)}: {m.NotYet(s, friend) ?? "때가 됐다"} · 타는 칸 {s.Get<Chemistry>().BurningCount}");
            }
            bool openedWhileBusy = s.Facts.Since(f0).Any(x => x.Kind == FactKind.Meeting && x.Tick < settled);
            var deferred = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Culture && (int)x.Size == (int)CultureEvent.MemorialDeferred);
            m.CallGatheringNow(s);
            var open = s.Facts.Since(f0).LastOrDefault(x => x.Kind == FactKind.Meeting);
            Check("추모는 부르는 사람이 정한다: 동료가 숨진 직후 불이 붙으면 — 가장 가까웠던 이는 불을 안 뒤로 불이 타고 동료가 끄는 동안 '추모를 미뤘다'를 남기고 기다리고 · 불이 꺼지고 모두 손을 놓은 뒤에야 부른다",
                why != null && !early && !openedWhileBusy && settled > 0 && deferred.Kind == FactKind.Culture && deferred.Actor == friend && soc.MemorialsDeferred == 1
                && open.Kind == FactKind.Meeting && (int)open.Size == (int)CouncilKind.Memorial && open.Actor == friend,
                $"불을 안 {m.NameOf(friend)}: '{why}' · '{(deferred.Kind == FactKind.Culture ? s.Causes.Text(deferred.Cause) : "-")}' · 때가 됐다고 여김 {(settled > 0 ? Time.Clock(settled) : "안 됨")} · 그 뒤 '{(open.Kind == FactKind.Meeting ? s.Causes.Text(open.Cause) : "-")}'");
        }

        // ── 축하: 불이 번졌다가 (위기) 꺼지고 모두 살았으면 — 가장 외향인 이가 '모두 무사했다 — 한잔하자' · 불을 끈 이에게 한잔 · 기쁨 +0.3 · 피로가 풀린다 /
        //    그 사이 누가 숨졌으면 축하는 없다 ──
        {
            (long cel, string first, List<string> lines, float joy, float weary) Feast(bool death)
            {
                var (s, m, soc, p) = World(5, 9);
                var life = s.Get<Life>();
                int loud = p[1], hero = p[2];
                m.SetPersonality(loud, 0.5f, 0.6f, 0.95f, 0.5f, 0.4f);
                foreach (int a in p) { m[a].C.Weary = 0.4f; m[a].C.Joy = 0; }
                int fire = s.Causes.Add(CauseKind.Fire, "식당 조리대에 불이 붙었다", s.Tick, At(14, 2));
                s.Facts.Emit(s.Tick, FactKind.Ignition, At(14, 2), 1, fire, -1, -1);
                s.Facts.Emit(s.Tick, FactKind.FireSpread, At(15, 2), 1, fire, -1, -1);
                s.Facts.Emit(s.Tick, FactKind.Extinguished, At(15, 2), 1, fire, hero, -1);
                if (death) life[m[p[4]].C.Body].Blunt = 2f;
                s.Run(Time.Minutes(2));
                var alive = death ? p.Take(4).ToList() : p;
                long f0 = s.Facts.Total;
                m.CallGatheringNow(s);
                var open = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Meeting);
                Run(s, m, alive, 26);
                return (soc.Celebrations, open.Kind == FactKind.Meeting ? s.Causes.Text(open.Cause) : "-", Lines(s, soc, 15), m[p[0]].C.Joy, m[p[0]].C.Weary);
            }
            var diedT = Task.Run(() => Feast(true)); var ok = Feast(false); var died = diedT.Result;
            if (Debug) foreach (var l in ok.lines) Console.WriteLine($"      {l}");
            Check("축하: 불이 번졌다 꺼지고 모두 살았으면 가장 외향인 이가 '모두 무사했다 — 한잔하자' · 불을 끈 이에게 한잔 · 기쁨이 오르고 피로가 풀린다 / 누가 숨졌으면 축하는 없다",
                ok.cel == 1 && ok.first.Contains("모두 무사했다") && ok.lines.Any(l => l.Contains("에게 한잔")) && ok.joy >= 0.25f && ok.weary <= 0.36f && died.cel == 0,
                $"'{ok.first}' · 말 {ok.lines.Count} ('{ok.lines.FirstOrDefault(l => l.Contains("한잔"))}') · 기쁨 {ok.joy:0.00} · 피로 0.40 → {ok.weary:0.00} / 숨진 배: 축하 {died.cel}");
        }

        // ── 배의 규칙: 사흘에 말소리에 깬 일이 셋이면 회의가 열린다 — 깬 이들이 '밤엔 조용히 하자' · 과반이면 '밤 10시부터 아침 6시까지 자는 이 곁에서는 말하지 않는다'가 배의 규칙이 된다 /
        //    켜 둔 조리대가 둘이면 다음 회의에서 '조리대를 떠날 땐 끄고 간다' (⑧ 켜 둔 채 잊는 몫이 반) ──
        {
            var (s, m, soc, p) = World(5, 10);
            int talker = p[0];
            m.SetPersonality(talker, 0.5f, 0.3f, 0.95f, 0.5f, 0.4f);
            m.SetPersonality(p[4], 0.5f, 0.9f, 0.6f, 0.5f, 0.4f);   // 가장 성실한 이가 조리대를 켜 둔 당사자 — 그가 규칙 회의를 열지는 않는다
            long f0 = s.Facts.Total;
            for (int i = 0; i < 3; i++)
            {
                int said = s.Causes.Add(CauseKind.Society, $"{m.NameOf(talker)}: \"그래서 말이야…\"", s.Tick, At(3, 2), 0, 0, talker);
                s.Facts.Emit(s.Tick, FactKind.Woke, At(3, 2), 0, said, p[1 + i], talker);
                s.Run(30);
            }
            m.CallCouncilNow(s);
            Run(s, m, p, 26);
            var quiet = LastCulture(s, f0, CultureEvent.Rule);
            var lines = Lines(s, soc, 15);
            long f1 = s.Facts.Total;
            for (int i = 0; i < 2; i++)
            {
                int c = s.Causes.Add(CauseKind.Person, $"{m.NameOf(p[4])} 조리대를 켜 둔 채 자리를 떴다", s.Tick, At(14, 2), 0, 0, p[4]);
                s.Facts.Emit(s.Tick, FactKind.Mistake, At(14, 2), 0.1f, c, p[4], 0);
                s.Run(30);
            }
            m.CallCouncilNow(s);
            var stoveOpen = s.Facts.Since(f1).FirstOrDefault(x => x.Kind == FactKind.Meeting);
            Run(s, m, p, 26);
            var stove = LastCulture(s, f1, CultureEvent.Rule);
            if (Debug) foreach (var l in lines) Console.WriteLine($"      {l}");
            Check("배의 규칙: 사흘에 말소리에 깬 일이 셋이면 회의 — '밤엔 조용히 하자' · 과반이면 밤의 규칙이 된다 / 켜 둔 조리대 둘이면 다음 회의에서 조리대 규칙 (⑧이 지킨다)",
                soc.QuietRule && quiet.Kind == FactKind.Culture && (int)quiet.Other == 1 && lines.Any(l => l.Contains("조용히") || l.Contains("말을 아끼자")) && soc.StoveRule && m.StoveRule && (int)stove.Other == 2 && soc.RulesMade == 2 && stoveOpen.Kind == FactKind.Meeting && stoveOpen.Actor != p[4],
                $"밤 규칙 {soc.QuietRule} · 조리대 규칙 {soc.StoveRule}/{m.StoveRule} · 조리대 회의를 연 이 {(stoveOpen.Actor >= 0 ? m.NameOf(stoveOpen.Actor) : "-")} (켜 둔 이 {m.NameOf(p[4])}) · '{(stove.Kind == FactKind.Culture ? s.Causes.Text(stove.Cause) : "-")}' · 말 {string.Join(" / ", lines.Take(5))}");
        }

        // ── 조용한 밤: 밤 0시 침실 — 둘이 자고 · 셋이 깨어 곁에 있다. 밤의 규칙이 있으면 성실한 셋(1.0)은 말을 삼킨다 (자는 이가 깨지 않는다) /
        //    규칙이 없으면 이야기하다 깨운다 / 덜 성실한 셋(0.1)은 규칙을 어기고 — 깨우면 '밤의 규칙을 어겼다' · 탓이 두 배 ──
        {
            var nightLay = Worlds.Ascii(CultureShip, legend);   // 쉴 곳이 따로 없는 배 — 잠 못 드는 셋은 침실에 남는다
            (int woke, long kept, long broke, float blame) Night(bool rule, float consc)
            {
                var s = Worlds.Full(nightLay, 11);
                var m = s.Get<Mind>(); var soc = s.Get<Society>(); var life = s.Get<Life>();
                var sleepers = new[] { m.AddPerson(s, At(3, 1), bed: At(3, 1)), m.AddPerson(s, At(5, 1), bed: At(5, 1)) };
                var awake = new[] { m.AddPerson(s, At(3, 2)), m.AddPerson(s, At(4, 2)), m.AddPerson(s, At(5, 2)) };
                foreach (int a in awake) m.SetPersonality(a, 0.5f, consc, 0.9f, 0.5f, 0.4f);
                foreach (int a in sleepers) m.SetPersonality(a, 0.5f, 0.6f, 0.5f, 0.5f, 0.6f);
                s.Run(2);
                foreach (int a in awake) foreach (int b in awake) if (a != b) { soc.Known[a * soc.N + b] = 1; soc.Grudge[a * soc.N + b] = 0.45f; m[a].C.Weary = 0.8f; }   // 묵은 앙금 — 말끝마다 부딪친다 (72dB)
                if (rule) soc.SetRuleNow(1);
                long f0 = s.Facts.Total;
                for (int k = 0; k < Time.Minutes(150); k++)
                {
                    s.Step();
                    foreach (int a in sleepers) { ref var c = ref m[a].C; c.Sleepy = Math.Max(c.Sleepy, 0.9f); c.Hunger = 0; c.Thirst = 0; }
                    foreach (int a in awake) { ref var c = ref m[a].C; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Bored = 0; c.Lonely = 0; life[c.Body].Asleep = 0; }
                }
                int woke = s.Facts.Since(f0).Count(f => f.Kind == FactKind.Woke && f.Cause != 0 && sleepers.Contains(f.Actor) && f.Other >= 0);
                float blame = sleepers.Max(a => awake.Max(b => soc.GrudgeOf(a, b)));
                return (woke, soc.QuietKept, soc.RuleBreaks, blame);
            }
            var noneT = Task.Run(() => Night(false, 1f)); var looseT = Task.Run(() => Night(true, 0.1f)); var kept = Night(true, 1f);
            var none = noneT.Result; var loose = looseT.Result;
            Check("조용한 밤: 밤의 규칙이 있으면 자는 이 곁의 성실한 셋은 말을 삼킨다 (깬 이 없음) · 규칙이 없으면 이야기하다 깨운다 · 덜 성실한 셋은 어기고 — '밤의 규칙을 어겼다' · 탓이 더 크다",
                kept.woke == 0 && kept.kept >= 2 && none.woke >= 1 && loose.broke >= 1 && loose.blame > none.blame,
                $"규칙 · 성실: 깸 {kept.woke} · 삼킨 말 {kept.kept} / 규칙 없음: 깸 {none.woke} · 탓 {none.blame:0.00} / 규칙 · 덜 성실: 깸 {loose.woke} · 어김 {loose.broke} · 탓 {loose.blame:0.00}");
        }
    }
}
