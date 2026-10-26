using System.Runtime.InteropServices;
using ShipEngine;

namespace Lab;

/// <summary>깊이 고침 1차 — 평시의 손 · 마음에 남는 것 (docs/DEPTH_FIX.md DF5 · DF6 · DF7 · 근거 docs/ENGINE_DEPTH_REVIEW.md §4-3 · §4-5 · §4-6 · §4-7 · §4-10 · §5).</summary>
public static partial class Program
{
    private static void Depth3Rules()
    {
        // ── DF5 보면 끈다 · 시신 수습 (보고서 §4-3 · §4-10 · §5): 켜 둔 오븐은 쓰는 이 없이 한 시간을 넘기지 않는다 · 숨진 이는 여섯 시간 안에 덮여 옮겨진다 ──
        if (Do("보면 끈다 · 수습"))
        {
            Console.WriteLine("\n[보면 끈다 · 시신 수습 — 켜 둔 오븐 · 숨진 이]");
            bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
            // 디딤호 6명 · 로봇 2 — 10분 뒤 주방 오븐을 켜 둔 채 자리를 떴다 (원인 마디) · 주컴퓨터가 돌면 주방 문을 잠가 아무도 못 들어가게 (주컴퓨터 몫만) · 멈췄으면 그대로 (사람 · 로봇의 눈)
            (long offAt, int by, string how, string story) Oven(bool computer)
            {
                var s = ShipWorld.Crewed("Didim", 9, 6, 2);
                var net = s.Get<Network>(); var m = s.Get<Mind>(); var sm = s.Get<ShipMind>(); var st = s.Get<Structure>();
                if (!computer) net.SetOn(net.Devices.First(d => d.Type == "MainComputer").Id, false);   // 주컴퓨터를 끈 배
                s.Run(Time.Minutes(10));
                var oven = net.Devices.First(d => d.Type == "Oven" && s.Layout.Rooms[s.Layout.Room[d.Cell]].Type == "Galley");
                int galley = s.Layout.Room[oven.Cell];
                if (computer)
                    for (int d = 0; d < s.Layout.Doors.Count; d++)
                    {
                        Span<int> nb = stackalloc int[4];
                        s.Layout.Neighbors(s.Layout.Doors[d].Cell, nb);
                        foreach (int j in nb) if (j >= 0 && s.Layout.Room[j] == galley) { st.LockDoor(d, s.Tick + Time.Hours(2)); break; }
                    }
                if (computer)   // 주방 안에 있던 이는 식당으로 (잠근 문 안에서 오븐을 보지 않게)
                {
                    int mess = s.Layout.Rooms.First(r => r.Type == "Mess").Cells.First(c => s.Layout.Kind[c] == CellKind.Floor && s.Layout.FurnitureAt[c] < 0);
                    foreach (var a in m.Agents.Where(a => a.IsPerson && s.Layout.Room[a.C.Cell] == galley)) { a.C.Cell = mess; s.Get<Life>().Move(a.C.Body, mess); a.Plan.Clear(); a.Path.Clear(); }
                }
                int who = m.Agents.First(a => a.IsPerson).Id;
                int c0 = s.Causes.Add(CauseKind.Person, $"{Ko.I(m.NameOf(who))} {Ko.Eul(oven.Name)} 켜 둔 채 자리를 떴다", s.Tick, oven.Cell, 0, 0, who);
                net.SetOn(oven.Id, true); net.OnCause[oven.Id] = c0;
                long f0 = s.Facts.Total, start = s.Tick, offAt = -1;
                for (long k = 0; k < Time.Hours(1) && offAt < 0; k++) { s.Step(); if (net.On[oven.Id] == 0) offAt = s.Tick - start; }
                var sw = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Switched && x.Other == oven.Id);
                string how = sw.Kind == FactKind.Switched ? s.Causes.Text(sw.Cause) : "";
                if (Debug) Console.WriteLine($"    {(computer ? "주컴퓨터" : "멈춤")} · 꺼짐 {(offAt >= 0 ? Time.ToSeconds(offAt) / 60 : -1):0}분 · \"{how}\" · {string.Join(" / ", sm.Log.Where(l => l.text.Contains(oven.Name)).Select(l => l.text).Take(3))}");
                return (offAt, sw.Kind == FactKind.Switched ? sw.Actor : -9, how, sw.Kind == FactKind.Switched ? s.Causes.Story(sw.Cause) : "");
            }
            var aloneT = Task.Run(() => Oven(false)); var watched = Oven(true); var alone = aloneT.Result;
            Check("켜진 오븐이 쓰는 이 없이 1시간 넘게 켜져 있지 않다: 주방을 아무도 못 보면 주컴퓨터가 쓰는 이 없이 15분 켜진 오븐을 끈다 (경보 문턱 아래도) · 주컴퓨터가 멈췄으면 지나다 본 사람 · 로봇이 끈다 — 원인 사슬은 켜 둔 이에게 잇는다",
                watched.offAt >= 0 && watched.offAt <= Time.Minutes(20) && watched.by == -1 && watched.how.Contains("쓰는 이 없이") && watched.story.Contains("켜 둔 채")
                && alone.offAt >= 0 && alone.by >= 0 && alone.story.Contains("켜 둔 채"),
                $"주컴퓨터 {(watched.offAt >= 0 ? $"{Time.ToSeconds(watched.offAt) / 60:0}분" : "1시간 넘게 켜짐")} '{watched.how}' / 멈춤 {(alone.offAt >= 0 ? $"{Time.ToSeconds(alone.offAt) / 60:0}분" : "1시간 넘게 켜짐")} (끈 이 {alone.by}) '{alone.how}'");

            // 숨진 이: 디딤호 6명 · 로봇 2 — 30분 뒤 한 사람이 그 자리에서 숨졌다 (주컴퓨터가 돌 때 · 멈췄을 때) → 6시간 안에 덮여 쉴 곳(추모실 · 의무실)으로 옮겨진다 · 먼저 로봇
            (bool moved, bool covered, string room, string by, double hours) Corpse(bool computer)
            {
                var s = ShipWorld.Crewed("Didim", 4, 6, 2);
                var net = s.Get<Network>(); var m = s.Get<Mind>(); var life = s.Get<Life>();
                if (!computer) net.SetOn(net.Devices.First(d => d.Type == "MainComputer").Id, false);   // 주컴퓨터를 끈 배
                s.Run(Time.Minutes(30));
                var dead = m.Agents.Where(a => a.IsPerson && life[a.C.Body].Asleep == 0 && s.Layout.Room[a.C.Cell] >= 0 && s.Layout.Rooms[s.Layout.Room[a.C.Cell]].Type is not ("Medbay" or "Chapel")).OrderBy(a => a.Id).First();
                int body = dead.C.Body, at = life[body].Cell;
                int why = s.Causes.Add(CauseKind.Setup, $"{m.NameOf(dead.Id)}의 심장이 멎었다 — 타고난 심장병", s.Tick, at);
                { ref var b = ref life[body]; b.Alive = 0; b.Conscious = 0; b.DiedAt = s.Tick; b.DeathCause = why; }
                s.Facts.Emit(s.Tick, FactKind.Died, at, 0, why, actor: body);
                long start = s.Tick, doneAt = -1;
                for (long k = 0; k < Time.Hours(6) && doneAt < 0; k++) { s.Step(); if (life[body].Shroud != 0 && life[body].Cell != at && dead.C.CarriedBy < 0) doneAt = s.Tick - start; }
                var laid = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.LaidOut && x.Other == body);
                int r = s.Layout.Room[life[body].Cell];
                if (Debug) Console.WriteLine($"    {(computer ? "주컴퓨터" : "멈춤")} (켜짐 {s.Get<ShipMind>().Online}) · {m.NameOf(dead.Id)} {s.Layout.Rooms[s.Layout.Room[at]].Name} → {(r >= 0 ? s.Layout.Rooms[r].Name : "-")} · 덮음 {life[body].Shroud} · {(laid.Kind == FactKind.LaidOut ? s.Causes.Story(laid.Cause) : "-")}");
                return (life[body].Cell != at, life[body].Shroud != 0, r >= 0 ? s.Layout.Rooms[r].Type : "-",
                    laid.Kind == FactKind.LaidOut && laid.Actor >= 0 ? (m.Agents[laid.Actor].IsPerson ? "사람" : "로봇") : "-", doneAt >= 0 ? Time.ToSeconds(doneAt) / 3600 : -1);
            }
            var cAloneT = Task.Run(() => Corpse(false)); var cWatched = Corpse(true); var cAlone = cAloneT.Result;
            Check("시신이 6시간 넘게 그 자리에 있지 않다: 숨진 이를 로봇이 먼저 수습해 덮고 의무실로 옮긴다 (주컴퓨터가 맡기고 · 멈췄으면 돌던 로봇이 보고 스스로)",
                cWatched.moved && cWatched.covered && cWatched.room is "Medbay" or "Chapel" && cWatched.by == "로봇"
                && cAlone.moved && cAlone.covered && cAlone.room is "Medbay" or "Chapel",
                $"주컴퓨터: 옮김 {cWatched.moved} · 덮음 {cWatched.covered} · {cWatched.room} · {cWatched.by} · {cWatched.hours:0.0}시간 / 멈춤: 옮김 {cAlone.moved} · 덮음 {cAlone.covered} · {cAlone.room} · {cAlone.by} · {cAlone.hours:0.0}시간");
        }

        // ── DF6 물 · 맡은 일 자료 (보고서 §4-3 · §5): 오수가 밀리면 주컴퓨터가 막힌 곳(처리량)을 짚고 샤워 배급 · 재생기 정비를 낸다 · 새뜰호 농부가 재배 선반에서 일하고 거둔다 ──
        if (Do("물 · 맡은 일"))
        {
            Console.WriteLine("\n[물 · 맡은 일 — 밀린 오수 · 농부의 재배 선반]");
            bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
            // 밀린 오수: 디딤호 6명 · 로봇 2 — 물 재생기 거르개가 낡았고 (0.8) 샤워가 몰려 한 시간마다 오수 25L가 더 든다 · 여섯 시간 (새뜰호와 나란히)
            var wasteT = Task.Run(() =>
            {
                var s = ShipWorld.Crewed("Didim", 4, 6, 2);
                var net = s.Get<Network>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>();
                var rec = net.Devices.First(d => d.Def.Role == "recycler");
                net.Age(rec.Id, Part.Filter, 0.8);
                for (int k = 0; k < Time.Hours(6); k++)
                {
                    s.Step();
                    if (k % Time.Hours(1) == 0) net.WasteWater += 25 * 55.5;
                }
                var blk = sm.Blocked.FirstOrDefault(b => b.StartsWith("오수")) ?? "";
                bool wrong = sm.Blocked.Any(b => b.Contains("마시는 양")) || sm.Log.Any(l => l.text.Contains("마시는 양이 되찾는 양보다"));
                bool fix = sm.Jobs.Any(j => j.Kind == GoalKind.Maintain && j.Target / 4 == rec.Id) || s.Facts.Since(0).Any(x => x.Kind == FactKind.Repaired && x.Other == rec.Id && (int)x.Size == (int)Part.Filter);
                if (Debug) Console.WriteLine($"    오수 {net.WasteWater * 0.018:0}L · 막힌 곳 [{string.Join(" / ", sm.Blocked)}] · 샤워 배급 {m.ShowerRuleMin}분 · 정비 {fix} · {string.Join(" / ", sm.Log.Where(l => l.text.Contains("오수") || l.text.Contains("샤워")).Select(l => l.text).Take(4))}");
                return (blk, wrong, shower: m.ShowerRuleMin, fix, waste: net.WasteWater * 0.018);
            });

            // 새뜰호 농부: 맡은 일 표의 설비가 재배 선반 · 화분 선반을 안다 → 재배 갑판에 일터가 있고 · 일하다 제 갑판의 작물이 다 자라면 거둔다
            {
                var s = ShipWorld.Crewed("Saetteul", 3, 40, 16);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var L = s.Layout;
                int farmer = Array.IndexOf(m.RoleNames, "농부");
                var farmers = m.Agents.Where(a => a.IsPerson && a.C.Role == farmer).ToList();
                bool stations = farmers.Count > 0 && farmers.All(a => a.C.Station >= 0 && L.Rooms[L.Furniture[a.C.Station].Room].Type == "Hydroponics");
                { int h = (int)Time.HourOfDay(s.Tick); foreach (var a in farmers) a.C.ShiftHour = h; }   // 농부들이 지금 근무 (출항 첫 시간)
                var byFarmer = new HashSet<int>();
                var net = s.Get<Network>();
                s.Run((int)Time.Minutes(40));   // 일터로 간다 (그사이 출항 때 다 자란 작물은 로봇이 거둔다)
                foreach (var a in farmers)
                {
                    int room = a.C.Station >= 0 ? L.Furniture[a.C.Station].Room : -1;
                    int crop = Enumerable.Range(0, life.Crops.Count).FirstOrDefault(k => L.Room[life.Crops[k].Cell] == room, -1);
                    if (crop >= 0) CollectionsMarshal.AsSpan(life.Crops)[crop].Biomass = (float)(Work.HarvestAt * life.CropArea(net, crop) * 1.05);   // 일하는 재배 갑판의 작물 하나가 막 다 자랐다
                }
                for (int k = 0; k < Time.Minutes(40); k++)
                {
                    s.Step();
                    foreach (var a in farmers) if (a.C.Goal == GoalKind.Harvest && a.C.Holding == m.ItemIndex("수확물")) byFarmer.Add(a.Id);
                }
                if (Debug) Console.WriteLine($"    농부 {farmers.Count} · 일터 {string.Join(",", farmers.Select(a => a.C.Station >= 0 ? L.Furniture[a.C.Station].Type + "@" + L.Rooms[L.Furniture[a.C.Station].Room].Name : "-"))} · 거둔 농부 {byFarmer.Count} · 거둔 수 {s.Get<Work>().Harvested}");
                Check("새뜰호 농부가 거둔다: 맡은 일 표가 재배 선반 · 화분 선반을 알아 농부마다 재배 갑판에 일터가 있고 · 일하다 제 갑판의 작물이 다 자라면 40분 안에 거둔다 (주컴퓨터는 농부가 일하는 갑판의 작물을 로봇에게 맡기지 않는다 · 예전엔 설비 이름이 달라 농부 여섯이 이틀 놀고 거두기 0끼니)",
                    stations && byFarmer.Count >= 1, $"농부 {farmers.Count} · 일터 있음 {stations} · 거둔 농부 {byFarmer.Count}");
            }
            var w = wasteT.Result;
            Check("오수가 밀리면 막힌 곳을 짚는다: 주컴퓨터가 '오수가 밀린다 ← 물 재생기가 거르는 양이 드는 양에 못 미친다'를 막힌 곳으로 · 샤워 배급을 열고 · 재생기 거르개를 미리 간다 ('마시는 양이 되찾는 양보다 많다'는 오진이다)",
                w.blk.Contains("물 재생기") && !w.wrong && w.shower > 0 && w.fix, $"막힌 곳 '{w.blk}' · 오진 {w.wrong} · 샤워 배급 {w.shower}분 · 재생기 정비 {w.fix} · 오수 {w.waste:0}L");
        }

        // ── DF7 마음에 남는 것 (보고서 §4-5 · §4-6 · §5): 자는 동안 외로움이 차지 않는다 · 축하는 그날 기분과 무게 있는 좋은 기억으로 남는다 ──
        //    (기름불이 난 저녁 · 평시 감정 0의 몫 · 함께 웃은 기억은 '평시 하루'가 같은 하루로 본다)
        if (Do("마음에 남는 것"))
        {
            Console.WriteLine("\n[마음에 남는 것 — 잠 · 외로움 · 축하]");
            bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
            var legend = new Dictionary<char, string> { ['m'] = "MealDispenser", ['a'] = "Seat", ['e'] = "Bed" };
            var lay = Worlds.Ascii(CultureShip, legend);
            int At(int x, int y) => Worlds.At(lay, x, y);
            int beds = lay.Room[At(2, 2)], mess = lay.Room[At(14, 2)], lounge = lay.Room[At(24, 2)];
            lay.Rooms[beds].Name = "침실"; lay.Rooms[mess].Name = "식당"; lay.Rooms[lounge].Name = "휴게실";
            lay.Rooms[mess].Type = "Lounge"; lay.Rooms[lounge].Type = "Lounge";

            // 잠든 8시간: 외향인 이 (사람이 빨리 그립다)가 제 침대에서 잔다 — 혼자 · 곁 침대 칸에 깬 이가 있어도 외로움이 늘지 않는다 (예전엔 자는 동안 시간에 0.1씩 찼다 · 0.96 → 1.00)
            (float l0, float l1, bool slept) Sleep(bool company)
            {
                var s = Worlds.Full(lay, 4);
                var m = s.Get<Mind>(); var life = s.Get<Life>();
                int x = m.AddPerson(s, At(3, 1), bed: At(3, 1));
                m.SetPersonality(x, 0.5f, 0.6f, 0.85f, 0.5f, 0.4f);
                int y = company ? m.AddPerson(s, At(5, 1), bed: At(5, 1)) : -1;
                s.Run(2);
                m[x].C.Lonely = 0.5f;
                bool slept = true;
                for (int k = 0; k < Time.Hours(8); k++)
                {
                    ref var c = ref m[x].C;
                    c.Sleepy = Math.Max(c.Sleepy, 0.7f); c.Hunger = 0; life[c.Body].Asleep = 1;   // 8시간 내리 잔다 (잠 자체는 이 시험의 몫이 아니다)
                    if (y >= 0) { ref var cy = ref m[y].C; cy.Sleepy = 0; cy.Hunger = 0; cy.Bored = 0; life[cy.Body].Asleep = 0; }
                    s.Step();
                    if (life[m[x].C.Body].Asleep == 0) slept = false;
                }
                return (0.5f, m[x].C.Lonely, slept);
            }
            var withT = Task.Run(() => Sleep(true)); var alone = Sleep(false); var with = withT.Result;
            Check("잠든 8시간 동안 외로움이 늘지 않는다: 외향인 이가 혼자 자도 · 곁에 깬 이가 있어도 — 외로움은 깨어 있는 동안 차고 말 · 함께 쉼 · 끼니로 풀린다",
                alone.slept && with.slept && alone.l1 <= alone.l0 + 0.01f && with.l1 <= with.l0 + 0.01f,
                $"혼자 {alone.l0:0.00} → {alone.l1:0.00} · 곁에 깬 이 {with.l0:0.00} → {with.l1:0.00} (내리 잤다 {alone.slept}/{with.slept})");

            // 축하 뒤: 불이 번졌다 꺼지고 다섯이 다 살았다 → 가장 외향인 이가 부른 축하 자리 — 마친 두 시간 뒤에도 그날 기분이 밝고 (기쁨 0.1 넘게 · 예전엔 시간에 0.25씩 잦아들어 0) ·
            //   축하는 무게 있는 좋은 기억 (0.5 넘게 — 예전 0.4 · 꺼낼 때마다 반으로)으로 모두에게 남는다
            {
                var s = Worlds.Full(lay, 9);
                var m = s.Get<Mind>(); var soc = s.Get<Society>();
                var p = new List<int>();
                for (int i = 0; i < 5; i++) { int a = m.AddPerson(s, At(12 + (i % 4) * 2, 1 + (i / 4) * 2)); m.SetPersonality(a, 0.5f, 0.6f, 0.6f, 0.5f, 0.4f); p.Add(a); }
                m.SetPersonality(p[1], 0.5f, 0.6f, 0.95f, 0.5f, 0.4f);
                s.Run(2);
                foreach (int a in p) m[a].C.Joy = 0;
                int fire = s.Causes.Add(CauseKind.Fire, "식당 조리대에 불이 붙었다", s.Tick, At(14, 2));
                s.Facts.Emit(s.Tick, FactKind.Ignition, At(14, 2), 1, fire, -1, -1);
                s.Facts.Emit(s.Tick, FactKind.FireSpread, At(15, 2), 1, fire, -1, -1);
                s.Facts.Emit(s.Tick, FactKind.Extinguished, At(15, 2), 1, fire, p[2], -1);
                s.Run(Time.Minutes(2));
                long f0 = s.Facts.Total;
                m.CallGatheringNow(s);
                void Keep() { foreach (int a in p) { ref var c = ref m[a].C; c.Sleepy = 0; c.Thirst = 0; c.Hunger = 0; c.Bored = 0; s.Get<Life>()[c.Body].Asleep = 0; } }
                long done = -1;
                for (int k = 0; k < Time.Minutes(40) && done < 0; k++) { s.Step(); Keep(); if (soc.Celebrations > 0) done = s.Tick; }
                for (int k = 0; k < Time.Hours(2); k++) { s.Step(); Keep(); }
                float joyMin = p.Min(a => m[a].C.Joy);
                float MemW(int a) => soc.Memories.Where(x => x.Owner == a && x.Topic == Topic.Joy).Select(x => soc.MemoryWeight(x, s.Tick)).DefaultIfEmpty(0).Max();
                float memMin = p.Min(MemW);
                if (Debug) foreach (int a in p) Console.WriteLine($"    {m.NameOf(a)} 기쁨 {m[a].C.Joy:0.00} · 좋은 기억 {MemW(a):0.00}");
                Check("축하 뒤 좋은 기억이 남는다: 위기를 넘긴 축하 자리 두 시간 뒤에도 모두 기분이 밝고 (기쁨 0.1 넘게) · 축하가 무게 있는 좋은 기억 (0.5 넘게)으로 남는다",
                    done > 0 && joyMin >= 0.1f && memMin >= 0.5f,
                    $"축하 {soc.Celebrations} ({(done > 0 ? Time.Clock(done) : "-")}) · 두 시간 뒤 기쁨 가장 낮은 이 {joyMin:0.00} · 좋은 기억 가장 옅은 이 {memMin:0.00}");
            }
        }
    }
}
