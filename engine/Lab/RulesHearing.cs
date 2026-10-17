using ShipEngine;
using ShipView;
using ShipView.Audio;

namespace Lab;

/// <summary>
/// 소리 깊이 (바퀴 2 최상) 규칙 시험 — 바탕 소음 · 가림 · 롬바르 · 시끄러운 잠자리 · 낡은 기계 소리를 듣고 미리 갈기 ·
/// 불 우르릉 · 기체 폭발 쾅 · 열폭주 펑 · 쉬익 = 분출 물리 · 늦게 닿는 소리 · 낡은 베어링의 딱딱 소리.
/// </summary>
public static partial class Program
{
    /// <summary>
    /// 소리 시험 배: 왼쪽 기계실(냉각 펌프 둘 · 배전반 · 배터리 · 침대) · 오른쪽 조용한 방(의자 · 침대) · 아래 통로(작업대 — 수리 꾸러미).
    /// 기계실 ↔ 조용한 방은 닫힌 문 하나 · 둘 다 통로로 문이 있다.
    /// </summary>
    public const string HearShip = @"
#########################
#.........#.............#
#..u...u..#....a........#
#.........D.............#
#.......e.#.............#
#..p.b....#.....e.......#
#####D########D##########
#.............w.........#
#.......................#
#########################";

    public static Dictionary<char, string> HearLegend => new()
    {
        ['u'] = "CoolantPump", ['p'] = "PowerPanel", ['b'] = "Battery", ['e'] = "Bed", ['a'] = "Seat", ['w'] = "Workbench",
    };

    private static void HearingRules()
    {
        if (!Do("소리 깊이")) return;
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        Console.WriteLine("\n[소리 깊이 — 바탕 소음 · 가림 · 롬바르 · 잠자리 · 고장 조짐 · 불 · 폭발 · 분출 · 늦게 닿음]");
        var lay = Worlds.Ascii(HearShip, HearLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int machine = lay.Room[At(4, 3)], quiet = lay.Room[At(15, 3)];

        Sim World(bool pumps, ulong seed = 3)
        {
            var s = Worlds.Full(lay, seed);
            if (!pumps) { var n = s.Get<Network>(); foreach (var d in n.Devices) if (d.Def.Role == "pump") n.On[d.Id] = 0; }
            s.Run(30); // 1분에 한 번 셈하는 바탕 소음이 한 번 돈다
            return s;
        }

        // ── 바탕 소음: 기계실은 시끄럽고(ISS 측정 56~69dBA) · 닫힌 문 너머 조용한 방 · 잠자리는 조용하다 (NC-40 약 49dBA 아래) · 진공 방은 0 ──
        {
            var d = ShipWorld.Create("Didim", 3); d.Run(30);
            var ds = d.Get<Sound>();
            var byRoom = d.Layout.Rooms.Select(r => (r, db: ds.RoomAmbient(r.Id))).OrderByDescending(x => x.db).ToList();
            var bedRooms = d.Layout.Furniture.Where(f => f.Type == "Bed" && f.Room >= 0).Select(f => f.Room).Distinct().ToList();
            float loudest = byRoom[0].db, beds = bedRooms.Count > 0 ? bedRooms.Max(r => ds.RoomAmbient(r)) : 99;

            var on = World(true); var off = World(false);
            var son = on.Get<Sound>(); var soff = off.Get<Sound>();
            float mOn = son.RoomAmbient(machine), mOff = soff.RoomAmbient(machine), qOn = son.RoomAmbient(quiet);
            var f = on.Get<Field>();
            foreach (int c in lay.Rooms[quiet].Cells) for (int g = 0; g < f.G; g++) f.RemoveGas(c, g, f.N[c * f.G + g]);
            on.Run(26);
            float qVac = son.RoomAmbient(quiet);
            Check("소리: 바탕 소음 — 돌아가는 펌프 둘의 기계실은 시끄럽고(60dB 넘게) · 끄면 조용하다 · 닫힌 문 너머 방은 25dB 넘게 작다 · 진공 방은 소리가 없다 · 디딤호 가장 시끄러운 방 55dB 넘게 · 잠자리 49dB 아래 (ISS NC-50 · NC-40)",
                mOn > 60 && mOff < 40 && qOn < mOn - 25 && qVac < 10 && loudest > 55 && beds < 49,
                $"기계실 {mOn:0}dB (끄면 {mOff:0}) · 조용한 방 {qOn:0} · 진공 {qVac:0} / 디딤호 {byRoom[0].r.Name} {loudest:0} · {byRoom[1].r.Name} {byRoom[1].db:0} · 잠자리 많아야 {beds:0}dB");
        }

        // ── 가림: 시끄러운 기계실에서는 문 너머 통로의 비명을 못 듣는다 (펌프를 끄면 듣는다) ──
        {
            (bool heard, float amb) Scream(bool pumps)
            {
                var s = World(pumps);
                var m = s.Get<Mind>(); var snd = s.Get<Sound>();
                int p = m.AddPerson(s, At(4, 4)), loud = m.AddPerson(s, At(3, 7));
                s.Run(2);
                snd.Emit(s, At(3, 7), 85, SoundKind.Scream, 0, loud);
                s.Run(2);
                return (m[p].Beliefs.Any(b => b.Kind == BeliefKind.Help && b.Conf > 0.1f), snd.Ambient(At(4, 4)));
            }
            var quietRun = Scream(false); var loudRun = Scream(true);
            Check("소리: 가림 — 펌프 둘이 도는 기계실(바탕 소음 60dB 넘게)에서는 닫힌 문 너머 통로의 비명을 못 듣고 · 펌프를 끄면 듣는다",
                quietRun.heard && !loudRun.heard && loudRun.amb > 60, $"조용함 {quietRun.amb:0}dB: {(quietRun.heard ? "들었다" : "못 들었다")} / 시끄러움 {loudRun.amb:0}dB: {(loudRun.heard ? "들었다" : "못 들었다")}");
        }

        // ── 롬바르 효과: 시끄러운 곳에서는 크게 외친다 · 주컴퓨터 방송은 바탕 소음보다 15dB 크게 ──
        {
            (float shout, float alarm, float amb) Run(bool pumps)
            {
                var s = World(pumps);
                var m = s.Get<Mind>(); var snd = s.Get<Sound>(); var ch = s.Get<Chemistry>();
                int p = m.AddPerson(s, At(5, 4));
                s.Run(2);
                long from = snd.Logged;
                m.Announce(s, At(4, 3), 80, new Belief { Kind = BeliefKind.Fire, Cell = At(8, 4), Conf = 1, Subject = -1 });
                float alarm = snd.LogSince(from).Where(e => e.Kind == SoundKind.Alarm).Select(e => e.Db).DefaultIfEmpty(0).Max();
                from = snd.Logged;
                ch.Ignite(At(8, 4), 3e6, s.Causes.Add(CauseKind.Setup, "불씨", s.Tick));
                float shout = 0;
                for (int i = 0; i < 60 && shout == 0; i++) { s.Step(); shout = snd.LogSince(from).Where(e => e.Kind == SoundKind.Voice && e.Actor == p).Select(e => e.Db).DefaultIfEmpty(0).Max(); }
                return (shout, alarm, snd.Ambient(At(4, 3)));
            }
            var q = Run(false); var l = Run(true);
            Check("소리: 롬바르 효과 — 시끄러운 기계실에서 불을 본 사람은 더 크게 외친다(75dB + 0.5 × 넘는 소음) · 주컴퓨터 방송은 바탕 소음보다 15dB 크게 (NFPA 72 · ISO 7731)",
                q.shout is > 74 and < 76 && l.shout > q.shout + 8 && q.alarm is > 79 and < 81 && l.alarm >= l.amb + 14.5f,
                $"외침 조용함 {q.shout:0}dB · 시끄러움({l.amb:0}dB) {l.shout:0}dB / 방송 {q.alarm:0} → {l.alarm:0}dB");
        }

        // ── 시끄러운 잠자리: 펌프가 도는 기계실 침대에서는 사건 소리가 없어도 잠이 얕다 ──
        {
            (float loss, float sleepy) Sleep(bool pumps)
            {
                var s = World(pumps);
                var m = s.Get<Mind>();
                int bed = lay.Furniture.First(fu => fu.Type == "Bed" && fu.Room == machine).Id;
                int p = m.AddPerson(s, lay.Furniture[bed].Cells[0]);
                m[p].C.Bed = lay.Furniture[bed].Cells[0];
                s.Run(5);
                m[p].C.Sleepy = 1; m[p].C.Hunger = 0; m[p].C.Thirst = 0;
                float loss = 0; int n = 0;
                for (int k = 0; k < Time.Hours(3); k++)
                {
                    s.Step(); m[p].C.Hunger = 0; m[p].C.Thirst = 0;
                    if (k > Time.Hours(1)) { loss += m[p].C.SleepLoss; n++; }
                }
                return (loss / Math.Max(1, n), m[p].C.Sleepy);
            }
            var q = Sleep(false); var l = Sleep(true);
            Check("소리: 시끄러운 잠자리 — 펌프 곁 침대는 사건 소리가 없어도 이어지는 소음에 잠이 얕다 (WHO 잠자리 30dBA · ISS 승무원 귀마개)",
                l.loss > q.loss + 0.15f && l.sleepy > q.sleepy, $"얕음 조용함 {q.loss:0.00} · 시끄러움 {l.loss:0.00} / 졸림 {q.sleepy:0.00} · {l.sleepy:0.00}");
        }

        // ── 낡은 기계 소리: 곧 망가질 펌프의 끽끽을 들은 고치는 솜씨 있는 이가 작업대의 수리 꾸러미로 미리 간다 · 솜씨 없는 이 · 아무도 없으면 망가진다 ──
        {
            (bool broke, bool fixedByEar, float worn, string story) Worn(float skill, bool person)
            {
                var s = World(true);
                var m = s.Get<Mind>(); var n = s.Get<Network>();
                int pump = n.Devices.First(d => d.Def.Role == "pump").Id;
                n.AgeLeft(pump, Part.Mechanical, 1.5);
                s.Run(2);
                float worn = n.WornDb(pump);
                int p = -1;
                if (person)
                {
                    p = m.AddPerson(s, At(6, 4));
                    m[p].C.SkRepair = skill; m[p].C.Station = -1; m[p].C.Sleepy = 0;
                }
                for (int k = 0; k < Time.Hours(2); k++)
                {
                    s.Step();
                    if (p >= 0) { m[p].C.Hunger = 0; m[p].C.Thirst = 0; m[p].C.Sleepy = 0; }
                }
                var fix = s.Facts.Since(0).Where(x => x.Kind == FactKind.Repaired && x.Other == pump && x.Actor == p && p >= 0).ToList();
                string story = fix.Count > 0 ? s.Causes.Story(fix[0].Cause) : "";
                return (n.IsBroken(pump, Part.Mechanical), fix.Count > 0, worn, story);
            }
            var skilled = Worn(0.8f, true); var clumsy = Worn(0.1f, true); var nobody = Worn(0.8f, false);
            Check("소리: 낡은 기계 소리 — 곧 망가질 펌프(마모가 숨은 한계 끝)는 소리가 커지고 끽끽거린다 · 들은 고치는 솜씨 있는 이가 미리 갈아 망가지지 않는다 · 원인 사슬이 그 소리에 이어진다 · 솜씨 없는 이 · 아무도 없으면 망가진다",
                skilled.worn > 10 && !skilled.broke && skilled.fixedByEar && skilled.story.Contains("끽끽") && clumsy.broke && nobody.broke,
                $"낡은 몫 +{skilled.worn:0.0}dB / 솜씨 0.8: {(skilled.broke ? "망가짐" : "멀쩡")} · {skilled.story} / 솜씨 0.1: {(clumsy.broke ? "망가짐" : "멀쩡")} / 아무도 없음: {(nobody.broke ? "망가짐" : "멀쩡")}");
        }

        // ── 알림: 고칠 줄 모르는 이가 끽끽을 듣고 '소리가 이상하다'고 알리면 → 일감판(주컴퓨터)이 통로 끝 충전대의 로봇에게 맡겨 미리 간다 ──
        {
            var layC = Worlds.Ascii(HearShip.Replace("#.......................#\n#########################", "#..................c..r.#\n#########################"),
                new Dictionary<char, string>(HearLegend) { ['c'] = "MainComputer", ['r'] = "RobotDock" });
            // 숨은 한계가 표준 수명의 80%보다 짧은 펌프 (와이블 — 넷에 하나 남짓): 쓴 시간으로 하는 예방 정비로는 아직 멀었는데 곧 망가진다 — 소리로만 안다
            ulong seed = 3; int pumpPick = -1;
            for (ulong sd = 3; sd < 40 && pumpPick < 0; sd++)
            {
                var probe = Worlds.Full(layC, sd); var pn = probe.Get<Network>();
                foreach (var d in pn.Devices.Where(d => d.Def.Role == "pump"))
                {
                    pn.AgeLeft(d.Id, Part.Mechanical, 1.5);
                    if (pn.Wear[d.Id * 4 + (int)Part.Mechanical] < Work.MaintainAt * d.Life(Part.Mechanical)) { seed = sd; pumpPick = d.Id; break; }
                }
            }
            (bool broke, bool robotFixed, string story) Report(bool person)
            {
                var s = Worlds.Full(layC, seed);
                s.Run(30);
                var m = s.Get<Mind>(); var n = s.Get<Network>();
                int robot = m.AddRobot(s, Worlds.At(layC, 22, 8), AgentKind.Robot, Worlds.At(layC, 22, 8));
                int pump = pumpPick;
                n.AgeLeft(pump, Part.Mechanical, 1.5);
                int p = -1;
                if (person) { p = m.AddPerson(s, Worlds.At(layC, 6, 4)); m[p].C.SkRepair = 0.1f; m[p].C.Station = -1; }
                for (int k = 0; k < Time.Hours(2); k++)
                {
                    s.Step();
                    if (p >= 0) { m[p].C.Hunger = 0; m[p].C.Thirst = 0; m[p].C.Sleepy = 0; }
                }
                var fix = s.Facts.Since(0).Where(x => x.Kind == FactKind.Repaired && x.Other == pump && x.Actor == robot).ToList();
                bool failed = s.Facts.Since(0).Any(x => x.Kind == FactKind.MachineFail && x.Other == pump);
                return (failed, fix.Count > 0 && !failed, fix.Count > 0 ? s.Causes.Story(fix[0].Cause) : "");
            }
            var told = Report(true); var alone = Report(false);
            Check("소리: 알림 — 고칠 줄 모르는 이가 곧 망가질 펌프의 끽끽을 듣고 '소리가 이상하다'고 알리면 일감판이 먼 충전대의 로봇에게 맡겨 미리 간다 (원인 사슬이 알린 사람까지) · 아무도 듣지 못하면 망가진다",
                told.robotFixed && !told.broke && told.story.Contains("알렸다") && alone.broke,
                $"알림: {(told.broke ? "망가짐" : "멀쩡")} · {told.story} / 아무도 없음: {(alone.broke ? "망가짐" : "멀쩡")}");
        }

        // ── 불 우르릉: 닫힌 문 너머 방의 불을 소리로 안다 (조용할 때만 — 펌프가 돌면 가려진다) ──
        {
            (bool heard, float roar) Fire(bool pumps)
            {
                var s = World(pumps);
                var m = s.Get<Mind>(); var snd = s.Get<Sound>(); var ch = s.Get<Chemistry>();
                int p = m.AddPerson(s, At(7, 3));
                s.Run(2);
                long from = snd.Logged;
                ch.Ignite(At(15, 2), 3e6, s.Causes.Add(CauseKind.Setup, "불씨", s.Tick));
                bool heard = false;
                for (int k = 0; k < Time.Minutes(3) && !heard; k++)
                {
                    s.Step();
                    heard = m[p].Beliefs.Any(b => b.Kind == BeliefKind.Fire && b.Src == BeliefSource.Heard && b.Conf > 0.1f);
                }
                float roar = snd.LogSince(from).Where(e => e.Kind == SoundKind.Roar).Select(e => e.Db).DefaultIfEmpty(0).Max();
                return (heard, roar);
            }
            var q = Fire(false); var l = Fire(true);
            Check("소리: 불 우르릉 — 타는 방은 우르릉 소리를 낸다(60 + 10·log kW) · 닫힌 문 너머 사람이 '불'을 소리로 안다 · 펌프가 도는 기계실에서는 가려 못 듣는다",
                q.roar > 75 && q.heard && !l.heard, $"우르릉 {q.roar:0}dB / 조용함 {(q.heard ? "들었다" : "못 들었다")} · 시끄러움 {(l.heard ? "들었다" : "못 들었다")}");
        }

        // ── 기체 폭발 쾅: 수소 주머니가 터지면 쾅 하나(에너지에서) — 옆 방에 자던 이가 깬다 ──
        {
            (float bang, bool woke, string story) Boom(bool hydrogen)
            {
                var s = World(false);
                var m = s.Get<Mind>(); var snd = s.Get<Sound>(); var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
                int bed = lay.Furniture.First(fu => fu.Type == "Bed" && fu.Room == machine).Id;
                int p = m.AddPerson(s, lay.Furniture[bed].Cells[0]);
                m[p].C.Bed = lay.Furniture[bed].Cells[0]; m[p].C.Sleepy = 1;
                for (int k = 0; k < Time.Minutes(10) && s.Get<Life>()[m[p].C.Body].Asleep == 0; k++) s.Step();
                bool asleep = s.Get<Life>()[m[p].C.Body].Asleep != 0;
                long from = snd.Logged, facts = s.Facts.Total;
                if (hydrogen) foreach (int c in new[] { At(19, 3), At(20, 3), At(19, 4), At(20, 4) }) f.AddGas(c, f.T.gH2, 12, f.Temp(c));
                ch.Ignite(At(19, 3), 2e4, s.Causes.Add(CauseKind.Setup, "불꽃", s.Tick));
                s.Run(12);
                var e = snd.LogSince(from).Where(x => x.Kind == SoundKind.Bang).OrderByDescending(x => x.Db).FirstOrDefault();
                bool woke = asleep && s.Facts.Since(facts).Any(x => x.Kind == FactKind.Woke && x.Actor == p);
                return (e.Db, woke, e.Cause != 0 ? s.Causes.Story(e.Cause) : "");
            }
            var h2 = Boom(true); var none = Boom(false);
            Check("소리: 기체 폭발 — 수소 주머니가 터지면 쾅(85 + 10·log kJ · 100dB 넘게) · 닫힌 문 너머 자던 이가 깬다 · 수소가 없으면 쾅도 없다",
                h2.bang > 100 && h2.woke && none.bang < 1 && !none.woke, $"쾅 {h2.bang:0}dB · {(h2.woke ? "깼다" : "안 깼다")} · 원인: {h2.story} / 수소 없음: 쾅 {none.bang:0} · {(none.woke ? "깼다" : "안 깼다")}");
        }

        // ── 배터리 열폭주: 펑 + 분출 쉬익 ──
        {
            var s = ShipWorld.Create("Didim", 5);
            var n = s.Get<Network>(); var f = s.Get<Field>(); var snd = s.Get<Sound>();
            var bat = n.Devices.First(d => d.Def.Role == "battery");
            long from = snd.Logged;
            f.AddSurfaceHeat(bat.Cell, (460 - f.Tf[bat.Cell]) * Field.SurfaceC);
            s.Run(Time.Minutes(2));
            var bang = snd.LogSince(from).Where(e => e.Kind == SoundKind.Bang && e.Cell == bat.Cell).Select(e => e.Db).DefaultIfEmpty(0).Max();
            var hiss = snd.LogSince(from).Where(e => e.Kind == SoundKind.Hiss && e.Cell == bat.Cell).ToList();
            string story = hiss.Count > 0 ? s.Causes.Story(hiss[0].Cause) : "";
            Check("소리: 배터리 열폭주 — 셀이 터지는 펑(105dB 넘게)과 분출 쉬익 · 원인 사슬이 열폭주",
                bang > 105 && hiss.Count > 0 && story.Contains("열폭주"), $"펑 {bang:0}dB · 쉬익 {hiss.Count}번 · {story}");
        }

        // ── 쉬익 = 분출 물리: 큰 구멍은 크게 · 작은 틈은 작게 (분출 속도의 8제곱 · 넓이) · 문틈 3kPa도 들린다 (예전: 5kPa 넘어야) ──
        {
            float HoleHiss(float area)
            {
                var s = World(false);
                var snd = s.Get<Sound>();
                long from = snd.Logged;
                s.Get<Field>().SetHole(At(24, 3), area);
                s.Run(6);
                return snd.LogSince(from).Where(e => e.Kind == SoundKind.Hiss).Select(e => e.Db).DefaultIfEmpty(0).Max();
            }
            float big = HoleHiss(0.02f), small = HoleHiss(0.0004f);
            float gap, gapX, dp;
            {
                // 조용한 방의 기압을 기계실보다 3kPa 낮춘다 (방마다 처음 기압이 조금씩 다르다 — 그 차이에 맞춰)
                var s = World(false);
                var f = s.Get<Field>(); var snd = s.Get<Sound>();
                double pm = f.Pressure(At(4, 3)), pq = f.Pressure(At(15, 3)), keep = (pm - 3) / pq;
                gapX = Sound.JetDb(Field.LeakNormalDoor, pm, pm - 3);
                foreach (int c in lay.Rooms[quiet].Cells) for (int g = 0; g < f.G; g++) f.RemoveGas(c, g, f.N[c * f.G + g] * (1 - keep));
                long from = snd.Logged;
                s.Run(6);
                var door = snd.LogSince(from).Where(e => e.Kind == SoundKind.Hiss && e.Source == lay.Doors.First(d => lay.Room[d.Cell - 1] == machine || lay.Room[d.Cell + 1] == machine).Cell).ToList();
                gap = door.Select(e => e.Db).DefaultIfEmpty(0).Max();
                dp = (float)(f.Pressure(At(4, 3)) - f.Pressure(At(15, 3)));
                if (Debug) foreach (var k in f.Links) Console.WriteLine($"    틈 {k.Kind} {k.Area * 1e4:0.0}cm² · {f.Pressure(k.A):0.00} / {(k.B >= 0 ? f.Pressure(k.B) : 0):0.00}kPa");
            }
            float bigX = Sound.JetDb(0.02, 101.3, 0), smallX = Sound.JetDb(0.0004, 101.3, 0);
            Check("소리: 쉬익 = 분출 물리 — 200cm² 구멍 105dB 넘게 · 4cm² 틈 88~100dB · 3kPa 문틈도 쉬익(40~50dB) — 엔진이 낸 크기가 분출 식과 같다",
                big > 105 && small is > 88 and < 100 && gap is > 40 and < 50 && Math.Abs(big - bigX) < 2 && Math.Abs(small - smallX) < 2 && Math.Abs(gap - gapX) < 2,
                $"200cm² {big:0}dB (식 {bigX:0}) · 4cm² {small:0} (식 {smallX:0}) · 문틈 3kPa {gap:0} (식 {gapX:0} · 문틈으로 고르게 되어 15초 뒤 {dp:0.0}kPa)");
        }

        // ── 늦게 닿는 소리 (화면): 공기 길은 343m/s — 결정적 순간(화면 1초에 0.5틱)에 먼 외침은 가까운 외침보다 늦게 ──
        {
            var show = new LiveShow(World(false), "시험호");
            show.Advance(1.0 / 60);
            show.Ears.ForceEar = At(3, 3);
            show.Ears.Remap(At(3, 3));
            show.SpeedOverride = 0.5;
            var st = show.Sim.Get<Structure>();
            for (int d = 0; d < lay.Doors.Count; d++) if (lay.Doors[d].Cell == At(5, 6)) st.RequestDoor(d, 1); // 기계실 → 통로 문을 연다
            show.Sim.Run(3);
            show.Ears.Remap(At(3, 3));
            while (show.Ears.Out.TryDequeue(out _)) { }
            var snd = show.Sim.Get<Sound>();
            snd.Emit(show.Sim, At(5, 3), 80, SoundKind.Voice);
            snd.Emit(show.Sim, At(22, 7), 80, SoundKind.Voice);
            show.Advance(1.0 / 60);
            var shots = new List<Voice>();
            while (show.Ears.Out.TryDequeue(out var fr)) shots.AddRange(fr.Shots);
            var near = shots.OrderBy(v => v.Delay).FirstOrDefault(); var far = shots.OrderByDescending(v => v.Delay).FirstOrDefault();
            float nearS = near != null ? near.Delay / (float)Dsp.Rate : -1, farS = far != null ? far.Delay / (float)Dsp.Rate : -1;
            Check("소리(화면): 늦게 닿는 소리 — 결정적 순간(화면 1초에 0.5틱)에 2m 외침은 곧바로 · 문을 돌아 20m 넘게 온 외침은 0.04초 넘게 늦게 (공기 343m/s · 화면 시간으로)",
                shots.Count >= 2 && nearS < 0.01f && farS > 0.04f && farS < 0.5f, $"소리 {shots.Count}개 · 가까운 {nearS * 1000:0}ms · 먼 {farS * 1000:0}ms");
        }

        // ── 낡은 베어링 (화면): 낡은 펌프 소리에는 하우징을 두드리는 딱딱 소리(2~3kHz)가 섞인다 ──
        {
            (double band, double rms) Play(float wear)
            {
                var v = new Machine("pump", 9);
                v.Set(1, wear);
                for (int i = 0; i < Dsp.Rate; i++) v.Next();
                var bp = Biquad.Band(2500, 2);
                double e = 0, all = 0;
                for (int i = 0; i < Dsp.Rate; i++) { float x = v.Next(); float y = bp.Run(x); e += y * y; all += x * x; }
                return (e / Dsp.Rate, Math.Sqrt(all / Dsp.Rate));
            }
            var fresh = Play(0); var worn = Play(12);
            Check("소리(화면): 낡은 베어링 — 낡은 펌프 소리는 2~3kHz 띠(하우징을 두드리는 결함 박자)가 네 배 넘게 세다",
                worn.band > fresh.band * 4, $"띠 세기 새것 {10 * Math.Log10(fresh.band + 1e-12):0}dB · 낡음 {10 * Math.Log10(worn.band + 1e-12):0}dB");
        }
    }
}
