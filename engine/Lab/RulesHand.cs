using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>
/// 함장의 손 규칙 (docs/UI2.md §8 R1 ~ R7 — 묶음 '함장의 손'): 손이 이미 있는 월드 길을 부르고 · 사람 · 로봇 · 주컴퓨터는 센서 · 본 것으로만 안다 ·
/// 원인 사슬의 뿌리는 '함장' · 막음 (가득 · 판정 글 · 헛손). 디딤호 (6명 · 로봇 2 · 항해 중)에서 갈래마다 몇 분.
/// </summary>
public static partial class Program
{
    private static void HandRules()
    {
        if (!Do("함장의 손")) return;
        Console.WriteLine("\n[함장의 손 — 불씨 · 구멍 · 운석 · 선 · 고장 · 중력 · 샘 · 끼임 · 폭발 · 밀기 · 구름 · 별 폭발 · 막음]");
        var base0 = HandShip(43);
        base0.Run(30);
        var aim = HandTargets(base0);
        bool FromCaptain(Sim s, int cause) => cause != 0 && s.Causes[s.Causes.Root(cause)].Kind == CauseKind.Captain;
        Fact FirstFact(Sim s, FactKind k, long from) => s.Facts.Since(0).FirstOrDefault(x => x.Kind == k && x.Tick >= from && FromCaptain(s, x.Cause));
        Incident? Known(Sim s, IncidentKind k, int room) { foreach (var x in s.Get<ShipMind>().Incidents) if (x.Active != 0 && x.Kind == k && (room < 0 || x.Room == room)) return x; return null; }

        // R1: 불씨 → 주컴퓨터는 센서를 읽은 뒤에 '불' (같은 틱이 아니다) · 로봇을 보낸다 · 원인 사슬 → 함장
        {
            var s = base0.Clone(); var sm = s.Get<ShipMind>();
            int room = s.Layout.Room[aim.Bed];
            long t0 = s.Tick;
            s.Get<Hand>().Enqueue(Order(HandKind.Fire, aim.Bed, 2));
            long known = -1; bool robot = false;
            for (int k = 0; k < 150 && !robot; k++)
            {
                s.Step();
                var x = Known(s, IncidentKind.Fire, room);
                if (x != null && known < 0) known = s.Tick;
                if (x is { } xi && (xi.RobotA >= 0 || xi.RobotB >= 0)) robot = true;
            }
            var lit = FirstFact(s, FactKind.Ignition, t0);
            Check("함장의 손 R1: 불씨 (침대 · 크게) → 불붙음 원인 사슬의 뿌리는 함장 · 주컴퓨터는 센서를 읽은 뒤에야 '불' 사건 (불붙은 틱이 아니다 — 아는 것으로만) · 로봇을 보낸다",
                lit.Kind == FactKind.Ignition && known > lit.Tick + 1 && robot, $"불붙음 틱 {(lit.Kind == FactKind.Ignition ? lit.Tick : -1)} · 주컴퓨터가 안 틱 {known} · 로봇 {robot}");
        }
        // R2: 외판 구멍 0.02㎡ → 벽 뚫림 · 기압이 준다 · 주컴퓨터가 안다 / 운석 0.5kg → 레이더가 잡는다 · 5g → 잡히지 않고 그 칸에 맞는다
        {
            var s = base0.Clone(); var f = s.Get<Field>(); var L = s.Layout;
            var (ux, uy, _) = Hand.RockAim(L, aim.Hull);
            int inner = L.Index(L.X(aim.Hull) + (int)Math.Round(ux), L.Y(aim.Hull) + (int)Math.Round(uy)), room = L.Room[inner];
            double p0 = f.Pressure(inner);
            long t0 = s.Tick;
            s.Get<Hand>().Enqueue(Order(HandKind.Hole, aim.Hull, 1));
            bool knew = false;
            for (int k = 0; k < 40 && !knew; k++) { s.Step(); knew = Known(s, IncidentKind.Leak, room) != null || Known(s, IncidentKind.Vacuum, room) != null; }
            var breach = FirstFact(s, FactKind.WallBreach, t0);
            double p1 = f.Pressure(inner);
            Check("함장의 손 R2: 외판 구멍 (보통 0.02㎡) → 벽 뚫림 사실 (뿌리 함장 · 그 칸) · 그 방 기압이 준다 · 주컴퓨터가 기압 센서로 샘 · 빈 방 사건을 붙잡는다",
                breach.Kind == FactKind.WallBreach && breach.Cell == aim.Hull && p1 < p0 - 1 && knew, $"벽 뚫림 {breach.Kind == FactKind.WallBreach} (칸 {breach.Cell}/{aim.Hull}) · 기압 {p0:0.0} → {p1:0.0}kPa · 주컴퓨터 {knew}");

            (bool seen, int hitCell, long hits) Rock(byte size)
            {
                var r = base0.Clone(); var sm = r.Get<ShipMind>(); var o = r.Get<Outside>();
                long seen0 = sm.RocksSeen, hits0 = o.RockHits, from = r.Tick;
                r.Get<Hand>().Enqueue(Order(HandKind.Rock, aim.Hull, size));
                for (int k = 0; k < 45; k++) r.Step();
                var imp = FirstFact(r, FactKind.Impact, from);
                return (sm.RocksSeen > seen0, imp.Kind == FactKind.Impact ? imp.Cell : -1, o.RockHits - hits0);
            }
            var big = Rock(2); var small = Rock(0);
            Check("함장의 손 R2: 운석 — 0.5kg은 레이더가 잡는다 (주컴퓨터가 안다) · 5g은 잡히지 않고 겨눈 외판 칸에 그대로 맞는다 (오는 쪽 · 비낀 거리를 ⑤ 틀대로 셈)",
                big.seen && !small.seen && small.hits == 1 && small.hitCell == aim.Hull, $"0.5kg 잡힘 {big.seen} (맞음 {big.hits}) · 5g 잡힘 {small.seen} · 맞음 {small.hits} · 맞은 칸 {small.hitCell}/{aim.Hull}");
        }
        // R3: 전선 끊기 → 그 방에 전기가 끊겨 센서가 꺼진다 ('못 보는 방') / 센서 선 끊기 → 주컴퓨터가 센서를 잃고 로봇이 잇는다
        {
            var s = base0.Clone(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
            long t0 = s.Tick;
            s.Get<Hand>().Enqueue(Order(HandKind.PowerCut, aim.Power));
            for (int k = 0; k < 10; k++) s.Step();
            var cut = FirstFact(s, FactKind.CableCut, t0);
            bool dark = !net.RoomPowered(aim.PowerRoom), blind = sm.RoomSeen[aim.PowerRoom] == 0;
            Check("함장의 손 R3: 전선 끊기 → 전선 끊김 사실 (뿌리 함장) · 그 방에 전기가 끊긴다 · 그 방 센서가 꺼져 주컴퓨터가 못 본다",
                cut.Kind == FactKind.CableCut && dark && blind, $"끊김 {cut.Kind == FactKind.CableCut} · 전기 끊김 {dark} · 못 봄 {blind} ({s.Layout.Rooms[aim.PowerRoom].Name})");

            var d = base0.Clone(); var dn = d.Get<Network>();
            long splices0 = dn.DataSplices;
            d.Get<Hand>().Enqueue(Order(HandKind.DataCut, aim.Data));
            bool lost = false;
            for (int k = 0; k < 600 && dn.DataSplices == splices0; k++) { d.Step(); lost |= Known(d, IncidentKind.SensorLost, -1) != null; }
            Check("함장의 손 R3: 센서 선 끊기 → 주컴퓨터가 '센서가 끊겼다' 사건 · 로봇이 가서 잇는다 (원인 사슬 → 함장)",
                lost && dn.DataSplices > splices0 && !dn.DataCutAt(aim.Data), $"센서 잃음 {lost} · 이음 {dn.DataSplices - splices0} · {Time.Clock(d.Tick)}");
        }
        // R4: 설비 고장 (산소 발생기 봉함) → 1분 안에 고장 (뿌리 함장) / 인공 중력 발생기 → 중력이 사라진다
        {
            var s = base0.Clone(); var net = s.Get<Network>();
            int dev = Hand.DeviceAt(net, s.Layout, aim.O2Gen);
            long t0 = s.Tick;
            s.Get<Hand>().Enqueue(Order(HandKind.Fault, aim.O2Gen, part: (byte)ShipEngine.Part.Seal));
            for (int k = 0; k < 26 && !net.IsBroken(dev, ShipEngine.Part.Seal); k++) s.Step();
            var fail = FirstFact(s, FactKind.MachineFail, t0);
            Check("함장의 손 R4: 설비 고장 (산소 발생기 봉함) → 1분 안에 고장 사실 (원인 사슬 → 함장 · 숨은 한계를 넘긴 낡음이 고장을 낸다)",
                net.IsBroken(dev, ShipEngine.Part.Seal) && fail.Kind == FactKind.MachineFail && s.Tick - t0 <= Time.Minutes(1) + 1, $"고장 {net.IsBroken(dev, ShipEngine.Part.Seal)} · 사실 {fail.Kind == FactKind.MachineFail} · {(s.Tick - t0) * Time.SecondsPerTick:0}초");

            var g = base0.Clone(); var gf = g.Get<Field>();
            int floor = aim.Bed;
            float g0 = gf.Grav[floor];
            g.Get<Hand>().Enqueue(Order(HandKind.Gravity, -1));
            for (int k = 0; k < 60 && gf.Grav[floor] > 0.1f; k++) g.Step();
            Check("함장의 손 R4: 인공 중력 발생기 고장 → 바닥 중력이 사라진다 (발생기의 장이 무너진다)", g0 > 0.5f && gf.Grav[floor] <= 0.1f, $"중력 {g0:0.00} → {gf.Grav[floor]:0.00}g · {Time.Clock(g.Tick)}");
        }
        // R5: 물관 샘 (이음매 · 모르는 샘) → 바닥 물의 원인이 함장 / 냉각관 터짐 → 암모니아
        {
            var s = base0.Clone(); var f = s.Get<Field>();
            s.Get<Hand>().Enqueue(Order(HandKind.WaterLeak, aim.Water, 1));
            int wet = -1;
            for (int k = 0; k < 200 && wet < 0; k++)
            {
                s.Step();
                for (int c = 0; c < s.Layout.N; c++) if (f.Stored(f.T.sWater, c) > 0.5 && FromCaptain(s, f.WetCause[c])) { wet = c; break; }
            }
            Check("함장의 손 R5: 물관 샘 (이음매 · 아무도 모르는 샘) → 바닥에 고인 물의 원인 사슬이 함장 ('함장이 일으킨 물관 샘')",
                wet >= 0, wet >= 0 ? $"젖은 칸 {wet} · '{s.Causes.Story(f.WetCause[wet])}' · {Time.Clock(s.Tick)}" : "젖은 칸 없음");

            var c2 = base0.Clone(); var f2 = c2.Get<Field>();
            c2.Get<Hand>().Enqueue(Order(HandKind.CoolantLeak, aim.Coolant, 2));
            double nh3 = 0;
            for (int k = 0; k < 40 && nh3 < 0.01; k++) { c2.Step(); for (int c = 0; c < c2.Layout.N; c++) if (c2.Layout.IsSpace(c)) nh3 = Math.Max(nh3, f2.Partial(f2.T.gNH3, c)); }
            Check("함장의 손 R5: 냉각관 터짐 → 암모니아가 공기에 섞인다", nh3 >= 0.01, $"암모니아 가장 짙은 칸 {nh3:0.000}kPa");
        }
        // R6: 문 끼임 · 폭발 · 밀어 던지기 (민 원인 = 함장) · 파편 구름 → 대피 방송 · 별 폭발 → 조짐 → 대피
        {
            var s = base0.Clone(); var st = s.Get<Structure>(); var h = s.Get<Hand>();
            int door = s.Layout.DoorAt[aim.Door];
            long t0 = s.Tick, blasts0 = st.Blasts;
            h.Enqueue(Order(HandKind.DoorJam, aim.Door));
            h.Enqueue(Order(HandKind.Blast, aim.Bed, 0));
            s.Step();
            var jam = FirstFact(s, FactKind.DoorJammed, t0);
            bool jammed = st.DoorJam[door] != 0, blasted = st.Blasts == blasts0 + 1;
            s = base0.Clone(); st = s.Get<Structure>();
            int pi = st.IndexOf(aim.Prop);
            double x0 = st.Props[pi].X, y0 = st.Props[pi].Y;   // 민 틱 앞의 자리 (8m/s면 한 틱에 벽까지 간다)
            s.Get<Hand>().Enqueue(Order(HandKind.Shove, -1, obj: aim.Prop, x: 0, y: 8));
            for (int k = 0; k < 6; k++) s.Step();
            pi = st.IndexOf(aim.Prop);
            var p1 = pi >= 0 ? st.Props[pi] : default;
            double moved = Math.Sqrt((p1.X - x0) * (p1.X - x0) + (p1.Y - y0) * (p1.Y - y0));
            Check("함장의 손 R6: 문 끼임 (끼인 문 · 사실 뿌리 함장) · 폭발 (폭풍 하나) · 밀어 던지기 8m/s (물건이 날아가 · 민 원인이 함장)",
                jammed && jam.Kind == FactKind.DoorJammed && blasted && moved > 0.5 && FromCaptain(s, p1.Cause),
                $"끼임 {jammed} · 폭발 {blasted} · {st.PropName(p1)} 날아간 거리 {moved:0.0}m · 민 원인 '{(p1.Cause != 0 ? s.Causes.Text(s.Causes.Root(p1.Cause)) : "")}'");

            (long shelters, long tick) Shelter(HandKind k)
            {
                var w = base0.Clone(); var sm = w.Get<ShipMind>();
                long s0 = sm.Shelters;
                w.Get<Hand>().Enqueue(Order(k, aim.Hull));
                for (int i = 0; i < 120 && sm.Shelters == s0; i++) w.Step();
                return (sm.Shelters - s0, w.Tick);
            }
            var cloud = Shelter(HandKind.Cloud); var flare = Shelter(HandKind.Flare);
            Check("함장의 손 R6: 파편 구름 (14분 앞 — 센서 안) → 주컴퓨터가 대피를 알린다 / 별 폭발 → X선 조짐 → 대피",
                cloud.shelters > 0 && flare.shelters > 0, $"구름 대피 {cloud.shelters} ({Time.Clock(cloud.tick)}) · 별 폭발 대피 {flare.shelters} ({Time.Clock(flare.tick)})");
        }
        // R7: 막음 — 기다리는 손 여덟이면 아홉째는 받지 않는다 · 판정 글 (외판이 아닌 칸 · 이미 뚫림) · 헛손 (받은 뒤 대상이 사라짐 — 원인 마디 없이 장부에만)
        {
            var s = base0.Clone(); var h = s.Get<Hand>(); var st = s.Get<Structure>();
            int taken = 0;
            for (int k = 0; k < 9; k++) if (h.Enqueue(Order(HandKind.Flare, -1))) taken++;
            h.Waiting.Clear();
            string notHull = Hand.Why(s, Order(HandKind.Hole, aim.Bed));
            s.Get<Hand>().Enqueue(Order(HandKind.Hole, aim.Hull, 2)); s.Step();
            string again = Hand.Why(s, Order(HandKind.Hole, aim.Hull, 1));
            int causes0 = s.Causes.Count;
            h.Enqueue(Order(HandKind.Shove, -1, obj: aim.Prop, x: 3));
            int pi = st.IndexOf(aim.Prop);
            var p = st.Props[pi]; p.State = 6; st.Props[pi] = p;   // 그사이 우주로 (시험 차림)
            h.Step(s, Time.SecondsPerTick);
            var last = h.Done[^1];
            Check("함장의 손 R7: 막음 — 기다리는 손 여덟 (아홉째 거절) · '외판 칸을 누른다' · '이미 뚫렸다' · 헛손 (받은 뒤 물건이 우주로 — 원인 마디 없이 한 손 장부에만)",
                taken == Hand.MaxWaiting && notHull == "외판 칸을 누른다" && again == "이미 뚫렸다" && last.Kind == HandKind.Shove && last.Cause == 0 && s.Causes.Count == causes0,
                $"받음 {taken}/9 · '{notHull}' · '{again}' · 헛손 원인 {last.Cause} · 새 원인 {s.Causes.Count - causes0}");
        }
        // R8 (결함 1 — 고치기 전 빨강): 출발 전 항로를 8로 바꾼 뒤 그 앞 찍기에서 다시보면 항로 8 (예전엔 항로 고르기가 세계를 곧바로 바꿔 다시보기에 남지 않았다 — 4)
        //    · R7 화면 쪽: 다시보기 중에는 손을 받지 않는다 (산 세계에도 · 되살린 세계에도)
        {
            var show = LiveShow.Crewed("Didim", 18);
            show.FrameBudget = 1000; show.SpeedOverride = 1;   // 화면 1초에 한 틱
            for (int i = 0; i < 3; i++) show.Advance(1);
            long t0 = show.Dir.Ring[^1].tick;
            bool chose = show.ChooseRoute(8);
            long tChoose = show.Sim.Tick;
            for (int i = 0; i < 6; i++) show.Advance(1);
            bool live8 = show.Sim.Get<Outside>().Route == 8;
            var h = new Highlight { Tick = show.Sim.Tick, StartTick = t0, Last = show.Sim.Tick, Title = "항로를 바꿨다" };
            bool started = show.StartReplay(h);
            int before = show.World.Get<Outside>().Route;
            while (show.Replaying && show.World.Tick <= tChoose) show.Advance(1);
            int replayRoute = show.Replaying ? show.World.Get<Outside>().Route : -1;
            show.ArmHand(HandKind.Fire, 1);
            bool refused = show.Armed == HandKind.None && show.Notice.Contains("다시보기 중에는") && !show.ChooseRoute(3) && show.Sim.Get<Hand>().Waiting.Count == 0;
            Check("함장의 손 R8 (결함 1): 출발 전 항로를 8로 바꾸면 산 세계도 8 · 그 앞 찍기에서 다시보면 그 틱에 8로 바뀐다 (항로 고르기도 함장의 손 — 예전엔 세계를 곧바로 바꿔 다시보기는 옛 항로 4로 돌았다) · 다시보기 중에는 손 · 항로를 받지 않는다",
                chose && live8 && started && before == 4 && replayRoute == 8 && refused, $"고름 {chose} · 산 세계 {show.Sim.Get<Outside>().Route} · 다시보기 처음 {before} → 그 틱 뒤 {replayRoute} · 다시보기 중 거절 {refused} ('{show.Notice}')");
        }
    }

    /// <summary>사고 판 화면 (docs/UI2.md §8 R9 — 묶음 '화면'): 겨눔 미리 보기 = 실제 판정 · 한 번 누르면 한 번 · 선 갈래는 겹쳐 보기 '선과 관' (그만두면 되돌린다).</summary>
    private static void HandViewRules()
    {
        if (!Do("화면")) return;
        var show = LiveShow.Crewed("Didim", 21);
        show.FrameBudget = 1000; show.SpeedOverride = 1;
        for (int i = 0; i < 3; i++) show.Advance(1);
        var aim = HandTargets(show.Sim);
        var L = show.Sim.Layout;
        (double, double) Mid(int c) => (L.X(c) + 0.5, L.Y(c) + 0.5);
        show.TogglePanel(PanelKind.Hand);
        bool armed = show.PanelClick(LiveShow.KeyHandKind(HandKind.Hole)) && show.Armed == HandKind.Hole;
        show.SetHoverAt(Mid(aim.Bed).Item1, Mid(aim.Bed).Item2);
        var f1 = show.Render(); show.Recycle(f1.Img);
        string want1 = Hand.Why(show.Sim, Order(HandKind.Hole, aim.Bed, 1));
        show.SetHoverAt(Mid(aim.Hull).Item1, Mid(aim.Hull).Item2);
        var f2 = show.Render(); show.Recycle(f2.Img);
        bool preview = f1.Armed == HandKind.Hole && !f1.Aim.Ok && f1.Aim.Why == want1 && want1.Length > 0 && f2.Aim.Ok && f2.Aim.Why == "" && f2.Aim.Cell == aim.Hull && f2.Aim.Label.StartsWith("외판 구멍 · 보통");
        bool miss = !show.HandAt(Mid(aim.Bed).Item1, Mid(aim.Bed).Item2) && show.Armed == HandKind.Hole;   // 안 되는 칸: 알림만 · 겨눔은 그대로
        bool hit = show.HandAt(Mid(aim.Hull).Item1, Mid(aim.Hull).Item2) && show.Armed == HandKind.None && show.Sim.Get<Hand>().Waiting.Count == 1;
        show.Show = Overlay.Heat;
        show.ArmHand(HandKind.PowerCut, 1); bool lines = show.Show == Overlay.Lines;
        show.ArmHand(HandKind.DataCut, 1); bool still = show.Show == Overlay.Lines;
        show.Disarm(); bool back = show.Show == Overlay.Heat;
        var f3 = show.Render(); show.Recycle(f3.Img);
        bool chips = f3.HandWaiting == 1 && f3.CanHand && f3.PanelRows.Any(r => r.Key == LiveShow.KeyHandKind(HandKind.Rock));
        Check("화면 R9 사고 판: 겨눔 이름표의 되고 안 됨이 손의 판정과 같다 ('외판 칸을 누른다' · '외판 구멍 · 보통 — …') · 안 되는 칸을 누르면 알림만 (겨눔 그대로) · 되는 칸을 한 번 누르면 받아 두고 겨눔이 풀린다 · 선 갈래는 겹쳐 보기 '선과 관' · 그만두면 예전 것 (열)으로",
            armed && preview && miss && hit && lines && still && back && chips,
            $"겨눔 {armed} · 미리 보기 {preview} ('{f1.Aim.Why}' / '{f2.Aim.Label}') · 안 되는 칸 {miss} · 한 번 {hit} · 선과 관 {lines}/{still} → 되돌림 {back} · 기다리는 손 {f3.HandWaiting}");
    }
}
