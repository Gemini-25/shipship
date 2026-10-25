using ShipEngine;

namespace Lab;

/// <summary>
/// 함장의 손 법칙 (docs/UI2.md §8 H1 ~ H5 — 묶음 '함장의 손'): 손은 바깥 입력 — 1코어 = 4코어 · 저장 왕복 · 다시보기 (같은 틱에 먹이면 바이트까지 같다) ·
/// 마음 · 사회 · 주컴퓨터 · 생명은 건드리지 않는다 (월드 길만) · 틱의 맨 처음에 적용된다.
/// </summary>
public static partial class Program
{
    /// <summary>손의 과녁 (디딤호에서 찾는다): 침대 칸 · 바깥이 곧게 트인 외판 칸 · 급전선 칸과 그 방 · 센서 선 칸 · 물관 · 냉각관 칸 · 문 칸 · 산소 발생기 칸 · 바닥 물체 Id.</summary>
    internal sealed record HandAim(int Bed, int Hull, int Power, int PowerRoom, int Data, int Water, int Coolant, int Door, int O2Gen, int Prop);

    internal static HandAim HandTargets(Sim s)
    {
        var L = s.Layout; var net = s.Get<Network>(); var f = s.Get<Field>(); var st = s.Get<Structure>();
        int bed = L.Furniture.First(x => x.Type == "Bed").Cells[0];
        int hull = -1;
        for (int c = 0; c < L.N && hull < 0; c++)
        {
            if (L.Kind[c] != CellKind.Wall || !L.Hull[c]) continue;
            var (ux, uy, _) = Hand.RockAim(L, c);
            int x = L.X(c), y = L.Y(c), dx = -(int)Math.Round(ux), dy = -(int)Math.Round(uy);
            bool clear = true;   // 바깥 쪽으로 도면 끝까지 바깥 칸만 (운석이 그 칸에 처음 닿는다)
            for (int k = 1; L.In(x + dx * k, y + dy * k); k++) if (L.Kind[L.Index(x + dx * k, y + dy * k)] != CellKind.Outside) { clear = false; break; }
            bool room = false;   // 안쪽 이웃이 방 (공기가 빠진다)
            int inner = L.Index(x - dx, y - dy);
            if (L.In(x - dx, y - dy) && L.Room[inner] >= 0) room = true;
            if (clear && room) hull = c;
        }
        int power = -1, powerRoom = -1;
        foreach (var fd in net.Feeders)
        {
            foreach (int c in fd.Path) if (L.Room[c] == fd.Room && L.IsSpace(c) && (net.LineAt[c] & Network.Bit(LineKind.Power)) != 0) { power = c; powerRoom = fd.Room; break; }
            if (power >= 0) break;
        }
        int Line(LineKind k) { for (int c = 0; c < L.N; c++) if ((net.LineAt[c] & Network.Bit(k)) != 0 && L.Room[c] >= 0) return c; for (int c = 0; c < L.N; c++) if ((net.LineAt[c] & Network.Bit(k)) != 0) return c; return -1; }
        int data = -1; for (int c = 0; c < L.N; c++) if (net.OnData(c) && L.Room[c] >= 0) { data = c; break; }
        int door = L.Doors.Count > 0 ? L.Doors[0].Cell : -1;
        int o2 = net.Devices.First(d => d.Def.Role == "o2gen").Cell;
        var loose = st.Props.Where(p => p.Kind == 0 && p.State == 0).ToList();   // 놓인 물건 — 깨지지 않는 상자부터 (밀어 던지기 시험 · 유리는 벽에 깨진다)
        int prop = (loose.FirstOrDefault(p => st.PropName(p) == "공구 상자") is { Id: > 0 } box ? box : loose.FirstOrDefault(p => st.PropName(p) is not ("유리병" or "컵" or "접시"))).Id;
        return new HandAim(bed, hull, power, powerRoom, data, Line(LineKind.Water), Line(LineKind.Coolant), door, o2, prop);
    }

    internal static HandOrder Order(HandKind k, int cell, byte size = 1, int obj = 0, float x = 0, float y = 0, byte part = 0) => new() { Kind = k, Cell = cell, Size = size, Obj = obj, X = x, Y = y, Part = part };

    /// <summary>디딤호 · 6명 · 로봇 2 (게임 띄우기와 같은 차림 — 항해에 나선다).</summary>
    internal static Sim HandShip(ulong seed)
    {
        var s = ShipWorld.Crewed("Didim", seed, 6, 2, 0);
        ShipWorld.Embark(s);
        return s;
    }

    private static void HandLaws()
    {
        if (!Do("함장의 손")) return;
        Console.WriteLine("\n[함장의 손 — 1코어 = 4코어 · 저장 왕복 · 다시보기 · 마음을 건드리지 않음 · 틱 처음]");
        var base0 = HandShip(41);
        base0.Run(4);
        var aim = HandTargets(base0);

        // H1: 1코어 = 4코어 — 손 셋 (불씨 · 외판 구멍 · 전선 끊기)을 낀 채 400틱
        {
            byte[] Run(int threads)
            {
                int keep = Par.Threads; Par.Threads = threads;
                try
                {
                    var s = base0.Clone(); var h = s.Get<Hand>();
                    for (int t = 0; t < 240; t++)
                    {
                        if (t == 10) h.Enqueue(Order(HandKind.Fire, aim.Bed, 2));
                        if (t == 70) h.Enqueue(Order(HandKind.Hole, aim.Hull));
                        if (t == 130) h.Enqueue(Order(HandKind.PowerCut, aim.Power));
                        s.Step();
                    }
                    return h.Done.Count(o => o.Cause != 0) == 3 ? s.Save() : Array.Empty<byte>();
                }
                finally { Par.Threads = keep; }
            }
            var one = Run(1); var four = Run(4);
            Check("함장의 손 H1: 1코어 = 4코어 — 디딤호 6명 · 로봇 2에 손 셋 (10틱 불씨 · 70 외판 구멍 · 130 전선 끊기) · 240틱 찍기 바이트가 같다 (손은 한 줄로 · 그 뒤 엔진은 지금처럼)",
                one.Length > 0 && one.AsSpan().SequenceEqual(four), $"손 셋 모두 일어남 {one.Length > 0} · 바이트 {one.Length} · 같음 {one.AsSpan().SequenceEqual(four)}");
        }
        // H2: 저장 왕복 — 기다리는 손이 있는 채 찍고 되살려 둘 다 150틱
        {
            var a = base0.Clone();
            a.Get<Hand>().Enqueue(Order(HandKind.Fire, aim.Bed, 2));
            a.Get<Hand>().Enqueue(Order(HandKind.Hole, aim.Hull));
            var b = a.Clone();
            bool waiting = b.Get<Hand>().Waiting.Count == 2 && b.Get<Hand>().Seq == a.Get<Hand>().Seq;
            long t0 = a.Tick;
            a.Run(150); b.Run(150);
            long Lit(Sim s) => s.Facts.Since(0).Where(x => x.Kind == FactKind.Ignition && x.Tick >= t0).Select(x => x.Tick).DefaultIfEmpty(-1).Min();
            bool same = a.Save().AsSpan().SequenceEqual(b.Save());
            Check("함장의 손 H2: 저장 왕복 — 기다리는 손 둘이 있는 채 찍고 되살려 둘 다 150틱: 찍기 바이트가 같다 · 그 틱에 같은 불붙음 사실 (기다리는 손 · 한 손 장부 · 차례를 찍는다)",
                waiting && same && Lit(a) == t0 && Lit(b) == t0, $"되살린 기다리는 손 {waiting} · 바이트 같음 {same} · 불붙음 {Lit(a)}/{Lit(b)} (손 받은 다음 틱 {t0})");
        }
        // H3: 다시보기 = 산 세계 — 첫 손 앞에서 찍은 것을 되살려 그 틱에 손을 먹이면 같은 틱까지 바이트가 같다 (먹이지 않으면 다르다)
        {
            var live = base0.Clone(); var h = live.Get<Hand>();
            var snap = live.Save(); long t0 = live.Tick;
            for (int t = 0; t < 200; t++)
            {
                if (t == 10) h.Enqueue(Order(HandKind.Fire, aim.Bed, 2));
                if (t == 60) { h.Enqueue(Order(HandKind.Hole, aim.Hull)); h.Enqueue(Order(HandKind.Rock, aim.Hull, 0)); }
                if (t == 120) h.Enqueue(Order(HandKind.DoorJam, aim.Door));
                live.Step();
            }
            byte[] Replay(bool feed)
            {
                var r = new Sim(live.Setup); r.Load(snap);
                var rh = r.Get<Hand>();
                while (r.Tick < live.Tick)
                {
                    if (feed) foreach (var o in h.Done) if (o.Tick == r.Tick && o.Tick >= t0) rh.Enqueue(o);   // 같은 틱 · 같은 차례 (Seq는 되살린 세계가 다시 매긴다 — 같아진다)
                    r.Step();
                }
                return r.Save();
            }
            var fed = Replay(true); var bare = Replay(false); var want = live.Save();
            Check("함장의 손 H3: 다시보기 = 산 세계 — 첫 손 앞에서 찍은 것을 되살려 한 손 장부의 손을 그 틱에 먹이면 200틱 뒤 찍기 바이트가 같다 (같은 틱 · 같은 차례) · 먹이지 않으면 다르다",
                fed.AsSpan().SequenceEqual(want) && !bare.AsSpan().SequenceEqual(want), $"먹임 같음 {fed.AsSpan().SequenceEqual(want)} · 먹이지 않음 다름 {!bare.AsSpan().SequenceEqual(want)} · 한 손 {h.Done.Count}");
        }
        // H4: 마음을 건드리지 않는다 — 갈래마다 손의 걸음만 부른 뒤 마음 · 사회 · 주컴퓨터 · 생명의 찍기 조각이 쌍둥이와 같다
        {
            byte[] Bytes(IState e) { var w = new StateWriter(1 << 16); e.Save(w); return w.ToArray(); }
            var bad = new List<string>(); int applied = 0;
            var orders = new[]
            {
                Order(HandKind.Fire, aim.Bed), Order(HandKind.Hole, aim.Hull), Order(HandKind.PowerCut, aim.Power), Order(HandKind.DataCut, aim.Data),
                Order(HandKind.Fault, aim.O2Gen, part: (byte)ShipEngine.Part.Seal), Order(HandKind.Gravity, -1), Order(HandKind.WaterLeak, aim.Water), Order(HandKind.CoolantLeak, aim.Coolant, 2),
                Order(HandKind.DoorJam, aim.Door), Order(HandKind.O2Drift, -1), Order(HandKind.Blast, aim.Bed, 2), Order(HandKind.Shove, -1, obj: aim.Prop, x: 6, y: 0),
                Order(HandKind.Rock, aim.Hull, 2), Order(HandKind.Cloud, aim.Hull), Order(HandKind.Flare, -1), Order(HandKind.Route, -1, obj: 8),
            };
            foreach (var o in orders)
            {
                var s = base0.Clone(); var twin = base0.Clone();
                var h = s.Get<Hand>();
                h.Enqueue(o); h.Step(s, Time.SecondsPerTick);
                if (h.Done.Count == 1 && h.Done[0].Cause != 0) applied++; else bad.Add($"{o.Kind} 헛손 '{Hand.Why(s, o)}'");
                foreach (var (name, a, b) in new (string, IState, IState)[] { ("마음", s.Get<Mind>(), twin.Get<Mind>()), ("사회", s.Get<Society>(), twin.Get<Society>()), ("주컴퓨터", s.Get<ShipMind>(), twin.Get<ShipMind>()), ("생명", s.Get<Life>(), twin.Get<Life>()) })
                    if (!Bytes(a).AsSpan().SequenceEqual(Bytes(b))) bad.Add($"{o.Kind} → {name}");
            }
            Check("함장의 손 H4: 마음을 건드리지 않는다 — 열여섯 갈래 (불씨 · 구멍 · 전선 · 센서 선 · 고장 · 중력 · 물관 · 냉각관 · 문 끼임 · 센서 어긋남 · 폭발 · 밀기 · 운석 · 구름 · 별 폭발 · 항로)마다 손의 걸음만 부른 뒤 마음 · 사회 · 주컴퓨터 · 생명의 찍기가 쌍둥이와 같다 (월드 길만 — 그들은 센서 · 본 것으로만 안다)",
                bad.Count == 0 && applied == orders.Length, $"일어남 {applied}/{orders.Length} · 어긋남 {bad.Count} {string.Join(" | ", bad.Take(4))}");
        }
        // H5: 틱 처음 — 받아 둔 불씨는 다음 틱에 불붙음 사실 (원인 사슬 뿌리 갈래 '함장') · 운석은 같은 틱 ⑤ 걸음에 들어 있다
        {
            var s = base0.Clone(); var h = s.Get<Hand>(); var outside = s.Get<Outside>();
            h.Enqueue(Order(HandKind.Fire, aim.Bed, 2));
            h.Enqueue(Order(HandKind.Rock, aim.Hull, 1));
            long t = s.Tick;
            s.Step();
            var lit = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Ignition && x.Tick == t);
            int root = lit.Cause != 0 ? s.Causes.Root(lit.Cause) : 0;
            var rockCause = h.Done.Count == 2 ? h.Done[1].Cause : 0;
            var rock = outside.Rocks.FirstOrDefault(r => r.Cause == rockCause);
            double dist = Math.Sqrt(rock.X * rock.X + rock.Y * rock.Y);
            bool moved = rockCause != 0 && Math.Abs(dist - (Hand.RockDistance - Hand.RockSpeed * Time.SecondsPerTick)) < 50;
            Check("함장의 손 H5: 틱 처음 — 받아 둔 불씨는 다음 틱 (받은 틱 그대로)에 불붙음 사실 · 원인 사슬의 뿌리 갈래 '함장' · 글 '함장이 일으킨 불씨' / 운석은 같은 틱 ⑤ 바깥 걸음에서 한 틱만큼 다가왔다 (손이 ⑤보다 먼저)",
                lit.Kind == FactKind.Ignition && root != 0 && s.Causes[root].Kind == CauseKind.Captain && s.Causes.Text(root).StartsWith("함장이 일으킨 불씨") && moved,
                $"불붙음 틱 {(lit.Kind == FactKind.Ignition ? lit.Tick : -1)} (받은 틱 {t}) · 뿌리 '{(root != 0 ? s.Causes.Text(root) : "")}' ({(root != 0 ? s.Causes[root].Kind : default)}) · 운석 거리 {dist / 1000:0.0}km (처음 {Hand.RockDistance / 1000:0}km)");
        }
    }
}
