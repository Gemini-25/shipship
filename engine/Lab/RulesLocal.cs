using ShipEngine;

namespace Lab;

/// <summary>③ 국소 현상 규칙 시험 — 연기층 · 몸을 낮춤 · 구멍으로 끌려감 · 붙잡기 · 빨려 붙음 · 상자가 막음 (docs/ENGINE.md ③ '국소 현상').</summary>
public static partial class Program
{
    /// <summary>왼쪽 화물칸(외판 x=0 · 의자 하나) · 문 D · 오른쪽 통로.</summary>
    private const string LocalRooms = @"
###############
#a....#.......#
#.....D.......#
#.....#.......#
###############";

    private static void LocalRules()
    {
        if (!Do("국소 현상")) return;
        Console.WriteLine("\n[③ 국소 현상]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var t = Tables.Default;

        // ── 불 난 방의 연기층: 사람 없이 불만 — 불 곁 칸 머리 높이(선 이)의 일산화탄소가 바닥 높이(기어가는 이)의 세 배 넘게 · 위 공기가 아래보다 30K 넘게 뜨겁다 ──
        {
            var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            var s = Worlds.WithChem(lay, 1);
            var f = s.Get<Field>();
            int fire = Worlds.At(lay, 15, 1), probe = Worlds.At(lay, 15, 2);
            s.Get<Chemistry>().Ignite(fire, 3e6, s.Causes.Add(CauseKind.Setup, "의자 밑 불씨", s.Tick, fire));
            double coUp = 0, coLow = 0, tUp = 0, tLow = 0;
            for (int k = 0; k < Time.Minutes(4); k++)
            {
                s.Step();
                coUp = Math.Max(coUp, f.HeadPpm(t.gCO, probe, false)); coLow = Math.Max(coLow, f.HeadPpm(t.gCO, probe, true));
                tUp = Math.Max(tUp, f.HeadTemp(probe, false)); tLow = Math.Max(tLow, f.HeadTemp(probe, true));
            }
            Check("불 난 방의 연기층: 불 곁 칸 머리 높이(선 이)의 일산화탄소가 바닥 높이(기어가는 이)의 세 배 넘게 · 위 공기가 아래보다 30K 넘게 뜨겁다",
                coUp > 3 * Math.Max(1, coLow) && tUp > tLow + 30,
                $"일산화탄소 위 {coUp:0}ppm · 아래 {coLow:0}ppm · 온도 위 {tUp - 273.15:0}℃ · 아래 {tLow - 273.15:0}℃");
        }

        // ── 몸을 낮춘다: 깨어 있는 이는 머리 높이에 연기 · 열기가 차면 몸을 낮춰 (기어가는 높이) 아래 층을 마신다 ──
        {
            var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 4);
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, At(15, 2));
            s.Run(3);
            var room = Enumerable.Range(0, lay.N).Where(c => lay.Room[c] == lay.Room[At(15, 2)] && lay.Kind[c] == CellKind.Floor).ToList();
            bool lowBefore = life[m[p].C.Body].Low != 0, crouched = false;
            for (int k = 0; k < 30 && !crouched; k++)
            {
                foreach (int c in room) { double want = f.Ts[c] + 80, now = f.Temp(c); if (now < want) f.AddHeat(c, (want - now) * f.GasC(c)); }   // 불이 데운 연기 (바닥보다 80K)
                s.Step();
                if (m[p].C.Up != 0 && life[m[p].C.Body].Low != 0) crouched = true;
            }
            Check("몸을 낮춘다: 깨어 있는 이는 머리 높이 공기가 50℃를 넘으면 몸을 낮춰 기어가는 높이(아래 층)로 숨 쉰다 — 조용할 땐 서 있다",
                !lowBefore && crouched,
                $"처음 낮음 {lowBefore} · 낮춤 {crouched} · 머리 높이 {f.HeadTemp(m[p].C.Cell, false) - 273.15:0}℃ · 바닥 높이 {f.HeadTemp(m[p].C.Cell, true) - 273.15:0}℃");
        }

        // ── 소리 없는 일산화탄소층: 바닥보다 조금 뜨거운 방 위에 고인 일산화탄소 — 깨어 앉은 이(위 층)의 COHb가 자는 이(아래 층)보다 훨씬 높다 ──
        {
            var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 3);
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>();
            int awake = m.AddPerson(s, At(15, 2)), sleeper = m.AddPerson(s, At(16, 2));
            s.Run(3);
            life[m[sleeper].C.Body].Asleep = 1; m[sleeper].C.Sleepy = 1;
            var room = Enumerable.Range(0, lay.N).Where(c => lay.Room[c] == lay.Room[At(15, 2)] && lay.Kind[c] == CellKind.Floor).ToList();
            foreach (int c in room) f.AddGas(c, t.gCO, f.Moles(c) * 0.0015, f.Temp(c));   // 1500ppm
            for (int k = 0; k < Time.Minutes(10); k++)
            {
                foreach (int c in room) { double want = f.Ts[c] + 15, now = f.Temp(c); if (now < want) f.AddHeat(c, (want - now) * f.GasC(c)); }   // 바닥보다 15K (뜨거운 기계 곁 공기)
                s.Step();
                life[m[sleeper].C.Body].Asleep = 1;
            }
            float ca = life[m[awake].C.Body].COHb, cs = life[m[sleeper].C.Body].COHb;
            Check("소리 없는 일산화탄소층: 바닥보다 15K 뜨거운 방 위에 고인 일산화탄소 1500ppm — 깨어 앉은 이(위 층)의 COHb가 자는 이(아래 층)의 세 배 넘게",
                ca > 3 * Math.Max(0.002f, cs) && ca > 0.02f,
                $"깨어 앉은 이 COHb {ca * 100:0.0}% · 자는 이 {cs * 100:0.0}% · 층 {f.Strat[At(15, 2)]:0.00} · 위 {f.HeadPpm(t.gCO, At(15, 2), false):0}ppm · 아래 {f.HeadPpm(t.gCO, At(15, 2), true):0}ppm");
        }

        // ── 외판 구멍과 사람: 구멍 곁 칸의 이는 구멍 입으로 모이는 흐름에 끌린다 (구멍이 나는 그 순간 — 장이 흐르기 전) ──
        {
            var bare = Worlds.Ascii(LocalRooms.Replace("#a....#", "#.....#").Replace("#.....D", "#.....P"), Worlds.Seats);   // 차압 문 — 옆 방 공기가 문틈으로 새어 들지 않는다
            var seat = Worlds.Ascii(LocalRooms, Worlds.Seats);
            (Sim s, Mind m, Field f, int[] ps) Breach(Layout lay, float area, params (int x, int y)[] who)
            {
                int At(int x, int y) => Worlds.At(lay, x, y);
                var s = Worlds.Full(lay, 1);
                var m = s.Get<Mind>(); var st = s.Get<Structure>(); var f = s.Get<Field>();
                st.RequestDoor(lay.DoorAt[At(6, 2)], 0);
                s.Run(3);
                var ps = who.Select(w => m.AddPerson(s, At(w.x, w.y))).ToArray();
                int cause = s.Causes.Add(CauseKind.Setup, "화물칸 외판에 파편이 박혔다", s.Tick);
                st.DamageCause[At(0, 2)] = cause; f.SetHole(At(0, 2), area);
                return (s, m, f, ps);
            }
            // (가) 1m²: 구멍 곁 이는 우주로 빨려 나가고 · 두 칸 떨어진 이는 남는다
            {
                var (s, m, f, ps) = Breach(bare, 1.0f, (1, 2), (3, 2));
                s.Run(20);
                var out1 = m[ps[0]].C; var far = m[ps[1]].C;
                string story = s.Facts.Since(0).Where(x => x.Kind == FactKind.WentOutside && x.Actor == ps[0]).Select(x => s.Causes.Story(x.Cause)).FirstOrDefault() ?? "";
                Check("외판 1m² 구멍: 구멍 곁 이는 구멍 입으로 모이는 흐름에 끌려 우주로 빨려 나간다 (원인 사슬: 파편 → 빨려 나갔다) · 두 칸 떨어진 이는 화물칸에 남는다",
                    out1.Adrift != 0 && story.Contains("파편") && story.Contains("빨려 나갔다") && far.Adrift == 0 && far.PinAt == 0,
                    $"곁의 이 빨려 나감 {out1.Adrift} \"{story}\" / 떨어진 이 떠감 {far.Adrift} @{bare.X(far.Cell)},{bare.Y(far.Cell)}");
            }
            // (나) 0.075m²: 곁에 붙잡을 것이 없으면 구멍에 빨려 붙어 막는다 — 방이 반으로 비는 데 다섯 배 넘게 · 멍 · 기압이 빠지면 뗀다 / 의자 곁이면 붙잡고 버틴다
            {
                double Half(bool person)
                {
                    var (s, m, f, ps) = person ? Breach(bare, 0.075f, (1, 2)) : Breach(bare, 0.075f);
                    int c = Worlds.At(bare, 3, 2);
                    for (int k = 0; k < Time.Minutes(10); k++) { s.Step(); if (f.Pressure(c) < 50) return (k + 1) * Time.SecondsPerTick; }
                    return Time.Minutes(10) * Time.SecondsPerTick;
                }
                var (s2, m2, f2, ps2) = Breach(bare, 0.075f, (1, 2));
                bool pinned = false, freed = false;
                for (int k = 0; k < Time.Minutes(10); k++)
                {
                    s2.Step();
                    if (m2[ps2[0]].C.PinAt != 0) pinned = true;
                    else if (pinned) freed = true;
                }
                float blunt = s2.Get<Life>()[m2[ps2[0]].C.Body].Blunt;
                double open = Half(false), plugged = Half(true);
                var (s3, m3, f3, ps3) = Breach(seat, 0.075f, (1, 2));
                s3.Run(5);
                bool held = m3[ps3[0]].C.PinAt == 0 && m3[ps3[0]].C.Adrift == 0 && m3.HoldsHeld >= 1;
                Check("외판 0.075m² 구멍: 곁의 이가 구멍에 빨려 붙어 막는다 — 방이 반으로 비는 데 막지 않았을 때의 다섯 배 넘게 · 빨린 자리에 멍 · 기압이 빠지면 몸을 뗀다 / 의자 곁이면 붙잡고 버틴다",
                    pinned && plugged > 5 * open && blunt > 0.05f && freed && held,
                    $"빨려 붙음 {pinned} · 반으로 비는 데 — 열림 {open:0}초 · 몸이 막음 {plugged:0}초 · 타박 {blunt:0.00} · 뗌 {freed} / 의자 곁: 붙잡음 {m3.HoldsHeld} · 빨려 붙음 {m3[ps3[0]].C.PinAt != 0}");
            }
        }

        // ── 물건이 구멍을 막는다: 외판 0.06m² 구멍 곁 식량 상자(13kg · 한 면 0.09m²)는 빨려 붙어 막고 · 수리 꾸러미(4kg · 0.04m²)는 빠져나간다 ──
        {
            var lay = Worlds.Ascii(LocalRooms, Worlds.Seats);
            int At(int x, int y) => Worlds.At(lay, x, y);
            (double kPa, bool plugged, bool gone, string story) Run(string item)
            {
                var s = Worlds.Full(lay, 1);
                var m = s.Get<Mind>(); var st = s.Get<Structure>(); var f = s.Get<Field>();
                byte it = (byte)m.ItemIndex(item);
                m.Loose.Add(new LooseItem { Item = it, Cell = At(1, 2) });
                st.RequestDoor(lay.DoorAt[At(6, 2)], 0);
                s.Run(10);
                int cause = s.Causes.Add(CauseKind.Setup, "화물칸 외판에 작은 파편이 박혔다", s.Tick);
                st.DamageCause[At(0, 2)] = cause; f.SetHole(At(0, 2), 0.06f);
                s.Run(Time.Minutes(1));
                bool gone = !m.Loose.Any(x => x.Item == it);
                bool plugged = m.Loose.Any(x => x.Item == it && x.PlugAt != 0);
                string story = s.Facts.Since(0).Where(x => x.Kind == FactKind.ObjectFlung).Select(x => s.Causes.Story(x.Cause)).LastOrDefault() ?? "";
                return (f.Pressure(At(3, 2)), plugged, gone, story);
            }
            var crate = Run("식량 상자"); var kit = Run("수리 꾸러미");
            Check("물건이 구멍을 막는다: 외판 0.06m² 구멍 곁 — 식량 상자(한 면 0.09m²)는 빨려 붙어 막아 1분 뒤 화물칸 기압이 꾸러미 때의 다섯 배 넘게 남고 · 수리 꾸러미(0.04m²)는 구멍 밖으로 나가 방이 빈다",
                crate.plugged && crate.kPa > 15 && crate.kPa > 5 * kit.kPa && kit.gone && kit.kPa < 5 && crate.story.Contains("빨려 붙었다"),
                $"상자: 막음 {crate.plugged} · {crate.kPa:0}kPa · \"{crate.story}\" / 꾸러미: 나감 {kit.gone} · {kit.kPa:0}kPa");
        }
    }
}
