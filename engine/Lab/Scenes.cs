using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>관찰 장면 — 작은 장면을 돌려 칸 지도 그림으로 남긴다 (build/scenes/이름/틱.png).</summary>
public static partial class Program
{
    /// <summary>관찰 장면이 그린 틱마다 그리는 장 수 (틱 안 기록으로 메운다 — docs/PHYSICS.md PH5).</summary>
    private const int SubFrames = 8;

    private static void Scene(string name, string outDir)
    {
        string dir = Path.Combine(outDir, name);
        Directory.CreateDirectory(dir);
        Sim s; int every, count;
        (int x, int y, int w, int h)? dots = null;   // 도트 화가로 (배 틀의 이 범위 — 떨어져 나간 조각까지)
        switch (name)
        {
            case "breach":
            {
                var l = Worlds.Ascii(Worlds.TwoRooms);
                s = Worlds.FieldOnly(l, 1, f =>
                {
                    f.SetDoor(0, 1f);
                    f.AddGas(Worlds.At(l, 3, 2), f.T.gSoot, 3, 300); // 연기가 조금 있던 방
                    f.SetHole(Worlds.At(l, 24, 3), 0.08f);
                });
                every = 5; count = 24;
                break;
            }
            case "ship":
            {
                var key = Layout.ShipKeys().First();
                var l = Layout.Ship(key, Tables.Default);
                s = Worlds.FieldOnly(l, 1);
                every = 1; count = 1;
                break;
            }
            case "fire":
            {
                var l = Worlds.Ascii(Worlds.Lounge, Worlds.Seats);
                int a = Worlds.At(l, 3, 3);
                s = Worlds.WithChem(l, 1, (f, c) => c.Ignite(a, 3e6, 0));
                var ff = s.Get<Field>(); var cc = s.Get<Chemistry>();
                for (int k = 0; k < 600; k++)
                {
                    if (k < 6 || k % 50 == 0)
                        Console.WriteLine($"  틱 {s.Tick}: 겉면 {ff.Tf[a]:0}K · 기체 {ff.Temp(a):0}K · 바닥 {ff.Ts[a]:0}K · 불 {cc.Burning[a]} · {cc.Hrr[a]:0}kW · 산소 {ff.Partial(ff.T.gO2, a):0.0}kPa · 고분자 {ff.Stored(ff.T.sPoly, a):0}몰 · 옆 겉면 {ff.Tf[a + 1]:0}K");
                    s.Step();
                }
                every = 25; count = 30;
                break;
            }
            case "meteor":
            {
                s = ShipWorld.Crewed("Didim", 4, 6, 3);
                s.Run(Time.Hours(2));
                int seed = s.Causes.Add(CauseKind.Setup, "운석 하나가 배를 향해 온다", s.Tick);
                s.Get<Outside>().AddRock(1.5, 6000, -1, 0.15, 2, 25_000, seed);
                every = 6; count = 60;
                break;
            }
            case "split":
            {
                // ④ 배가 갈라진다: 두 방을 잇는 좁은 통로를 큰 운석(20kg · 8km/s — 640MJ)이 위에서 꿰뚫는다
                var l = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
                s = Worlds.Full(l, 2);
                s.Get<Mind>().Crew(s, 2, 0, 0);
                s.Run(Time.Minutes(1));
                int seed = s.Causes.Add(CauseKind.Setup, "커다란 운석이 배를 향해 온다", s.Tick);
                s.Get<Structure>().Rock(s, Worlds.At(l, 10, 1), 20, 640e6, 0, 1, 0, 1, seed);   // 운석 길 그대로 (운동량 · 운석 지름의 구멍 — DF9: 충격파가 통로의 위아래 벽 · 바닥을 찢는다)
                every = 2; count = 30;
                break;
            }
            case "crack":
            {
                // ④ 차가운 칸막이의 금이 찢어진다 (오른쪽 방이 빠진 뒤 · 200K)
                var l = Worlds.Ascii(Worlds.TwoRooms);
                s = Worlds.Full(l, 1);
                var st = s.Get<Structure>(); var f = s.Get<Field>();
                st.SeedCrack(Worlds.At(l, 10, 1), 0.06, s.Causes.Add(CauseKind.Impact, "파편이 칸막이에 흠을 냈다", 0));
                f.SetHole(Worlds.At(l, 24, 2), 0.05f);
                for (int y = 0; y <= 5; y++) f.Ts[Worlds.At(l, 10, y)] = 200;
                every = 3; count = 30;
                break;
            }
            case "breakup":
            {
                // ④ 칸 강체: 큰 운석이 통로를 꿰뚫어 배가 둘로 갈라지고 · 오른쪽 방 바깥 벽에도 파편 구멍 —
                // 그 구멍으로 뿜는 공기가 떨어진 조각을 배 쪽으로 밀어 되돌아와 부딪히고 · 판 조각이 부스러기로 흩어진다 (사람은 덜컹임에 넘어진다)
                var l = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
                s = Worlds.Full(l, 2);
                s.Get<Mind>().Crew(s, 2, 0, 0);
                s.Run(Time.Minutes(1));
                var st = s.Get<Structure>();
                int rock = s.Causes.Add(CauseKind.Setup, "커다란 운석이 통로를 꿰뚫었다", s.Tick);
                for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(l, 10, y), rock);
                st.Destroy(s, Worlds.At(l, 20, 3), s.Causes.Add(CauseKind.Setup, "파편이 오른쪽 방 바깥 벽을 뚫었다", s.Tick));
                every = 1; count = 40; dots = (-14, -12, l.W + 28, l.H + 24);
                break;
            }
            case "foambed":
            {
                // ② 사고 화학 관찰 장면: 둘째 날 새벽 두 시 · 디딤호 '침실 둘'에서 한 사람이 자고 나머지는 당직 —
                // 빈 침대 곁 충전기의 불꽃이 폼 매트리스에 튄다. 폼이 타며 연기 · 일산화탄소 · 시안화수소 (산소가 모자랄수록) —
                // 자던 이는 깨나 · 누가 무엇을 들고 오나 · 주컴퓨터는 무엇을 알리나
                int robots = int.TryParse(Environment.GetEnvironmentVariable("ROBOTS"), out int rb) ? rb : 2;
                s = ShipWorld.Crewed("Didim", 5, 4, robots);
                s.Run(Time.Hours(26));
                var L = s.Layout; var m = s.Get<Mind>(); var life = s.Get<Life>();
                var sleeper = m.Agents.FirstOrDefault(a => a.IsPerson && life[a.C.Body].Asleep != 0 && L.FurnitureAt[a.C.Cell] >= 0 && L.Furniture[L.FurnitureAt[a.C.Cell]].Type == "Bed");
                int room = sleeper != null ? L.Room[sleeper.C.Cell] : L.Furniture.First(fu => fu.Type == "Bed").Room;
                var bed = L.Furniture.Where(fu => fu.Type == "Bed" && fu.Room == room && (sleeper == null || !fu.Cells.Contains(sleeper.C.Cell)))
                    .OrderBy(fu => sleeper == null ? 0 : Math.Abs(L.X(fu.Cells[0]) - L.X(sleeper.C.Cell)) + Math.Abs(L.Y(fu.Cells[0]) - L.Y(sleeper.C.Cell))).First();
                s.Get<Chemistry>().Ignite(bed.Cells[0], 3e6, s.Causes.Add(CauseKind.Setup, "빈 침대 곁 충전기가 합선해 불꽃이 폼 매트리스에 튀었다", s.Tick, bed.Cells[0]));
                var rc = L.Rooms[room].Cells;
                int x0 = rc.Min(L.X) - 7, y0 = rc.Min(L.Y) - 5;
                dots = (x0, y0, rc.Max(L.X) - x0 + 8, rc.Max(L.Y) - y0 + 6);
                _fireRoom = room; _sleeper = sleeper?.Id ?? -1;
                every = 25; count = 48;
                break;
            }
            case "zerogmess":
            {
                // 인공 중력 관찰 장면 '중력이 꺼진 식당': 디딤호 첫날 점심 — 식당에 모여 먹는데 인공 중력 발생기의 축받이가 닳아 멎고 · 식탁의 국과 물이 쏟아진다.
                // 사람들은 떠오르고 (처음인 이는 멀미) · 국물은 방울로 떠 설비에 들러붙고 · 주컴퓨터는 무엇을 알리고 · 로봇은 고치러 가나 · 중력이 돌아오면 사람 · 물이 바닥으로
                int robots = int.TryParse(Environment.GetEnvironmentVariable("ROBOTS"), out int rb) ? rb : 2;
                s = ShipWorld.Crewed("Didim", 6, 6, robots);
                while (Time.HourOfDay(s.Tick) < 12.1) s.Run(25);   // 점심때까지
                var L = s.Layout; var net = s.Get<Network>(); var fz = s.Get<Field>();
                int mess = L.Rooms.FindIndex(r => r.Type == "Mess");
                var gen = net.Devices.First(d => d.Type == "GravityGenerator");
                net.Age(gen.Id, Part.Mechanical, 1.2, s.Causes.Add(CauseKind.Setup, "인공 중력 발생기의 회전자 축받이가 닳았다", s.Tick, gen.Cell));
                foreach (var tb in L.Furniture.Where(fu => fu.Type == "Table" && fu.Room == mess)) fz.AddStore(tb.Cells[0], fz.T.sWater, 350);   // 식탁의 국 · 물 (6L)
                var rc = L.Rooms[mess].Cells;
                int x0 = rc.Min(L.X) - 6, y0 = rc.Min(L.Y) - 4;
                dots = (x0, y0, rc.Max(L.X) - x0 + 7, rc.Max(L.Y) - y0 + 5);
                _zgRoom = mess;
                every = 25; count = 60;
                break;
            }
            case "arrest":
            {
                // ⑦ 장기 · 약 · 수술 관찰 장면 '식당에서 멎은 심장': 디딤호 첫날 점심 — 심장이 상해 있던 이(지난 사고 · 심장 0.6)가 밥을 먹다 심장이 떤다 (잔떨림).
                // 곁의 동료가 무릎 꿇고 누르나 · 주컴퓨터가 '심정지'를 알리고 로봇이 제세동기를 가져오나 · 아드레날린 · 몇 번 만에 다시 뛰나 ·
                // 다시 뛰면 진단 · 수술(인공 심장)까지 이어지나
                int robots = int.TryParse(Environment.GetEnvironmentVariable("ROBOTS"), out int rb) ? rb : 2;
                s = ShipWorld.Crewed("Didim", 7, 6, robots);
                while (Time.HourOfDay(s.Tick) < 12.15) s.Run(25);   // 점심때까지
                var L = s.Layout; var m = s.Get<Mind>(); var life = s.Get<Life>();
                int mess = L.Rooms.FindIndex(r => r.Type == "Mess");
                var victim = m.Agents.Where(a => a.IsPerson && life[a.C.Body].Alive != 0).OrderBy(a => L.Room[a.C.Cell] == mess ? 0 : 1).First();
                ref var vb = ref life[victim.C.Body];
                vb.Heart = 0.6f; vb.HeartPeak = 0.6f;
                life.StopHeart(s, victim.C.Body, 1, "상한 심장이 갑자기 떨린다 — 부정맥", s.Causes.Add(CauseKind.Setup, "지난 사고에 상한 심장", s.Tick, vb.Cell));
                int room = L.Room[victim.C.Cell] >= 0 ? L.Room[victim.C.Cell] : mess;
                var rc = L.Rooms[room].Cells;
                int x0 = rc.Min(L.X) - 6, y0 = rc.Min(L.Y) - 4;
                dots = (x0, y0, rc.Max(L.X) - x0 + 7, rc.Max(L.Y) - y0 + 5);
                _arrestBody = victim.C.Body; _arrestWho = victim.Id;
                every = 25; count = 90;
                break;
            }
            case "robotfire":
            {
                // 판단 이관 (사) 관찰 장면 '상한 로봇': 디딤호 첫날 오전 열 시 — 화물 상자가 떨어져 로봇 하나의 팔 · 다리가 상한다 (세상이 계산한 충격 — 구조의 그 칸 충격 에너지).
                // 상한 로봇은 무엇을 느끼고 놓나 · 곧 불이 나면 주컴퓨터는 누구에게 맡기나 (팔을 못 쓰는 로봇은 빼고) · 로봇 카드에 상한 부위와 원인 사슬
                int robots = int.TryParse(Environment.GetEnvironmentVariable("ROBOTS"), out int rb) ? rb : 2;
                s = ShipWorld.Crewed("Didim", 5, 4, robots);
                while (Time.HourOfDay(s.Tick) < 10) s.Run(25);
                var L = s.Layout; var m = s.Get<Mind>(); var st = s.Get<Structure>();
                var hurt = m.Agents.First(a => !a.IsPerson);
                int drop = s.Causes.Add(CauseKind.Setup, "선반에 덜 묶인 화물 상자가 떨어졌다", s.Tick, hurt.C.Cell);
                st.ImpactJ[hurt.C.Cell] += 14000; st.DamageCause[hurt.C.Cell] = drop;   // 상자 하나 (세상이 다리 · 팔 · 속에 나눠 셈한다)
                s.Run(Time.Minutes(3));
                var stove = L.Furniture.First(fu => fu.Type == "Stove");
                s.Get<Chemistry>().Ignite(stove.Cells[0], 3e6, s.Causes.Add(CauseKind.Setup, "조리대에 흘린 기름에 불이 붙었다", s.Tick, stove.Cells[0]));
                var rc = L.Rooms[stove.Room].Cells;
                int x0 = rc.Min(L.X) - 7, y0 = rc.Min(L.Y) - 5;
                dots = (x0, y0, rc.Max(L.X) - x0 + 8, rc.Max(L.Y) - y0 + 6);
                _fireRoom = stove.Room; _sleeper = -1;
                every = 10; count = 30;
                break;
            }
            case "heart": HeartScene(dir); return;
            case "culture": CultureScene(dir); return;
            case "supply": SupplyScene(dir); return;
            case "vibration": VibrationScene(dir); return;
            case "map": MapScene(dir); return;
            case "longarc": LongArcScene(dir); return;
            case "local": LocalScene(dir); return;
            case "lights": LightsScene(dir); return;
            case "fx": FxScene(dir); return;
            case "people": PeopleScene(dir); return;
            case "wash": WashScene(dir); return;
            case "washview": WashViewScene(dir); return;
            case "art": ArtScene(dir); return;
            case "dotforms": DotFormsScene(dir); return;
            case "slide": SlideScene(dir); return;
            case "pile": PileScene(dir); return;
            case "kick": KickScene(dir); return;
            case "props-breach": PropsBreachScene(dir); return;
            case "props-space": PropsSpaceScene(dir); return;   // 우주의 물체 (PH11)
            case "props-body": PropsBodyScene(dir); return;   // 몸 물체 (PH14)
            case "props-burn": PropsBurnScene(dir); return;
            case "topple": ToppleScene(dir); return;
            case "props-zerog": PropsZeroGScene(dir); return;
            case "props-track": PropsTrackScene(dir); return;
            case "shatter": ShatterScene(dir); return;
            case "props-ships": PropsShipsScene(dir); return;
            case "mess": MessScene(dir); return;
            case "unbolt": UnboltScene(dir); return;
            case "props-blast": PropsBlastScene(dir); return;
            case "hand": HandScene(dir); return;   // 함장의 손 (docs/UI2.md U2 — SceneHand.cs)
            case "forecast": ForecastScene(dir); return;   // 예측 장부 (docs/UI2.md U8 — SceneForecast.cs)
            default: Console.WriteLine($"모르는 장면: {name}"); return;
        }
        var trace = Environment.GetEnvironmentVariable("TRACE");
        var tm = s.Find<Mind>();
        var painter = dots != null ? new Painter(s.Layout) : null;
        if (painter != null && s.Find<Structure>() is { } tst) tst.Track ??= new MotionTrack();   // 틱 안 기록 — 그린 틱마다 8장 (한 틱 늦은 시계로 메운다 · 세계는 그대로)
        var page = new System.Text.StringBuilder();
        long seen = 0;
        for (int k = 0; k < count; k++)
        {
            if (painter != null && dots is var (dx, dy, dw, dh))
            {
                // 지난 틱을 8장으로 (f = 1/8 … 1 — 앞 일곱 장은 {k}-{j}.png · 틱 끝은 {k}.png) · 첫 장은 지금 그대로
                var subs = new System.Text.StringBuilder();
                if (k > 0 && TrackPlay.Of(s) != null)
                    for (int j = 1; j < SubFrames; j++)
                    {
                        TrackPlay.Apply(painter, s, j / (double)SubFrames);
                        var si = painter.Frame(s, dx, dy, dw, dh, k * SubFrames + j);
                        Png.WriteRgba(Path.Combine(dir, $"{k:000}-{j}.png"), si.W, si.H, si.Px);
                        subs.Append($"<img class=sub src=\"{k:000}-{j}.png\">");
                    }
                TrackPlay.Apply(painter, s, 1);
                var img = painter.Frame(s, dx, dy, dw, dh, k * SubFrames + SubFrames);
                Png.WriteRgba(Path.Combine(dir, $"{k:000}.png"), img.W, img.H, img.Px);
                page.Append(subs);
                // 그 틱의 일 (사실 — 소리 · 깸은 뺀다)
                var lines = new List<string>();
                bool fire = name is "foambed" or "robotfire", zg = name == "zerogmess", ar = name == "arrest";
                foreach (var f in s.Facts.Since(seen))
                    if (ar) { if (f.Kind is FactKind.Arrest or FactKind.Revived or FactKind.Dosed or FactKind.Treated or FactKind.Diagnosed or FactKind.Surgery or FactKind.ComputerAlarm
                                 or FactKind.ComputerAssign or FactKind.Died or FactKind.Collapsed or FactKind.Rescue) lines.Add(s.Causes.Text(f.Cause)); }
                    else if (zg) { if (f.Kind is FactKind.Gravity or FactKind.Fell or FactKind.Vomit or FactKind.Woke or FactKind.ComputerAlarm or FactKind.ComputerAssign or FactKind.Short
                                 or FactKind.BreakerTrip or FactKind.MachineFail or FactKind.Hurt or FactKind.Panicked) lines.Add(s.Causes.Text(f.Cause)); }
                    else if (!fire && f.Kind is FactKind.ShipSplit or FactKind.Crash or FactKind.Tumble or FactKind.WallBreach or FactKind.Fell or FactKind.Impact) lines.Add(s.Causes.Text(f.Cause));
                    else if (fire && f.Kind is FactKind.Ignition or FactKind.FireSpread or FactKind.FireOut or FactKind.Noticed or FactKind.Woke or FactKind.Warned or FactKind.Fled
                             or FactKind.Extinguished or FactKind.ComputerAlarm or FactKind.ComputerAssign or FactKind.ComputerSeal or FactKind.DoorSealed or FactKind.Hurt or FactKind.Collapsed
                             or FactKind.Died or FactKind.Rescue or FactKind.Short or FactKind.BreakerTrip or FactKind.CableCut or FactKind.Panicked or FactKind.Treated or FactKind.MachineFail or FactKind.Blocked) lines.Add(s.Causes.Text(f.Cause));
                seen = s.Facts.Total;
                string head;
                if (name == "robotfire") head = FireCaption(s) + RobotCaption(s);
                else if (fire) head = FireCaption(s);
                else if (zg) head = ZeroGCaption(s);
                else if (ar) head = ArrestCaption(s);
                else { var stc = s.Get<Structure>(); head = $"몸 {stc.Bodies.Count} · 부딪힘 {stc.Crashes} · 부스러기 날고 {stc.Shards.Count} / 우주로 {stc.ShardKgLost / 1000:0.0}t"; }
                page.Append($"<figure><img src=\"{k:000}.png\"><figcaption>{Time.Clock(s.Tick)} · {System.Net.WebUtility.HtmlEncode(head)}{string.Concat(lines.Distinct().Take(fire || zg || ar ? 6 : 4).Select(x => "<br>" + System.Net.WebUtility.HtmlEncode(x)))}</figcaption></figure>\n");
            }
            else Map.Save(s, Path.Combine(dir, $"{k:000}.png"));
            for (int i = 0; i < every; i++)
            {
                s.Step();
                if (trace != null && tm != null && int.TryParse(trace, out int ti) && ti < tm.Agents.Count)
                {
                    var a = tm.Agents[ti];
                    int r = tm.RoomOf(a.C.Cell);
                    var fq = s.Get<Field>(); var lq = s.Find<Light>(); var cq = s.Find<Chemistry>();
                    string near = cq == null ? "" : string.Join(",", Enumerable.Range(0, s.Layout.N).Where(j => cq.Burning[j] != 0 && Math.Abs(s.Layout.X(j) - s.Layout.X(a.C.Cell)) <= 1 && Math.Abs(s.Layout.Y(j) - s.Layout.Y(a.C.Cell)) <= 1));
                    Console.WriteLine($"    [{tm.NameOf(ti)}] {Time.Clock(s.Tick)} 틱 {s.Tick} 칸 {a.C.Cell} {fq.Temp(a.C.Cell):0}K 시야 {(lq != null ? lq.Visibility(a.C.Cell) : 99):0.0} 곁불 [{near}] 모드 {a.C.Mode} {(r >= 0 ? s.Layout.Rooms[r].Name : "문")} {tm.Thought(ti)} · {s.Get<Field>().Pressure(a.C.Cell):0}kPa · 믿음 {string.Join(",", a.Beliefs.Where(b => b.Conf >= 0.3f).Select(b => $"{Mind.BeliefName(b.Kind)}@{(tm.RoomOf(b.Cell) >= 0 ? s.Layout.Rooms[tm.RoomOf(b.Cell)].Name : "?")}:{b.Conf:0.0}{b.Src.ToString()[0]}"))}");
                }
            }
        }
        if (painter != null)
            File.WriteAllText(Path.Combine(dir, "index.html"), $"<!doctype html><meta charset=utf-8><title>{name}</title><style>body{{background:#0b0d12;color:#cfd6e4;font:13px sans-serif;display:flex;flex-wrap:wrap;gap:8px}}figure{{margin:0;width:420px}}img{{width:420px;image-rendering:pixelated}}img.sub{{width:140px}}</style>\n{page}");
        Console.WriteLine($"  {name}: {count}장 → {dir}");
        if (Environment.GetEnvironmentVariable("FACTS") == "1")
        {
            var mind = s.Find<Mind>();
            foreach (var f in s.Facts.Since(0))
            {
                if (f.Kind is FactKind.LoudSound or FactKind.Woke) continue;
                string who = f.Actor >= 0 && mind != null && f.Actor < mind.Agents.Count ? mind.NameOf(f.Actor) + " " : "";
                Console.WriteLine($"    {Time.Clock(f.Tick)} {f.Kind} {who}칸 {f.Cell} 크기 {f.Size:0.##} · {s.Causes.Story(f.Cause)}");
            }
            var sm = s.Find<ShipMind>();
            if (sm != null) foreach (var (t, txt) in sm.Log) Console.WriteLine($"    [주컴퓨터] {Time.Clock(t)} {txt}");
            if (mind != null) foreach (var a in mind.Agents) Console.WriteLine($"    {mind.NameOf(a.Id)}: {mind.Thought(a.Id)} · {(a.IsPerson ? (s.Get<Life>()[a.C.Body].Alive != 0 ? "삶" : "죽음") : "")}");
        }
    }
}

public static partial class Program
{
    private static int _fireRoom = -1, _sleeper = -1, _zgRoom = -1, _arrestBody = -1, _arrestWho = -1;

    /// <summary>로봇마다 몸 (판단 이관 (사)): 전지 · 상한 부위 · 하는 일.</summary>
    private static string RobotCaption(Sim s)
    {
        var m = s.Get<Mind>(); var net = s.Get<Network>();
        var parts = new List<string>();
        foreach (var a in m.Agents.Where(a => !a.IsPerson && a.C.Mech >= 0))
        {
            var b = net.Robots[a.C.Mech];
            parts.Add($"{m.NameOf(a.Id)} 전지 {b.Battery * 100:0} 다리 {b.Legs * 100:0} 팔 {b.Arms * 100:0} 눈 {b.Eyes * 100:0} 속 {b.Core * 100:0} · {m.Thought(a.Id)}");
        }
        return parts.Count > 0 ? " / " + string.Join(" / ", parts) : "";
    }

    /// <summary>
    /// ⑧ 기억 · 정신 건강 관찰 장면 '멎었던 심장 뒤의 날들': 디딤호 첫날 점심 — 식당에서 한 사람의 심장이 떤다 (⑦ '식당에서 멎은 심장'과 같은 시작 — 살아나면 죽을 뻔한 일이 남는다) → 나흘 동안
    /// 마음의 일(악몽 · 되살아남 · 공황 발작 · 우울 · 불안 · 성격이 바뀜 · 주컴퓨터가 말을 검 · 친구의 '요즘 어때' · 상담 · 수면제)이 날 때마다 그 사람 곁을 찍는다
    /// (불이 붙은 처음 30분은 1분마다). 그림 밑: 가장 크게 겪은 이의 충격 · 외상 · 우울 · 불안 · 그 일.
    /// </summary>
    private static void HeartScene(string dir)
    {
        int robots = int.TryParse(Environment.GetEnvironmentVariable("ROBOTS"), out int rb) ? rb : 2;
        ulong seed = ulong.TryParse(Environment.GetEnvironmentVariable("SEED"), out ulong sd) ? sd : 8;
        var s = ShipWorld.Crewed("Didim", seed, 6, robots);
        while (Time.HourOfDay(s.Tick) < 12.15) s.Run(25);   // 첫날 점심
        var L = s.Layout; var m = s.Get<Mind>(); var life = s.Get<Life>(); var soc = s.Get<Society>(); var sm = s.Get<ShipMind>();
        // '식당에서 멎은 심장' (⑦ 관찰 장면과 같은 시작): 심장이 상해 있던 이가 밥을 먹다 심장이 떤다 — 살아나면 죽을 뻔한 일이 남는다
        int mess = L.Rooms.FindIndex(r => r.Type == "Mess");
        var victim = m.Agents.Where(a => a.IsPerson && life[a.C.Body].Alive != 0).OrderBy(a => L.Room[a.C.Cell] == mess ? 0 : 1).First();
        ref var vb = ref life[victim.C.Body];
        vb.Heart = 0.6f; vb.HeartPeak = 0.6f;
        life.StopHeart(s, victim.C.Body, 1, "상한 심장이 갑자기 떨린다 — 부정맥", s.Causes.Add(CauseKind.Setup, "지난 사고에 상한 심장", s.Tick, vb.Cell));
        var seat = (Cells: new[] { victim.C.Cell }, Id: 0);
        long fireAt = s.Tick, end = s.Tick + Time.Hours(24 * 4);
        var painter = new Painter(L);
        var page = new System.Text.StringBuilder();
        long seen = s.Facts.Total, lastShot = long.MinValue / 2;
        int sayingsSeen = soc.Sayings.Count, k = 0;
        const int MaxShots = 70;
        string Frame(int cell, string text)
        {
            int x0 = Math.Max(0, L.X(cell) - 14), y0 = Math.Max(0, L.Y(cell) - 9);
            var img = painter.Frame(s, x0, y0, 29, 19, k);
            Png.WriteRgba(Path.Combine(dir, $"{k:000}.png"), img.W, img.H, img.Px);
            var hero = m.Agents.Where(a => a.IsPerson).OrderByDescending(a => a.C.Trauma + a.C.Shock).First();
            string state = $"{m.NameOf(hero.Id)}: 충격 {hero.C.Shock:0.00} · 외상 {hero.C.Trauma:0.00} · 우울 {hero.C.Depress:0.00} · 불안 {hero.C.Anxiety:0.00} · 신경성 {hero.C.Neuro:0.00} · {m.Thought(hero.Id)}";
            page.Append($"<figure><img src=\"{k:000}.png\"><figcaption>{Time.Clock(s.Tick)} · {System.Net.WebUtility.HtmlEncode(state)}<br>{System.Net.WebUtility.HtmlEncode(text)}</figcaption></figure>\n");
            k++; lastShot = s.Tick;
            return text;
        }
        var lines = new List<(int cell, string text)>();   // 다음 그림까지 모은 일
        while (s.Tick < end && k < MaxShots)
        {
            s.Step();
            foreach (var f in s.Facts.Since(seen))
            {
                bool heart = f.Kind == FactKind.Heart || f.Kind == FactKind.Dosed && f.Other == (int)Drug.Hypnotic || f.Kind == FactKind.Decision && f.Other == (int)GoalKind.Counsel;
                bool fire = s.Tick - fireAt < Time.Minutes(40) && f.Kind is FactKind.Arrest or FactKind.Revived or FactKind.Panicked or FactKind.ComputerAlarm or FactKind.Collapsed or FactKind.Died or FactKind.Surgery or FactKind.Diagnosed;
                if (heart || fire) lines.Add((f.Cell >= 0 ? f.Cell : seat.Cells[0], s.Causes.Text(f.Cause)));
            }
            seen = s.Facts.Total;
            for (int i = sayingsSeen; i < soc.Sayings.Count; i++)
                if (soc.Sayings[i].Topic == Topic.Troubled) lines.Add((soc.Sayings[i].Cell, $"{s.Causes.Text(s.Causes[soc.Sayings[i].Line].Parent)} — \"{s.Causes.Text(soc.Sayings[i].Line)}\""));
            sayingsSeen = soc.Sayings.Count;
            if (s.Tick == fireAt + Time.Hours(1))
                Console.WriteLine("    한 시간 뒤: " + string.Join(" · ", m.Agents.Where(a => a.IsPerson).Select(a => $"{m.NameOf(a.Id)} 충격 {a.C.Shock:0.00} 외상 {a.C.Trauma:0.00} 겁 {a.C.Fear:0.00} 저산소 {life[a.C.Body].Hypoxia:0.00}")));
            bool firePhase = s.Tick - fireAt < Time.Minutes(30);
            if (lines.Count > 0 && s.Tick - lastShot >= (firePhase ? 25 : 5)) { Frame(lines[0].cell, string.Join(" / ", lines.Select(x => x.text).Distinct().Take(5))); lines.Clear(); }
            else if (firePhase && s.Tick - lastShot >= 25) Frame(seat.Cells[0], "");
        }
        File.WriteAllText(Path.Combine(dir, "index.html"), $"<!doctype html><meta charset=utf-8><title>heart</title><style>body{{background:#0b0d12;color:#cfd6e4;font:13px sans-serif;display:flex;flex-wrap:wrap;gap:8px}}figure{{margin:0;width:420px}}img{{width:420px;image-rendering:pixelated}}</style>\n{page}");
        Console.WriteLine($"  heart: {k}장 → {dir} · {Time.Clock(s.Tick)}");
        foreach (var a in m.Agents.Where(a => a.IsPerson))
            Console.WriteLine($"    {m.NameOf(a.Id)}: 충격 {a.C.Shock:0.00} · 외상 {a.C.Trauma:0.00} · 우울 {a.C.Depress:0.00} · 불안 {a.C.Anxiety:0.00} · 신경성 {a.C.Neuro0:0.00}→{a.C.Neuro:0.00} · 용기 {a.C.Brave0:0.00}→{a.C.Brave:0.00} · {(life[a.C.Body].Alive != 0 ? "삶" : "죽음")}");
        Console.WriteLine($"    악몽 {m.Nightmares} · 되살아남 {m.Flashbacks} · 발작 {m.PanicAttacks} · 겁이 많아짐 {m.Fearful} · 단단해짐 {m.Hardened} · 우울 {m.Depressions} · 불안 {m.Anxieties} · 수면제 {m.SleepPills} · 상담 {m.Counsels} · 주컴퓨터 말 {sm.CheckIns}");
    }

    /// <summary>
    /// ⑨ 문화 · 관습 관찰 장면 '디딤호의 엿새': 여섯이 엿새를 지낸다 — 첫날 저녁 주방 조리대에 불이 붙고 (위기 → 넘기면 축하) ·
    /// 셋째 날 아침 정비실 선반이 무너져 한 사람이 깔려 숨진다 (숨진 이의 침대 · 추모) · 늦게 자는 둘에겐 묵은 앙금이 있다 (밤 말다툼 → 깬 이들 → 배의 규칙).
    /// 함께 먹는 때는 저절로 — 엿새 사이 관습이 서는지 · 별명이 붙는지 · 금기가 생기는지를 본다 (줄거리가 아니다: 심은 것은 불 · 사고 · 앙금뿐).
    /// </summary>
    private static void CultureScene(string dir)
    {
        int robots = int.TryParse(Environment.GetEnvironmentVariable("ROBOTS"), out int rb) ? rb : 2;
        ulong seed = ulong.TryParse(Environment.GetEnvironmentVariable("SEED"), out ulong sd) ? sd : 8;
        int days = int.TryParse(Environment.GetEnvironmentVariable("DAYS"), out int dd) ? dd : 6;
        var s = ShipWorld.Crewed("Didim", seed, 6, robots);
        s.Run(25);
        var L = s.Layout; var m = s.Get<Mind>(); var life = s.Get<Life>(); var soc = s.Get<Society>(); var sm = s.Get<ShipMind>();
        var people = m.Agents.Where(a => a.IsPerson).Select(a => a.Id).ToList();
        // 늦게 자는 둘 — 지난 항해에서 남은 앙금
        int owl1 = people[0], owl2 = people[1], N = soc.N;
        m[owl1].C.Chrono = 3; m[owl2].C.Chrono = 3.5f;
        soc.Grudge[owl1 * N + owl2] = 0.4f; soc.Grudge[owl2 * N + owl1] = 0.35f;
        var stove = L.Furniture.First(fu => fu.Type == "Stove");
        var shelf = L.Furniture.First(fu => fu.Type == "Shelf" && fu.Room >= 0 && L.Rooms[fu.Room].Type == "Workshop");
        long fireAt = Time.Hours(18) + Time.Minutes(10), crushAt = Time.Hours(24 * 2 + 10) + Time.Minutes(30), end = Time.Hours(24 * days);
        bool fired = false, crushed = false;
        var painter = new Painter(L);
        var page = new System.Text.StringBuilder();
        long seen = s.Facts.Total, lastShot = long.MinValue / 2;
        int sayingsSeen = soc.Sayings.Count, k = 0;
        var meetCauses = new HashSet<int>();
        bool probe = Environment.GetEnvironmentVariable("PROBE") == "1";
        var eatMax = new int[24];
        const int MaxShots = 80;
        void Frame(int cell, string text)
        {
            int x0 = Math.Max(0, L.X(cell) - 14), y0 = Math.Max(0, L.Y(cell) - 9);
            var img = painter.Frame(s, x0, y0, 29, 19, k);
            Png.WriteRgba(Path.Combine(dir, $"{k:000}.png"), img.W, img.H, img.Px);
            string nicks = string.Join(" · ", people.Where(p => soc.NickOf(p) != null).Select(p => $"{m.NameOf(p)} '{soc.NickOf(p)}'"));
            string state = $"이 배의 관습: {soc.CultureLine(s) ?? "아직 없다"}{(nicks.Length > 0 ? " / 별명: " + nicks : "")}";
            page.Append($"<figure><img src=\"{k:000}.png\"><figcaption>{Time.Clock(s.Tick)} · {System.Net.WebUtility.HtmlEncode(state)}<br>{System.Net.WebUtility.HtmlEncode(text)}</figcaption></figure>\n");
            k++; lastShot = s.Tick;
        }
        var lines = new List<(int cell, string text)>();
        while (s.Tick < end && k < MaxShots)
        {
            s.Step();
            if (!fired && s.Tick >= fireAt)
            {
                fired = true;
                int oil = s.Causes.Add(CauseKind.Setup, "저녁을 짓던 조리대의 기름에 불이 붙었다", s.Tick, stove.Cells[0]);
                s.Get<Chemistry>().Ignite(stove.Cells[0], 3e6, oil);
                var galleyShelf = L.Furniture.FirstOrDefault(fu => fu.Type == "Shelf" && fu.Room == stove.Room);   // 튄 기름이 곁의 선반에도 옮아붙었다
                if (galleyShelf != null) s.Get<Chemistry>().Ignite(galleyShelf.Cells[0], 3e6, s.Causes.Add(CauseKind.Setup, "튄 기름불이 주방 선반에 옮아붙었다", s.Tick, galleyShelf.Cells[0], oil));
            }
            if (!crushed && s.Tick >= crushAt)
            {
                crushed = true;
                var who = m.Agents.Where(a => a.IsPerson && a.Id != owl1 && a.Id != owl2 && life[a.C.Body].Alive != 0 && life[a.C.Body].Asleep == 0)
                    .OrderBy(a => L.Room[a.C.Cell] == shelf.Room ? 0 : 1).ThenBy(a => a.Id).First();
                int why = s.Causes.Add(CauseKind.Setup, $"정비실 선반이 무너졌다 — {Ko.I(m.NameOf(who.Id))} 쏟아진 부품 상자에 깔렸다", s.Tick, who.C.Cell);
                ref var b = ref life[who.C.Body];
                b.Blunt = 2.2f; b.HurtCause = why;
                lines.Add((who.C.Cell, s.Causes.Text(why)));
            }
            foreach (var f in s.Facts.Since(seen))
            {
                if (f.Kind == FactKind.Meeting && (int)f.Size is (int)CouncilKind.Memorial or (int)CouncilKind.Celebration or (int)CouncilKind.Rule) meetCauses.Add(f.Cause);
                bool show = f.Kind is FactKind.Culture or FactKind.Ignition or FactKind.Extinguished or FactKind.Died or FactKind.Grieved
                    || f.Kind == FactKind.Meeting && (int)f.Size is (int)CouncilKind.Memorial or (int)CouncilKind.Celebration or (int)CouncilKind.Rule or -1
                    || f.Kind == FactKind.Woke && f.Cause != 0 && f.Other >= 0;
                if (show) lines.Add((f.Cell >= 0 ? f.Cell : stove.Cells[0], s.Causes.Text(f.Cause)));
            }
            seen = s.Facts.Total;
            for (int i = sayingsSeen; i < soc.Sayings.Count; i++)
                if (meetCauses.Contains(s.Causes[soc.Sayings[i].Line].Parent)) lines.Add((soc.Sayings[i].Cell, s.Causes.Text(soc.Sayings[i].Line)));
            sayingsSeen = soc.Sayings.Count;
            if (probe && s.Tick % Time.Minutes(1) == 0)
            {
                int mr = m.MessRoom(), eat = m.Agents.Count(a => a.IsPerson && a.C.Goal == GoalKind.Eat && m.RoomOf(a.C.Cell) == mr && a.C.StepIndex < a.Plan.Count && a.Plan[a.C.StepIndex].Kind == StepKind.Use);
                int hr = (int)Time.HourOfDay(s.Tick);
                if (eat > eatMax[hr]) eatMax[hr] = eat;
                if (s.Tick % Time.TicksPerDay == 0) { Console.WriteLine($"    {Time.Clock(s.Tick - 1)} 식당({L.Rooms[mr].Name})에서 함께 먹은 가장 많은 수 (시마다): {string.Join(" ", eatMax.Select((n, h) => n > 0 ? $"{h}시:{n}" : "").Where(x => x.Length > 0))}"); Array.Clear(eatMax); }
                if (soc.Meeting.Kind != CouncilKind.None && s.Tick % Time.Minutes(3) == 0)
                    Console.WriteLine($"    {Time.Clock(s.Tick)} 모임({soc.Meeting.Kind} · {L.Rooms[soc.Meeting.Room].Name}): " + string.Join(" · ", m.Agents.Where(a => a.IsPerson && life[a.C.Body].Alive != 0).Select(a => $"{m.NameOf(a.Id)} {a.C.Goal}@{(m.RoomOf(a.C.Cell) >= 0 ? L.Rooms[m.RoomOf(a.C.Cell)].Name : "-")}{(life[a.C.Body].Asleep != 0 ? "(잠)" : "")}")));
                if (Time.HourOfDay(s.Tick) is >= 22 or < 3 && s.Tick % Time.Minutes(30) == 0)
                    Console.WriteLine($"    {Time.Clock(s.Tick)} 밤: " + string.Join(" · ", new[] { owl1, owl2 }.Select(o => $"{m.NameOf(o)} {m[o].C.Goal}@{(m.RoomOf(m[o].C.Cell) >= 0 ? L.Rooms[m.RoomOf(m[o].C.Cell)].Name : "-")}")) + " / 자는 이: " + string.Join(" · ", m.Agents.Where(a => a.IsPerson && life[a.C.Body].Asleep != 0).Select(a => $"{m.NameOf(a.Id)}@{L.Rooms[m.RoomOf(a.C.Cell)].Name}")));
            }
            if (lines.Count > 0 && s.Tick - lastShot >= 5 && (lines.Count >= 4 || s.Tick - lastShot >= Time.Minutes(2))) { Frame(lines[0].cell, string.Join(" / ", lines.Select(x => x.text).Distinct().Take(5))); lines.Clear(); }
        }
        File.WriteAllText(Path.Combine(dir, "index.html"), $"<!doctype html><meta charset=utf-8><title>culture</title><style>body{{background:#0b0d12;color:#cfd6e4;font:13px sans-serif;display:flex;flex-wrap:wrap;gap:8px}}figure{{margin:0;width:420px}}img{{width:420px;image-rendering:pixelated}}</style>\n{page}");
        Console.WriteLine($"  culture: {k}장 → {dir} · {Time.Clock(s.Tick)}");
        Console.WriteLine($"    관습 {soc.Customs} ({(soc.MealCustom >= 0 ? soc.MealCustom + "시" : "없음")}) · 함께 먹은 몫 {soc.CustomMealsShared} · 관습으로 간 번 {m.CustomEats} · 금기 {soc.Taboos}(풀림 {soc.TaboosLifted}) · 침대 {soc.BedTaboos} · 비켜 간 침대 {m.BedsShunned}"
            + $" · 별명 {soc.Nicknames} · 추모 {soc.Memorials} · 축하 {soc.Celebrations} · 모임 {soc.Gatherings}(못 열림 {soc.GatheringsFailed}) · 규칙 {soc.RulesMade}(부결 {soc.RulesRejected}) · 어김 {soc.RuleBreaks} · 삼킨 말 {soc.QuietKept} · 밤 깸 {soc.NightWakes}");
        Console.WriteLine($"    이 배의 관습: {soc.CultureLine(s) ?? "없음"} · 별명: {string.Join(" · ", people.Where(p => soc.NickOf(p) != null).Select(p => $"{m.NameOf(p)} '{soc.NickOf(p)}'"))}");
        foreach (var a in m.Agents.Where(a => a.IsPerson))
            Console.WriteLine($"    {m.NameOf(a.Id)}: {(life[a.C.Body].Alive != 0 ? "삶" : "죽음")} · 슬픔 {soc.GriefOf(a.Id):0.00} · 기쁨 {a.C.Joy:0.00} · 피로 {a.C.Weary:0.00}");
    }

    /// <summary>심정지 장면 그림 밑 글: 그 사람 (뜀 · 잔떨림 · 멎음 · 깨어 있나) · 산소 부족 · 뇌 · 심장 · 누른 분 · 제세동 · 진료 기록 · 주컴퓨터의 마지막 말.</summary>
    private static string ArrestCaption(Sim s)
    {
        var m = s.Get<Mind>(); var life = s.Get<Life>();
        var b = life[_arrestBody];
        string beat = b.Alive == 0 ? "숨짐" : b.Arrest == 1 ? "잔떨림" : b.Arrest == 2 ? "멎음" : b.Conscious != 0 ? "뛴다 · 깨어 있다" : "뛴다 · 정신을 잃었다";
        var sm = s.Find<ShipMind>();
        string said = sm != null && sm.Log.Count > 0 ? " · 주컴퓨터: " + sm.Log[^1].text : "";
        string chart = m[_arrestWho].C.DxAt > 0 ? $" · 기록 {Life.AilName(m[_arrestWho].C.DxAil)}" : "";
        return $"{m.NameOf(_arrestWho)}: {beat} · 산소 부족 {b.Hypoxia:0.00} · 뇌 {b.Brain:0.00} · 심장 {b.Heart:0.00} · 압박 {b.LowFlow:0}분 · 압박 없이 {b.NoFlow:0}분 · 제세동 {life.Shocks}번{chart}{said}";
    }

    /// <summary>중력 장면 그림 밑 글: 식당 바닥 중력 · 떠 있는 이 · 멀미 · 떠 있는 물 · 들러붙은 물 · 발생기 · 주컴퓨터의 마지막 말.</summary>
    private static string ZeroGCaption(Sim s)
    {
        var f = s.Get<Field>(); var L = s.Layout; var net = s.Get<Network>(); var m = s.Get<Mind>(); var life = s.Get<Life>();
        var rc = L.Rooms[_zgRoom].Cells;
        var people = m.Agents.Where(a => a.IsPerson && life[a.C.Body].Alive != 0).ToList();
        int floating = people.Count(a => a.C.Floated != 0);
        double sick = people.Count > 0 ? people.Average(a => life[a.C.Body].Nausea) : 0;
        var gen = net.Devices.First(d => d.Type == "GravityGenerator");
        string gs = net.IsBroken(gen.Id, Part.Mechanical) || net.IsBroken(gen.Id, Part.Electric) ? "망가짐" : net.Working(gen.Id) ? $"돎 {net.GravField[gen.Id]:0.00}" : "꺼짐";
        var sm = s.Find<ShipMind>();
        string said = sm != null && sm.Log.Count > 0 ? " · 주컴퓨터: " + sm.Log[^1].text : "";
        double puddles = rc.Sum(c => f.Stored(f.T.sWater, c)) * 0.018;
        return $"식당 {f.Grav[rc[0]]:0.00}g · 떠 있는 이 {floating}/{people.Count} · 멀미 {sick:0.00} · 떠 있는 물 {f.Drops.Sum() * 0.018:0.0}L · 겉면 · 바닥의 물 {puddles:0.0}L · 발생기 {gs}{said}";
    }

    /// <summary>불 장면 그림 밑 글: 타는 칸 · 그 방 공기 (산소 · 일산화탄소 · 시안화수소 · 염화수소 · 가장 뜨거운 곳) · 자던 사람.</summary>
    private static string FireCaption(Sim s)
    {
        var f = s.Get<Field>(); var ch = s.Get<Chemistry>(); var L = s.Layout;
        var rc = L.Rooms[_fireRoom].Cells;
        double Ppm(int g) => rc.Max(c => f.Partial(g, c) / Math.Max(1e-6, f.Pressure(c))) * 1e6;
        double o2 = rc.Average(c => f.Partial(f.T.gO2, c) / Math.Max(1e-6, f.Pressure(c)));
        string who = "";
        var m = s.Find<Mind>();
        if (m != null && _sleeper >= 0)
        {
            var a = m[_sleeper]; var b = s.Get<Life>()[a.C.Body];
            int r = m.RoomOf(a.C.Cell);
            who = $" · {m.NameOf(_sleeper)}: {(b.Alive == 0 ? "죽음" : b.Asleep != 0 ? "잠" : "깸")} · {(r >= 0 ? L.Rooms[r].Name : "문")} · {m.Thought(_sleeper)}";
        }
        return $"타는 칸 {ch.BurningCount} · 산소 {o2:P1} · 일산화탄소 {Ppm(f.T.gCO):0} · 시안화수소 {Ppm(f.T.gHCN):0} · 염화수소 {Ppm(f.T.gHCl):0}ppm · {rc.Max(c => f.Temp(c)) - 273:0}℃{who}";
    }
}

/// <summary>칸 지도 그리기: 벽 · 바닥 · 연기 · 물 · 열 · 압력 · 바람.</summary>
public static class Map
{
    public const int Px = 8;

    public static void Save(Sim s, string path, bool overlay = true)
    {
        var f = s.Get<Field>();
        var chem = s.Find<Chemistry>();
        var st = s.Find<Structure>();
        var l = f.L;
        int w = l.W * Px, h = l.H * Px;
        var img = new uint[w * h];
        for (int i = 0; i < l.N; i++)
        {
            int cx = l.X(i), cy = l.Y(i);
            uint c;
            switch (l.Kind[i])
            {
                case CellKind.Outside: c = Png.Rgba(0.02, 0.02, 0.05); break;
                case var _ when f.Severed[i] != 0: c = Png.Rgba(0.0, 0.0, 0.0); break;   // 떨어져 나간 자리 — 우주
                case CellKind.Wall:
                    c = f.Hole[i] > 0 ? Png.Rgba(0.9, 0.3, 0.1) : l.Hull[i] ? Png.Rgba(0.45, 0.47, 0.52) : Png.Rgba(0.35, 0.36, 0.4);
                    break;
                case CellKind.Door:
                    c = f.DoorOpen[l.DoorAt[i]] >= 0.5f ? Png.Rgba(0.3, 0.5, 0.35) : Png.Rgba(0.7, 0.6, 0.2);
                    break;
                default:
                {
                    double p = f.Pressure(i) / 101.3;
                    double t = f.Temp(i);
                    double smoke = Math.Min(1, f.Ppm(f.T.gSoot, i) / 20000);
                    double water = Math.Min(1, f.Stored(f.T.sWater, i) / 2000);
                    double r = 0.12 + 0.1 * p, g = 0.13 + 0.1 * p, b = 0.15 + 0.12 * p;
                    if (overlay)
                    {
                        double hot = Math.Clamp((t - 300) / 500, 0, 1), cold = Math.Clamp((280 - t) / 100, 0, 1);
                        r += 0.8 * hot; g += 0.3 * hot; b += 0.4 * cold;
                    }
                    r = r * (1 - smoke) + 0.05 * smoke; g = g * (1 - smoke) + 0.04 * smoke; b = b * (1 - smoke) + 0.04 * smoke;
                    b += 0.5 * water; g += 0.15 * water;
                    if (l.FurnitureAt[i] >= 0) { r += 0.08; g += 0.08; b += 0.06; }
                    if (f.Hole[i] > 0) { r += 0.45; g *= 0.6; b *= 0.6; }                       // 바닥 · 천장 판이 뚫렸다
                    if (st != null && st.FragmentOf(i) > 0) { b += 0.18; }                       // 떨어져 나간 조각
                    if (chem != null && chem.Hrr[i] > 1) { double fl = Math.Min(1, chem.Hrr[i] / 200); r = 0.6 + 0.4 * fl; g = 0.25 + 0.45 * fl; b = 0.05; }
                    double sc = Math.Clamp((f.Tf[i] - 400) / 500, 0, 1);
                    if (sc > 0 && (chem == null || chem.Hrr[i] <= 1)) { r += 0.5 * sc; g += 0.15 * sc; }
                    c = Png.Rgba(r, g, b);
                    break;
                }
            }
            for (int y = 0; y < Px; y++) for (int x = 0; x < Px; x++) img[(cy * Px + y) * w + cx * Px + x] = c;
            // 바람 화살 (점 두 개)
            double wx = f.WindX[i], wy = f.WindY[i], sp = Math.Sqrt(wx * wx + wy * wy);
            if (overlay && sp > 0.5)
            {
                double ux = wx / sp, uy = wy / sp, len = Math.Min(Px / 2 - 1, 1 + Math.Log10(1 + sp) * 2);
                for (double k = 0; k <= len; k += 0.5)
                {
                    int px = cx * Px + Px / 2 + (int)(ux * k), py = cy * Px + Px / 2 + (int)(uy * k);
                    if (px >= 0 && py >= 0 && px < w && py < h) img[py * w + px] = Png.Rgba(0.9, 0.95, 1);
                }
            }
        }
        var lifeE = s.Find<Life>();
        var mindE = s.Find<Mind>();
        if (mindE != null)
            foreach (var a in mindE.Agents)
            {
                if (a.IsPerson) continue;
                int bx = l.X(a.C.Cell) * Px, by = l.Y(a.C.Cell) * Px;
                uint col = Png.Rgba(0.4, 0.7, 1.0);
                for (int yy = 2; yy < Px - 2; yy++) for (int xx = 2; xx < Px - 2; xx++) if ((xx + yy) % 2 == 0) img[(by + yy) * w + bx + xx] = col;
            }
        if (lifeE != null)
            foreach (var b in lifeE.Bodies)
            {
                if (b.Cell < 0) continue;
                int bx = l.X(b.Cell) * Px, by = l.Y(b.Cell) * Px;
                uint col = b.Alive == 0 ? Png.Rgba(0.4, 0.1, 0.1) : b.Conscious == 0 ? Png.Rgba(0.9, 0.7, 0.2) : Png.Rgba(0.3, 0.9, 0.5);
                for (int yy = 2; yy < Px - 2; yy++) for (int xx = 2; xx < Px - 2; xx++) img[(by + yy) * w + bx + xx] = col;
            }
        Png.Write(path, w, h, img);
    }
}
