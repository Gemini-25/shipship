using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.10 증축: 선체 바깥에 방을 새로 붙인다 — 침실이 모자란 배가 증축을 정하고, 골조 → 외판 → 가압 → 배선 → 침대를 거쳐
// 새 침실이 열리면 사람들이 옮겨 가 잔다 · 공사 중 운석에 골조가 상한다 · 무게로 가속이 준다 · 저장 · 되감기 · 결정론 · 모든 배.
public static partial class Program
{
    private static int RunAnnexTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"증축 점검 (v16.10) · 시드 {seed}\n");

        // ── 1) 침실이 모자란 배 ──
        {
            var w = DayOne(seed, "Hanbit");
            int h0 = w.Ship.Grid.Height, rooms0 = w.Ship.Rooms.Count;
            float evade0 = w.Propulsion.EvadeCost, thrust0 = w.Propulsion.Thrust;
            Player.Scenario(w, "crowded", out _);
            var homeless0 = w.Annex.Homeless().Select(c => c.Id).ToList();
            var stages = new List<AnnexStage>();
            int meteors = 0;
            long lastThrow = -1;
            IncomingMeteor? mt = null;
            AnnexPlan? p = null;
            bool slept = false;
            float maxNoise = 0f;
            int maxOutside = 0;
            long t0 = w.Tick;
            while (w.Tick - t0 < SimTime.TicksPerDay * 7)
            {
                w.Step();
                p = w.Annex.Plans.LastOrDefault(x => x.State is "공사" or "개통") ?? p;
                if (p == null) continue;
                if (p.State == "공사")
                {
                    if (stages.Count == 0 || stages[^1] != p.Stage) stages.Add(p.Stage);
                    maxNoise = MathF.Max(maxNoise, w.Ship.Rooms[p.Site.AttachRoom].Noise);
                    maxOutside = Math.Max(maxOutside, w.Crew.Count(c => c.Outside && w.Annex.Doing(c)));
                    // 골조가 몇 칸 섰을 때 운석 (회피 기동에 비켜 가면 다시 — 세 번까지)
                    if (p.Stage <= AnnexStage.Plating && p.MeteorHits == 0 && meteors < 3 && p.Frame.Count(f => f >= 1f) >= 3
                        && (mt == null || !w.Sensors.Incoming.Contains(mt)) && w.Tick - lastThrow > SimTime.Minutes(40))
                    {
                        int k = Array.FindIndex(p.Frame, f => f >= 1f);
                        mt = Player.Meteor(w, p.Site.Shell[k], 0.6f);
                        meteors++;
                        lastThrow = w.Tick;
                    }
                }
                if (p.State == "개통")
                {
                    var room = w.Ship.Rooms[p.RoomId];
                    if (!slept) foreach (var id in p.MovedIn) if (w.Crew[id].Pose == Pose.Sleeping && w.Crew[id].Room == room) slept = true;
                    if (slept && w.Tick - p.Opened > SimTime.Hours(2) || w.Tick - p.Opened > SimTime.TicksPerDay * 1.5f) break;
                }
            }
            if (p == null)
            {
                Check("증축 안건이 공사까지", false, $"안건 {w.Annex.Plans.Count}: {string.Join(" / ", w.Annex.Plans.Select(x => $"{x.Title}:{x.State}"))}");
            }
            else
            {
                var by = w.Crew[p.Proposer];
                Check("제안 — 간이침대에서 자는 사람(인원 증가)이 안건을 낸다", homeless0.Contains(p.Proposer) && p.Why.Contains("간이침대"),
                    $"{by.Name} · {p.Why} ({p.Source}) · 간이침대 {homeless0.Count}명");
                Check("주 컴퓨터 — 자리 · 무게 → 가속 · 연료 · 전력 · 자재 · 기간 · 위험 · 공정 순서", p.Advice.Contains("가속") && p.Advice.Contains("자재") && p.Advice.Contains("위험") && p.Advice.Contains("순서"),
                    p.Advice.Length > 160 ? p.Advice[..160] + "…" : p.Advice);
                Check("회의가 정했다 (찬성 · 반대 · 기록)", p.Decided > 0 && p.For.Count > p.Against.Count && w.History.Events.Any(e => e.Text.Contains("회의: ") && e.Text.Contains("증축")),
                    $"찬성 {p.For.Count} · 반대 {p.Against.Count}");
                Check("아래 여백에 지었다 — 격자 · 칸 배열이 맞다 (저장 기록의 칸이 처음부터 격자 안)", p.Site.Grow == 0 && p.Site.Y0 + p.Site.Depth + 1 < h0 && w.Body.Floor.Length == w.Ship.Grid.CellCount && w.Paths.CellBody.Length == w.Ship.Grid.CellCount,
                    $"높이 {h0} → {w.Ship.Grid.Height} · 바깥 벽 줄 {p.Site.Y0 + p.Site.Depth + 1} · 방 {rooms0} → {w.Ship.Rooms.Count}");
                var want = new[] { AnnexStage.Frame, AnnexStage.Plating, AnnexStage.Pressure, AnnexStage.Utilities, AnnexStage.FitOut, AnnexStage.Opening };
                int wi = 0;
                foreach (var s in stages) if (wi < want.Length && s == want[wi]) wi++;
                Check("공정 — 골조 → 외판 → 기밀 시험 · 가압 → 배선 · 배관 → 내장 · 침대 → 개통", wi == want.Length && p.State == "개통", string.Join(" → ", stages));
                Check("공사 중 운석에 골조가 상한다 → 재작업 (주 컴퓨터가 선외 공사를 멈춘다)", p.MeteorHits >= 1 && p.Reworks > 0 && w.Annex.Stats.Halts >= 1,
                    $"던진 운석 {meteors} · 맞음 {p.MeteorHits} · 재작업 {p.Reworks}칸 · 선외 중지 {w.Annex.Stats.Halts}번 · 샌 이음 {p.Leaks}");
                Check("선외 골조 — 우주복 입고 밖에서 (드론이 거든다)", w.Annex.Stats.EvaTrips > 0 && maxOutside >= 1 && p.Hands.Count >= 2,
                    $"EVA {w.Annex.Stats.EvaTrips}번 · 밖에 동시에 {maxOutside}명 · 손댄 사람 {p.Hands.Count} · 드론 출격 {p.DroneSorties}");
                var room = p.RoomId >= 0 ? w.Ship.Rooms[p.RoomId] : null;
                bool door = room != null && room.Doors.Any(d => (d.RoomA?.Id == p.Site.AttachRoom || d.RoomB?.Id == p.Site.AttachRoom) && !d.IsExternal);
                int beds = room?.Furniture.Count(f => f.Type == FurnitureType.Bed) ?? 0;
                Check("새 방 — 외벽에 문 · 첫 점등(전력) · 침대", room != null && door && room.Powered && !room.PowerCut && beds == p.Fixtures && p.FirstLight > 0,
                    $"{room?.Name} · 문 {(door ? "있음" : "없음")} · 전력 {(room?.Powered == true ? "들어옴" : "없음")} · 침대 {beds}/{p.Fixtures}");
                Check("새 침실이 열리면 사람들이 옮겨 가 잔다", p.MovedIn.Count >= 2 && slept && w.Annex.Homeless().Count < homeless0.Count,
                    $"옮긴 사람 {string.Join("·", p.MovedIn.Select(id => w.Crew[id].Name))} · 그 방에서 잠 {(slept ? "✔" : "✘")} · 간이침대 {homeless0.Count} → {w.Annex.Homeless().Count}");
                bool chron = w.History.Events.Any(e => e.Text.Contains("증축 개통") && p.Name != null && e.Text.Contains(p.Name) && e.CrewIds.Length >= 2);
                Check("개통식 · 이름 · 지은 사람이 연대기에", p.Celebrated.Count >= 3 && chron && room?.CustomName == p.Name,
                    $"'{p.Name}' ({p.NameSource}) · 개통식 {p.Celebrated.Count}명 · {SimTime.Day(p.Opened)}일 개통");
                Check("무게 → 가속 감소 · 회피 연료 증가", w.Annex.MassMul > 1.005f && w.Propulsion.EvadeCost > evade0 * 1.005f,
                    $"무게 ×{w.Annex.MassMul:0.000} · 추력 {thrust0 * 100:0.#}% → {w.Propulsion.Thrust * 100:0.#}% · 회피 연료 {evade0:0.##} → {w.Propulsion.EvadeCost:0.##}kg");
                Check("공사 소음이 옆 방에 닿는다 · 먼지 · 잠", maxNoise >= 0.35f,
                    $"붙은 방 소음 최대 {maxNoise * 100:0}% · 깬 사람 {w.Annex.Stats.Woken} · 먼지 기다림 {w.Annex.Stats.DustWaits} · 분진 폭발 {w.Annex.Stats.DustBlasts}");

                // 저장 · 불러오기
                string text = SaveGame.Write(w);
                var runner = new ReplayRunner(text);
                while (!runner.Advance(50000)) { }
                var w2 = runner.World;
                var p2 = w2.Annex.Plans.LastOrDefault(x => x.State == "개통");
                Check("저장 · 불러오기 — 같은 증축 (같은 틱에 같은 줄이 붙는다)", runner.Verified && p2 != null && p2.Name == p.Name && w2.Ship.Grid.Height == w.Ship.Grid.Height && w2.Ship.Rooms.Count == w.Ship.Rooms.Count,
                    $"지문 {(runner.Verified ? "같음" : "어긋남")} · {p2?.Name ?? "(없음)"} · 높이 {w2.Ship.Grid.Height}");
                // 되감기: 격자가 자라기 전으로 되감아 다시 흘려도 같은 역사
                long back = Math.Max(SimTime.TicksPerDay + SimTime.Hours(8), p.Decided - SimTime.Hours(1));
                var rw = new ReplayRunner(SaveGame.WriteAt(w, back));
                while (!rw.Advance(50000)) { }
                int hBack = rw.World.Ship.Grid.Height;
                while (rw.World.Tick < w.Tick) rw.World.Step();
                Check("되감기 — 증축 전으로 되감아 다시 흘려도 같은 역사", rw.Rewind && hBack == h0 && SaveGame.StateHash(rw.World) == SaveGame.StateHash(w),
                    $"{SimTime.Day(back)}일 {SimTime.Clock(back)} (높이 {hBack}) → {SaveGame.StateHash(rw.World):x8} / {SaveGame.StateHash(w):x8}");
            }
        }

        // ── 2) 기밀 시험을 건너뛰면 약한 이음이 나중에 터진다 (공사 구역 감압) ──
        {
            bool off = AnnexSystem.ThinkOff;
            AnnexSystem.ThinkOff = true;
            var w = DayOne(seed, "Mirinae");
            var p = w.Annex.Order(RoomType.Quarters, 2);
            if (p == null) Check("기밀 시험 생략 → 이음이 터진다", false, "자리가 없다");
            else
            {
                for (int i = 0; i < p.Frame.Length; i++) { p.Frame[i] = 1f; p.Plate[i] = 1f; p.PlateQ[i] = i == 1 ? 0.4f : 0.8f; }
                long t0 = w.Tick;
                bool rejected = false;
                while (w.Tick - t0 < SimTime.TicksPerDay * 3 && p.Seams == 0)
                {
                    w.Step();
                    if (!rejected && p.CardId >= 0 && w.Automation.Asks.All.FirstOrDefault(x => x.Id == p.CardId) is Proposal pr && pr.State == ProposalState.Pending)
                    {
                        w.Automation.Asks.Decide(pr, false, "관찰자", "급하다 — 간단히");
                        rejected = true;
                    }
                }
                var room = p.RoomId >= 0 ? w.Ship.Rooms[p.RoomId] : null;
                bool hist = w.History.Events.Any(e => e.Text.Contains("이음이 터졌다"));
                Check("기밀 시험 카드를 거절(간단히) → 약한 이음이 남아 → 터진다 (공사 구역 감압)", rejected && p.FullTest == false && p.Seams >= 1 && hist && room != null,
                    $"카드 {(rejected ? "거절" : "없음")} · 이음 {p.Seams} · 방 {room?.Name} 압력 {room?.Air.Pressure:0} · {p.Stage}");
            }
            AnnexSystem.ThinkOff = off;
        }

        // ── 3) 격자를 아래로 키운다: 칸 번호 · 좌표 · 칸 상태가 그대로 (여백을 넘는 증축용) ──
        {
            var w = DayOne(seed, "Hanbit");
            var g = w.Ship.Grid;
            int h = g.Height, n = g.CellCount;
            var floors = Enumerable.Range(0, n).Select(i => w.Body.Floor[i]).ToArray();
            var wear = Enumerable.Range(0, n).Select(i => w.Body.Wear[i]).ToArray();
            var probe = w.Ship.Rooms.First(r => r.Type == RoomType.Mess).Cells[3];
            int idx = g.Index(probe);
            w.Body.SetMark(probe, CellMark.Wet, 0.8f, "시험");
            bool ok = true;
            string err = "";
            float wet = 0f;
            try
            {
                w.Annex.GrowGrid(3);
                wet = w.Body.Mark(probe, CellMark.Wet);
                for (int i = 0; i < n; i++) if (w.Body.Floor[i] != floors[i] || w.Body.Wear[i] != wear[i] || g.CellAt(i) != new Cell(i % g.Width, i / g.Width)) { ok = false; break; }
                Run(w, SimTime.Hours(2));
                var p = w.Annex.Order(RoomType.Storage, 2);
                var room = p != null ? w.Annex.BuildNow(p) : null;
                Run(w, SimTime.Hours(2));
                ok &= room != null;
            }
            catch (Exception e) { ok = false; err = $"{e.GetType().Name}: {e.Message}"; }
            Check("격자를 아래로 키운다 — 칸 번호 · 좌표 · 칸 상태(바닥 · 닳음 · 젖음)가 그대로 · 계통이 따라온다",
                ok && g.Height == h + 3 && g.Index(probe) == idx && wet > 0.3f && w.Body.Floor.Length == g.CellCount && w.Paths.Crawl.Length == g.CellCount,
                $"높이 {h} → {g.Height} · 칸 {n} → {g.CellCount} · {probe} 번호 {idx} → {g.Index(probe)} · 젖음 {wet:0.00} {err}");
        }

        // ── 4) 결정론 ──
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("결정론 — 같은 시드는 같은 지문", a == b, $"{a:x8} / {b:x8}");
            uint G() { var w = DayOne(seed, "Hanbit"); Player.Scenario(w, "crowded", out _); Run(w, SimTime.TicksPerDay * 2); return SaveGame.StateHash(w) ^ (uint)w.Ship.Grid.Height * 2654435761u ^ (uint)w.Annex.Stats.Members; }
            uint c1 = G(), c2 = G();
            Check("결정론 — 증축 공사 중에도 같은 지문", c1 == c2, $"{c1:x8} / {c2:x8}");
        }

        // ── 5) 모든 기존 배에서 증축 한 번 (예외 없이) ──
        {
            bool off = AnnexSystem.ThinkOff;
            AnnexSystem.ThinkOff = true;
            var lines = new List<string>();
            int ok = 0;
            foreach (var t in ShipCatalog.All)
            {
                try
                {
                    var w = World.CreateDefault(seed, 0, t.Key);
                    Run(w, SimTime.Hours(2));
                    var p = w.Annex.Order(RoomType.Quarters, 2);
                    if (p == null) { lines.Add($"{t.Key}: 자리 없음"); continue; }
                    int h = w.Ship.Grid.Height;
                    var room = w.Annex.BuildNow(p);
                    Run(w, SimTime.Hours(3));
                    bool reach = room != null && w.Paths.Flood(p.Site.DoorInner).Reachable(p.Site.DoorOuter);
                    bool arrays = w.Body.Floor.Length == w.Ship.Grid.CellCount;
                    SaveGame.StateHash(w);
                    if (room != null && reach && arrays && p.State == "개통") { ok++; lines.Add($"{t.Key}✔"); }
                    else lines.Add($"{t.Key}: 방 {(room != null ? "있음" : "없음")} · 길 {reach} · 배열 {arrays} · {p.State}");
                }
                catch (Exception e) { lines.Add($"{t.Key}: 예외 {e.GetType().Name} {e.Message}"); }
            }
            Check("모든 기존 배에서 증축 한 번이 예외 없이", ok == ShipCatalog.All.Length, string.Join(" · ", lines));
            AnnexSystem.ThinkOff = off;
        }

        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
