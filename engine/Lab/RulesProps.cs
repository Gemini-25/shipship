using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>물체 규칙 시험 (docs/PHYSICS.md §10 — 묶음 '물체' · '물체 그림' …): 작은 판 · 몇 틱 · 몇 장.</summary>
public static partial class Program
{
    private static void PropRules()
    {
        if (Do("물체"))
        {
            Console.WriteLine("\n[물체 — 문틀 · 사람]");
            DoorFrameRules();
            PropHitRules();
        }
        if (Do("물체 그림"))
        {
            Console.WriteLine("\n[물체 그림]");
            StackRules();
        }
    }

    // 두 방 사이 문 하나 (문 칸은 위아래가 벽 — 동서로 지나고 문짝은 남북으로 닫힌다)
    private const string DoorRooms = "\n###########\n#....#....#\n#....D....#\n#....#....#\n###########";

    /// <summary>
    /// 문틀 (PH4b — docs/PHYSICS.md §3-6 · §10 '문틀'): 닫히는 문짝 판이 물체에서 멈춘다 — 10kg 넘는 것이 물리면 문이 끼고 (원인 '문틀에 끼었다' · 물체는 문틀에 낌) ·
    /// 가벼운 것은 문짝이 밀어내 문이 닫힌다 · 문 칸에 걸친 것도 막는다 · 어느 쪽이든 닫으라는 목표(DoorTarget)는 그대로다 · 바로잡으면 물체를 옆 바닥으로 치운다.
    /// </summary>
    private static void DoorFrameRules()
    {
        var lay = Worlds.Ascii(DoorRooms, Worlds.Seats);
        int door = lay.DoorAt[Worlds.At(lay, 5, 2)], dc = lay.Doors[door].Cell;
        // 열어 둔 문 칸 가운데 (dx: 동서로 비켜 — 걸친 것)에 물건을 두고 닫으라 한다: 1틱 뒤 · 3틱 뒤
        (Sim s, int id, float open1, int held1, bool jam, float open, float target, byte state, int cell, string story) Run(string item, double dx)
        {
            var s = Worlds.Full(lay, 1);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            st.RequestDoor(door, 1); s.Run(3);
            int id = Worlds.PutItem(s, item, dc);
            int i = st.IndexOf(id); var p = st.Props[i]; p.X += dx; st.Props[i] = p;
            st.RequestDoor(door, 0);
            s.Step();
            float open1 = f.DoorOpen[door]; int held1 = st.DoorHeldBy(door);
            s.Run(2);
            int k = st.IndexOf(id);
            string story = s.Facts.Since(0).Where(x => x.Kind == FactKind.DoorJammed).Select(x => s.Causes.Story(x.Cause)).LastOrDefault() ?? "";
            return (s, id, open1, held1, st.DoorJam[door] != 0, f.DoorOpen[door], st.DoorTarget[door], k >= 0 ? st.Props[k].State : (byte)255, k >= 0 ? st.CellOf(st.Props[k]) : -1, story);
        }
        var heavy = Run("식량 상자", 0);
        Check("문틀: 닫히는 문짝에 물린 식량 상자(13kg)가 문을 끼운다 — 문은 반 넘게 열린 채 · 닫으라는 목표는 그대로 (0) · 상자는 문틀에 낌 · 붙든 물체가 그 상자 · 원인 '문틀에 끼었다'",
            heavy.jam && heavy.open >= 0.5f && heavy.target == 0 && heavy.state == 7 && heavy.cell == dc && heavy.held1 == heavy.id && heavy.story.Contains("식량 상자") && heavy.story.Contains("문틀에 끼었다"),
            $"끼임 {heavy.jam} · 열림 {heavy.open:0.00} · 목표 {heavy.target:0.0} · 상태 {heavy.state} · 칸 {(heavy.cell == dc ? "문" : heavy.cell.ToString())} · 붙든 것 {heavy.held1}/{heavy.id} · \"{heavy.story}\"");
        var astride = Run("식량 상자", -0.62);
        Check("문틀: 문 칸에 걸친 식량 상자(가운데는 옆 칸 · 몸이 문 칸에 0.1m 들어옴)도 문짝을 붙들어 문이 낀다 · 목표 그대로",
            astride.jam && astride.open >= 0.5f && astride.target == 0 && astride.held1 == astride.id && astride.cell == dc - 1 && astride.state == 7,
            $"끼임 {astride.jam} · 열림 {astride.open:0.00} · 목표 {astride.target:0.0} · 붙든 것 {astride.held1} · 칸 {astride.cell}/{dc - 1} · 상태 {astride.state}");
        var light = Run("공구 상자", 0);
        Check("문틀: 가벼운 공구 상자(5kg)는 닫히는 문짝이 밀어낸다 — 첫 틱은 상자 앞에서 멈추고 (반쯤 열림) 그 틱에 상자가 문 줄 밖으로 밀려나 (붙든 것 없음) · 다음 틱에 닫힌다 · 끼지 않는다 · 목표 그대로",
            light.open1 > 0.5f && light.open1 < 1 && light.held1 < 0 && !light.jam && light.open == 0 && light.target == 0 && light.cell != dc && light.cell >= 0 && light.state != 7,
            $"첫 틱 열림 {light.open1:0.00} · 붙든 것 {light.held1}/{light.id} · 3틱 뒤 열림 {light.open:0.00} · 끼임 {light.jam} · 칸 {light.cell}/{dc} · 상태 {light.state}");
        // 바로잡기: 끼인 문을 풀고 (FreeDoor) 문틀의 물체를 옆 바닥으로 치우면 (ClearDoorProps) 닫힌다
        {
            var (s, id, _, _, _, _, _, _, _, _) = heavy;
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            st.FreeDoor(door); st.ClearDoorProps(door, s.Tick); st.RequestDoor(door, 0);
            s.Run(2);
            int k = st.IndexOf(id); var p = k >= 0 ? st.Props[k] : default;
            int pc = k >= 0 ? st.CellOf(p) : -1;
            Check("문틀: 끼인 문을 바로잡으면 문틀의 상자를 옆 바닥으로 치우고 (놓임) 문이 닫힌다",
                k >= 0 && pc != dc && pc >= 0 && lay.Kind[pc] == CellKind.Floor && p.State == 0 && f.DoorOpen[door] == 0 && st.DoorJam[door] == 0 && st.DoorHeldBy(door) < 0,
                $"상자 칸 {pc}/{dc} · 상태 {p.State} · 문 {f.DoorOpen[door]:0.00} · 끼임 {st.DoorJam[door]} · 붙든 것 {st.DoorHeldBy(door)}");
        }
    }

    /// <summary>
    /// 날아든 물체에 맞음 (PH4b — docs/PHYSICS.md §7 · §10 '사람'): ⑧이 산 몸을 원판으로 알리고 (SetActors) · ④가 날아든 물체의 맞음을 장부(PropHits)에 적고 ·
    /// ⑦이 5틱마다 읽어 20J 넘으면 타박 (E/800 · 원인은 민 것까지) — 장부는 읽은 뒤 빈다.
    /// </summary>
    private static void PropHitRules()
    {
        var lay = Worlds.Ascii("\n############\n#..........#\n#..........#\n#..........#\n############");
        var s = Worlds.Full(lay, 1);
        var m = s.Get<Mind>(); var st = s.Get<Structure>(); var life = s.Get<Life>();
        int who = m.AddPerson(s, Worlds.At(lay, 8, 2)), body = m[who].C.Body;
        s.Run(2);
        int at = life[body].Cell;
        int id = Worlds.PutItem(s, "식량 상자", at - 3);
        int cause = s.Causes.Add(CauseKind.Setup, "누가 식량 상자를 걷어찼다", s.Tick);
        var p = st.Props[st.IndexOf(id)]; var fm = st.PropForm(p);
        st.Shove(id, fm.Mass * 7, 0, 0, cause);   // 7m/s로 사람 쪽 (3m)
        float blunt0 = life[body].Blunt;
        s.Step();
        var hits = st.PropHits.Where(h => h.Tag == body).ToList();
        double e = hits.Count > 0 ? hits[0].E : 0;
        s.Run(5);
        var hurt = s.Facts.Since(0).Where(x => x.Kind == FactKind.Hurt && x.Actor == body).Select(x => s.Causes.Story(x.Cause)).LastOrDefault() ?? "";
        float got = life[body].Blunt - blunt0;
        Check("사람: 7m/s로 날아든 식량 상자(13kg)에 맞으면 ④ 장부에 한 번 (20J 넘게) → ⑦이 5틱 안에 읽어 타박 (E/800) · 원인은 걷어찬 것까지 · 장부는 빈다",
            hits.Count == 1 && e >= 20 && Math.Abs(got - Math.Min(1.5, e / 800)) < 1e-4 && hurt.Contains("식량 상자") && hurt.Contains("걷어찼다") && !st.PropHits.Any(h => h.Tag == body),
            $"맞음 {hits.Count} · {e:0}J · 타박 {got:0.000}/{Math.Min(1.5, e / 800):0.000} · \"{hurt}\" · 남은 장부 {st.PropHits.Count}");
    }

    /// <summary>
    /// 쌓아 그린 꼴 (PH2): 탁자 · 소화기 · 식량 상자 · 우주복을 각 단 0~63으로 (32 · 16도트 둘 다) — 불투명 도트 수가 단 0의 ±10% 안 · 단 0과 8 (45°)이 다르다 ·
    /// 캐시에 2만 장을 넣어도 32MB 안이고 가장 오래 안 쓴 것부터 나간다.
    /// </summary>
    private static void StackRules()
    {
        var t = Tables.Default;
        static int Opaque(Img im) { int n = 0; for (int i = 3; i < im.Px.Length; i += 4) if (im.Px[i] > 0) n++; return n; }
        var forms = new[] { DotForge.Make(t, "Table", Facing.S, 2, 2), DotForge.Make(t, "소화기"), DotForge.Make(t, "식량 상자"), DotForge.Make(t, "우주복") };
        // 32도트 (scale 1)와 넓은 보기 16도트 (scale 2) 둘 다 — 넓은 보기에서도 돌 때 크기가 줄었다 늘었다 하지 않는다
        foreach (int sc in new[] { 1, 2 })
        {
            var spread = new List<string>(); bool ok = true, differ = true;
            foreach (var f in forms)
            {
                int n0 = Opaque(DotStack.Draw(f, 0, sc).Img), lo = n0, hi = n0;
                for (int k = 1; k < DotStack.Steps; k++) { int n = Opaque(DotStack.Draw(f, k, sc).Img); lo = Math.Min(lo, n); hi = Math.Max(hi, n); }
                ok &= n0 > 0 && lo >= 0.9 * n0 && hi <= 1.1 * n0;
                spread.Add($"{f.Name} {n0} ({(n0 > 0 ? (double)lo / n0 : 0):0.00}~{(n0 > 0 ? (double)hi / n0 : 0):0.00})");
                var a = DotStack.Draw(f, 0, sc).Img; var b = DotStack.Draw(f, 8, sc).Img;
                differ &= a.W != b.W || a.H != b.H || !a.Px.AsSpan().SequenceEqual(b.Px);
            }
            string at = sc == 1 ? "32도트" : "16도트 (넓은 보기)";
            Check($"쌓아 그린 꼴 {at} — 각 단 0~63의 불투명 도트 수가 단 0의 ±10% 안 (탁자 · 소화기 · 식량 상자 · 우주복)", ok, string.Join(" · ", spread));
            Check($"쌓아 그린 꼴 {at} — 단 0과 단 8 (45°)의 그림이 다르다 (넷 모두)", differ && ok);
            // 겉 색: 소화기는 빨간 몸 속에 검은 기둥 (꼭지 + 속 — 바닥부터 꼭대기까지)이 있다 — 어느 단에서도 속 기둥이 앞면을 칠하지 않는다
            static double Red(Img im) { int n = 0, r = 0; for (int i = 0; i < im.Px.Length; i += 4) if (im.Px[i + 3] > 0) { n++; if (im.Px[i] > 2 * im.Px[i + 1] && im.Px[i] > 60) r++; } return n > 0 ? (double)r / n : 0; }
            var ext = forms[1];
            double redLo = Enumerable.Range(0, DotStack.Steps).Min(k => Red(DotStack.Draw(ext, k, sc).Img));
            Check($"쌓아 그린 꼴 {at} — 소화기는 단마다 빨간 도트가 그림의 75% 넘게 (속의 검은 기둥이 앞면을 칠하지 않는다)", redLo > 0.75, $"가장 적은 단 {redLo:0.00}");
        }
        // 캐시: 64×64 그림 (16KB) 2만 장 = 320MB를 넣는다 — 32MB 안 · 오래 안 쓴 것부터 나간다 (가끔 꺼내 본 것은 남는다)
        var cache = new DotStackCache();
        var form = forms[0];
        DotStackCache.Key K(int i) => new(form, 0, 0, 0, (byte)(i & 63), 1, (uint)i);
        long peak = 0; bool keptTouched = true;
        for (int i = 0; i < 20000; i++)
        {
            cache.Put(K(i), new DotStack.Drawn(new Img(64, 64), 32, 48));
            if (i >= 100 && i % 100 == 0) keptTouched &= cache.TryGet(K(7), out _);   // 7번은 자주 꺼내 본다
            peak = Math.Max(peak, cache.Bytes);
        }
        bool newest = cache.TryGet(K(19999), out _) && cache.TryGet(K(19000), out _), oldest = !cache.TryGet(K(0), out _) && !cache.TryGet(K(8), out _) && !cache.TryGet(K(10000), out _);
        Check("쌓은 그림 캐시 — 2만 장 (320MB)을 넣어도 32MB 안 · 오래 안 쓴 것부터 나가고 자주 꺼낸 것 · 새것은 남는다", peak <= 32L << 20 && cache.Count > 1000 && newest && oldest && keptTouched,
            $"가장 많을 때 {peak / 1048576.0:0.0}MB · {cache.Count}장 · 새것 {(newest ? "있음" : "없음")} · 옛것 {(oldest ? "나감" : "남음")} · 자주 본 것 {(keptTouched ? "남음" : "나감")}");
    }
}
