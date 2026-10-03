using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v17.6 HUD 2차 · 접근성 · 음악 + v17.9 숨은 것 · 드문 것 · 도감 — --uitest 에 붙는 줄 · --hudtest (패널 배치 규칙).
public static partial class Program
{
    private static void UiV176Checks(int seed)
    {
        // 1) 문제 있는 사람이 위로 (30인 배)
        {
            var w = World.CreateDefault(seed, 30, null);
            Run(w, SimTime.Hours(2));
            var crew = w.Crew.Where(c => !c.Dead).ToList();
            var hurt = crew[crew.Count / 2];
            var hungry = crew[crew.Count - 1];
            hurt.Vitals.Injury = 0.7f;
            hungry.Needs.Food = 0.05f;
            var order = UiCrewList.Order(w);
            int firstClean = order.FindIndex(x => !x.t.Any);
            bool troubledFirst = order.Take(firstClean < 0 ? order.Count : firstClean).All(x => x.t.Any) && order.Skip(firstClean < 0 ? order.Count : firstClean).All(x => !x.t.Any);
            int iHurt = order.FindIndex(x => x.c == hurt), iHungry = order.FindIndex(x => x.c == hungry);
            var groups = UiCrewList.Groups(w, order);
            Check("승무원 목록 — 30인 배에서 다친 사람 · 배고픈 사람이 맨 위 (무거운 순) · 나머지는 역할별 묶음",
                w.Crew.Count >= 30 && troubledFirst && iHurt >= 0 && iHurt < firstClean && iHungry < firstClean && iHurt < iHungry && groups.Sum(g => g.crew.Count) + Math.Max(0, firstClean) == w.Crew.Count && groups.Count >= 3,
                $"사람 {w.Crew.Count} · 위 {firstClean}명 · 다침 {iHurt}번째({UiCrewList.Of(w, hurt).Why}) · 배고픔 {iHungry}번째 · 역할 묶음 {groups.Count}");
        }

        // 2) 색약 팔레트: 사람은 모양 · 채움만으로도 갈리고, 방은 무늬 · 색약에서도 갈린다
        {
            int n = 32;
            bool shapes = true; int colorClose = 0, pairs = 0;
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                {
                    pairs++;
                    if (UiAccess.Mark(a) == UiAccess.Mark(b) && UiAccess.Fill(a) == UiAccess.Fill(b)) shapes = false;
                    if (UiAccess.WorstDelta(UiAccess.CrewHex(a), UiAccess.CrewHex(b)) < 12f) colorClose++;
                }
            var types = Enum.GetValues<RoomType>();
            int roomBad = 0, roomColorClose = 0;
            for (int i = 0; i < types.Length; i++)
                for (int j = i + 1; j < types.Length; j++)
                {
                    if (!UiAccess.RoomsApart(types[i], types[j])) roomBad++;
                    if (UiAccess.WorstDelta(UiAccess.RoomHex(types[i]), UiAccess.RoomHex(types[j])) < 12f) roomColorClose++;
                }
            // 실제 배: 맞닿은 다른 종류 방은 무늬가 다르다
            var ws = World.CreateDefault(seed, 0, "Hanbit");
            int adj = 0, adjSameWeave = 0;
            foreach (var r in ws.Ship.LiveRooms)
                foreach (var o in ws.Ship.LiveRooms)
                {
                    if (o.Id <= r.Id || o.Kind == r.Kind || !r.Cells.Any(c => o.Cells.Any(d => Math.Abs(c.X - d.X) + Math.Abs(c.Y - d.Y) <= 2))) continue;
                    adj++;
                    if (UiAccess.WeaveOf(r.Kind) == UiAccess.WeaveOf(o.Kind) && UiAccess.WorstDelta(UiAccess.RoomHex(r.Kind), UiAccess.RoomHex(o.Kind)) < 12f) adjSameWeave++;
                }
            Check("색약 팔레트 — 사람 32명은 표식 모양 · 채움만으로 모두 갈린다 · 방 70종은 무늬나 색약에서도 남는 색 차이로 갈린다 · 맞닿은 방도",
                shapes && roomBad == 0 && adjSameWeave == 0 && adj > 5,
                $"사람 짝 {pairs}(색만으론 헷갈림 {colorClose}) · 방 짝 못 가름 {roomBad} (색만으론 헷갈림 {roomColorClose}) · 맞닿은 방 짝 {adj} · 못 가름 {adjSameWeave}");
        }

        // 3) 음악 — 분위기 전환 (평시 → 위기 → 평시 → 추모) · 방 스피커 소리가 벽 너머로 먹먹하게 · 경보에 컴퓨터가 끈다
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(1));
            var m = w.Music;
            var seq = new List<MusicMood> { m.Mood };
            var lounge = w.Ship.LiveRooms.Where(r => m.CanPlay(r) && r.Type != RoomType.Corridor).OrderByDescending(r => r.Type == RoomType.Lounge ? 1 : 0).First();
            var tune = m.Start(lounge, 2, w.Crew[0], 120f);
            Run(w, SimTime.Minutes(3));
            float inRoom = 0f, nextDoor = 0f; bool muffled = false; string near = "";
            foreach (var h in w.Hearing.In(lounge)) if (w.Hearing.Sources[h.Src].Kind == Noise.Music) inRoom = Math.Max(inRoom, h.Level);
            foreach (var r in w.Ship.LiveRooms)
            {
                if (r == lounge) continue;
                foreach (var h in w.Hearing.In(r))
                    if (w.Hearing.Sources[h.Src].Kind == Noise.Music && h.Level > nextDoor) { nextDoor = h.Level; muffled = h.Muffled; near = r.Name; }
            }
            Check("음악 — 방 스피커 노래가 소리 전파를 타고 옆방엔 작고 먹먹하게",
                tune != null && inRoom > 0.3f && nextDoor > 0f && nextDoor < inRoom,
                $"{lounge.Name} {inRoom:0.00} · 가장 큰 옆방 {near} {nextDoor:0.00}{(muffled ? " (먹먹)" : " (문 열림)")}");
            w.Alerts.Add(new Alert(w.Tick, "시험 경보", null, AlertLevel.Critical, true));
            Run(w, SimTime.Minutes(2));
            seq.Add(m.Mood);
            bool muted = m.Playing.Count == 0 && m.Stats.ComputerMuted > 0;
            Run(w, SimTime.Minutes(40));
            seq.Add(m.Mood);
            var dead = w.Crew.Last(c => !c.Dead);
            w.KillAway(dead);
            Run(w, SimTime.Minutes(4));
            seq.Add(m.Mood); // 죽음은 치명 경보다
            Run(w, SimTime.Minutes(40));
            seq.Add(m.Mood); // 경보가 가라앉으면 추모
            Check("음악 — 분위기가 평시 → 위기 → 평시 → (죽음) 위기 → 추모로 바뀌고 · 위기엔 컴퓨터가 선내 음악을 끈다",
                seq.SequenceEqual(new[] { MusicMood.Calm, MusicMood.Crisis, MusicMood.Calm, MusicMood.Crisis, MusicMood.Mourning }) && muted && m.History.Count >= 3,
                $"{string.Join(" → ", seq.Select(MusicSystem.MoodName))} · 컴퓨터가 끔 {m.Stats.ComputerMuted} · 기록 {m.History.Count} · 까닭 {m.MoodWhy} · 떠난 사람 {dead.Name} {dead.Dead}");
        }

        // 4) 도감 — 숨은 물건을 곁에서 찾는다 · 창밖에 지나가면 컴퓨터가 알리고 사람이 본다 · 그림은 항목마다 다르다
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(1));
            var cu = w.Curios;
            CurioPlaced? p = null;
            for (int k = 0; k < 72 && !cu.Seen.Contains("music_box"); k++)
            {
                var c = w.Crew.Where(x => x.IsAwake && x.Room != null && x.Room.Type != RoomType.Corridor).OrderBy(x => (x.Id + k) % 7).FirstOrDefault();
                if (c != null)
                {
                    if (p != null && !p.Found) cu.Placed.Remove(p);
                    var cell = c.Room!.Cells.Where(w.Ship.IsWalkable).OrderBy(x => (x.Center - c.Position).Length()).First();
                    p = cu.PlaceForTest("music_box", c.Room, cell);
                }
                Run(w, SimTime.Minutes(10));
            }
            var find = cu.Finds.FirstOrDefault(f => f.Key == "music_box");
            Check("도감 — 곁에 있던 사람이 숨은 오르골을 찾아 도감 · 일지에 남는다",
                find != null && p != null && p.Found && w.Log.Entries.Any(e => e.Text.Contains("오르골")),
                find != null ? $"{w.Crew.First(c => c.Id == find.By).Name} · {SimTime.Clock(find.Tick)} · {find.Note}" : "못 찾음");
            cu.ForcePassing();
            var ev = cu.Passing;
            Run(w, SimTime.Minutes(80));
            Check("도감 — 창밖에 드문 것이 지나가면 주 컴퓨터가 알리고 · 사람이 창가에서 보고 사진이 남는다",
                ev != null && ev.Announced && ev.Seen.Count > 0 && cu.Seen.Contains(ev.Key),
                ev != null ? $"{CurioTable.Of(ev.Key).Name} · 방송 {ev.Announced} · 본 사람 {ev.Seen.Count} · 창가로 감 {cu.Stats.Gathered}" : "없음");
            var arts = CurioTable.All.Select(s => s.Art).ToList();
            var first = CurioTable.All.Select(s => s.Art.Split(';')[0]).ToList();
            Check("도감 — 항목 50개 넘게 · 이름 · 그림이 모두 다르다 (첫 획까지) · 네 칸 모두 있다",
                CurioTable.All.Length >= 50 && arts.Distinct().Count() == arts.Count && first.Distinct().Count() == first.Count && CurioTable.All.Select(s => s.Name).Distinct().Count() == arts.Count
                && Enum.GetValues<CodexShelf>().All(s => CurioTable.All.Any(c => c.Shelf == s)),
                $"항목 {CurioTable.All.Length} · " + string.Join(" · ", Enum.GetValues<CodexShelf>().Select(s => $"{CurioTable.ShelfName(s)} {CurioTable.All.Count(c => c.Shelf == s)}")));
            // 비밀: 지켜 주면 고마워하고 퍼뜨리면 틀어진다
            var owner = w.Crew[0]; var friend = w.Crew[1]; var stranger = w.Crew[2];
            cu.ForceSecret(owner, "s_ration");
            friend.ChangeAffinity(owner, 0.6f);
            cu.Reveal(owner, friend, "시험");
            bool kept = cu.Stats.Kept == 1;
            var owner2 = w.Crew[3];
            cu.ForceSecret(owner2, "s_broke");
            stranger.ChangeAffinity(owner2, -0.5f);
            float before = w.Crew.Where(o => o != owner2 && o != stranger).Sum(o => o.AffinityTo(owner2));
            cu.Reveal(owner2, stranger, "시험");
            float after = w.Crew.Where(o => o != owner2 && o != stranger).Sum(o => o.AffinityTo(owner2));
            Check("비밀 — 가까운 사람은 지켜 주고 · 사이 나쁜 사람은 퍼뜨려 다른 사람의 마음이 식는다",
                kept && cu.Stats.Told == 1 && after < before - 0.05f && cu.Seen.Contains("s_ration") && cu.Seen.Contains("s_broke"),
                $"지켜 줌 {cu.Stats.Kept} · 퍼뜨림 {cu.Stats.Told} · 다른 사람들 마음 합 {before:0.00} → {after:0.00}");
        }

        // 5) 왜 이 값 — 설비 효율 몫을 곱하면 실제 효율과 같다
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(2));
            var ms = w.Ship.Machines.ToList();
            ms[0].Wear = 0.7f; ms[1].Heat = 0.9f;
            int bad = 0; string ex = "";
            foreach (var m in ms)
            {
                var terms = UiWhy.Efficiency(m);
                if (MathF.Abs(UiWhy.Product(terms) - m.Efficiency) > 0.002f) bad++;
                if (ex == "" && terms.Count >= 2) ex = UiWhy.Line(m.Name, m.Efficiency, terms);
            }
            Check("왜 이 값 — 설비마다 효율의 몫(전압 · 마모 · 열 …)을 곱하면 보이는 효율과 같다", bad == 0 && ex != "", $"설비 {ms.Count} · 어긋남 {bad} · 예: {ex}");
        }

        // 6) 결정론: 새 시스템이 같은 시드에 같은 지문
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("결정론 — 음악 · 숨은 것이 들어가도 같은 시드 같은 지문", a == b, $"{a:x8} / {b:x8}");
        }
    }

    /// <summary>--hudtest: 패널 배치 규칙 — 1600×900 · 1280×720 에서 어떤 조합이든 패널끼리 겹치지 않고, 배는 빈자리에.</summary>
    private static int RunHudTest(int seed)
    {
        _fails = 0;
        var w = World.CreateDefault(seed, 0, "Hanbit");
        Run(w, SimTime.Hours(6));
        int minX = w.Ship.Rooms.SelectMany(r => r.Cells).Min(c => c.X), maxX = w.Ship.Rooms.SelectMany(r => r.Cells).Max(c => c.X);
        int minY = w.Ship.Rooms.SelectMany(r => r.Cells).Min(c => c.Y), maxY = w.Ship.Rooms.SelectMany(r => r.Cells).Max(c => c.Y);
        float shipW = (maxX - minX + 1) * 32f, shipH = (maxY - minY + 1) * 32f;
        float sc = Math.Min(280f / shipW, 160f / shipH);
        int modules = w.Automation.Modules.Count;
        foreach (var (sw, sh) in new[] { (1600f, 900f), (1280f, 720f) })
        {
            int combos = 0, bad = 0, outside = 0, shipHit = 0, folded = 0, hidden = 0;
            float minShip = float.MaxValue;
            string ex = "";
            foreach (bool profile in new[] { false, true })
                foreach (float cosmic in new[] { 0f, 30f, 54f, 236f })
                    foreach (bool legend in new[] { false, true })
                        foreach (int props in new[] { 0, 1, 3 })
                            foreach (bool quiet in new[] { false, true })
                                foreach (bool userFold in new[] { false, true })
                                {
                                    var n = new HudNeeds
                                    {
                                        W = sw, H = sh, TopBarW = 560f, ProfileW = profile ? 440f : 0f, ProfileH = profile ? 80f : 0f, CosmicH = cosmic,
                                        LegendW = legend ? 170f : 0f, LegendH = legend ? 118f : 0f, ComputerH = UiLayout.ComputerHeight(modules, props, false), ComputerUserFolded = userFold,
                                        LogH = quiet ? 36f : 190f, MinimapW = shipW * sc + 24f, MinimapH = shipH * sc + 36f, Voyage = true, ShipW = shipW, ShipH = shipH,
                                    };
                                    var p = UiLayout.Plan(n);
                                    combos++;
                                    var o = p.Overlaps();
                                    if (o.Count > 0) { bad++; if (ex == "") ex = o[0]; }
                                    if (p.Panels().Any(x => !x.r.Inside(sw, sh))) outside++;
                                    if (p.Panels().Any(x => x.r.Intersects(p.ShipArea))) shipHit++;
                                    if (p.ComputerFolded && !userFold) folded++;
                                    if (p.ComputerHidden) hidden++;
                                    minShip = Math.Min(minShip, Math.Min(p.ShipArea.W / (shipW / shipH), p.ShipArea.H));
                                }
            Check($"패널 배치 {sw:0}×{sh:0} — {combos}가지 조합에서 패널 사각형끼리 겹치지 않고 화면 안 · 배 자리는 패널과 안 겹친다",
                bad == 0 && outside == 0 && shipHit == 0 && minShip >= 120f,
                $"겹침 {bad}{(ex != "" ? $" ({ex})" : "")} · 화면 밖 {outside} · 배 자리 침범 {shipHit} · 자리 모자라 접음 {folded} · 숨김 {hidden} · 배가 들어가는 높이 최소 {minShip:0}px");
        }
        // 주 컴퓨터 카드 속: 아이콘 줄 아래에 글줄 (겹치지 않게) · 높이 = 줄 합
        {
            int rows = UiLayout.IconRows(modules, UiLayout.ComputerW);
            float iconBottom = 10f + (rows - 1) * UiLayout.CompIconRow + 7f; // 마지막 줄 중심 + 반지름
            float bookTop = rows * UiLayout.CompIconRow + UiLayout.CompIconPad + 11f - 9f; // 글줄 기준선 - 글자 높이
            float sum = UiLayout.CompHead + UiLayout.CompNow + UiLayout.CompLoad + rows * UiLayout.CompIconRow + UiLayout.CompIconPad + UiLayout.CompBookLine + UiLayout.CompBrain + UiLayout.CompButtons + 6f;
            Check("주 컴퓨터 카드 — 설비 아이콘 줄과 '오늘 조치' 줄이 겹치지 않고 · 카드 높이가 내용 높이와 같다",
                bookTop > iconBottom && Math.Abs(sum - UiLayout.ComputerHeight(modules, 0, false)) < 0.5f && UiLayout.IconsPerRow(UiLayout.ComputerW) * rows >= Math.Min(modules, UiLayout.IconsPerRow(UiLayout.ComputerW) * 3),
                $"설비 {modules} · 줄 {rows} · 아이콘 아래 {iconBottom:0} < 글 위 {bookTop:0} · 높이 {sum:0}");
        }
        // 상태 칩 접기
        {
            var widths = new[] { 90f, 70f, 80f, 70f, 60f, 110f, 60f, 140f, 90f };
            int fit = UiLayout.FitChips(widths, 420f, 16f, 18f, 46f);
            float used = 32f + widths.Take(fit).Sum() + 18f * fit + (fit < widths.Length ? 46f : 0f);
            Check("상태 줄 — 자리가 모자라면 뒤쪽 칩을 '+n' 하나로 접어 오른쪽 칸에 닿지 않는다", fit < widths.Length && used <= 420f + 18f, $"{widths.Length}개 중 {fit}개 · 쓴 너비 {used:0}/420");
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 화면 배치 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
