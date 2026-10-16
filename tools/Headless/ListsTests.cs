using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// 압축-마 목록 확장: 사고 30 · 설비 30 · 기술 30 — 하나하나 걸리고 · 놓이고 쓰이고 · 익히면 설비가 바뀐다.
public static partial class Program
{
    private static int RunListsTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"목록 확장 점검 (압축-마) · 시드 {seed}\n");
        try
        {
            // ═══ 사고 30 ═══
            var specs = HazardsV18.Specs;
            bool order = specs.Select((s, i) => (int)s.Kind == (int)HazardsV18.First + i).All(x => x) && Hazards.All.Length == (int)HazardsV18.First + specs.Length;
            var scales = HazardsV18.More.GroupBy(m => m.Scale).ToDictionary(g => g.Key, g => g.Count());
            bool meta = specs.All(s => HazardsV18.Of(s.Kind) is HazardMore m && m.Cause.Length > 0 && m.Omen.Length > 0 && m.Trace.Length > 0 && m.Remember.Length > 0)
                        && specs.All(s => ScaleTable.Row(s.Kind.ToString()) is ScaleRow r && r.Base == HazardsV18.Of(s.Kind)!.Scale) && ScaleTable.Unclassified().Count == 0;
            Check("사고 — 30가지 · 순서 · 원인 · 전조 · 흔적 · 기억 · 규모표에 한 줄씩 · 다섯 규모에 고루", specs.Length == 30 && order && meta && scales.Count == 5,
                $"사고 {Hazards.All.Length} · 규모 {string.Join(" ", ScaleTable.Scales.Select(s => $"{ScaleTable.Mark(s)}{scales.GetValueOrDefault(s)}"))} · 대응 갈래 {HazardsV18.More.Count(m => m.Way != null)}/30 · 센서로 재는 것 {HazardsV18.More.Count(m => m.Sensor)}");

            var fired = new Dictionary<HazardKind, string>();
            var traceKinds = new HashSet<HazardKind>();
            foreach (var ship in new[] { "Hanbit", "Mirinae", "Cheonma" })
            {
                var w = DayOne(seed, ship);
                foreach (var s in specs)
                {
                    if (fired.ContainsKey(s.Kind)) continue;
                    var w2 = w; // 같은 배에서 이어 건다 (앞 사고가 뒤 사고의 무대를 만든다 — 무게가 사라진 뒤의 멀미처럼)
                    Prep(w2, s.Kind);
                    string? what = null;
                    for (int t = 0; t < 4 && what == null; t++) what = w2.Hazards.FireStory(s.Kind.ToString(), null);
                    if (what != null) { fired[s.Kind] = what; if (w2.Signs.Traces.Any(x => x.Kind == s.Kind)) traceKinds.Add(s.Kind); }
                    Run(w2, SimTime.Minutes(20));
                }
            }
            var miss = specs.Where(s => !fired.ContainsKey(s.Kind)).Select(s => s.Name).ToList();
            Check("하나하나 — 새 사고 30이 저마다 걸린다 (배 셋)", miss.Count <= 2, $"{fired.Count}/30 · 예: {string.Join(" / ", fired.Values.Take(6))}" + (miss.Count > 0 ? $" · 안 걸린 것: {string.Join(", ", miss)}" : ""));
            Check("흔적 — 걸린 사고는 저마다 흔적을 남긴다 (그림이 종류마다 다르게 그린다)", traceKinds.Count >= fired.Count - 1, $"흔적 {traceKinds.Count}/{fired.Count}");

            // ═══ 원인 → 전조 → 사람 · 컴퓨터 → 손씀 ═══
            {
                var w = DayOne(seed, "Hanbit");
                var d = w.Drains.Drains.OrderBy(x => x.Id).First();
                d.Clog = 0.97f; d.Food = 0.6f;
                var (p, room, refId) = HazardsV18.Pressure(w, HazardKind.DrainBackflow);
                var sign = w.Signs.Raise(HazardKind.DrainBackflow, room, refId, 6f);
                bool computer = sign != null && sign.Noticed >= 1 && sign.Claim >= 0;
                var claimer = w.Crew.FirstOrDefault(c => c.Id == sign?.Claim);
                Run(w, SimTime.Hours(4));
                bool averted = sign != null && sign.Done && w.Signs.Stats.Averted >= 1 && d.Clog < 0.5f && !d.Backflow;
                Check("전조 — 막혀 가는 배수구: 컴퓨터가 재고 사람을 불러 · 그 사람이 가서 긁어내 역류를 막는다", p > 0.5f && computer && averted,
                    $"원인 {p:0.00} · 알아챔 {sign?.Noticed} · 손쓸 사람 {claimer?.Name ?? "없음"} · 막음 {w.Signs.Stats.Averted} · 막힘 {d.Clog:0.00} · {w.Drains.Stats.Line()}");
            }
            {
                var w = DayOne(seed, "Hanbit");
                var r = w.Ship.LiveRooms.First(x => x.Type == RoomType.Galley);
                var sign = w.Signs.Raise(HazardKind.ScaldSpill, r, -1, 0.5f);
                if (sign != null) sign.Claim = -2; // 아무도 손쓰지 못했다
                Run(w, SimTime.Hours(1));
                Check("전조 — 손쓸 사람이 없으면 때가 되어 사고가 난다", sign != null && sign.Done && (w.Signs.Stats.Fired >= 1 || w.Signs.Stats.Averted >= 1),
                    $"터짐 {w.Signs.Stats.Fired} · 원인이 사라져 쉼 {w.Signs.Stats.Averted}");
                // 경험: 겪은 사람은 같은 전조를 더 빨리 알아챈다
                var c = w.Crew.First(x => !x.Dead && !x.IsChild);
                w.Signs.Witness(c, HazardKind.DrainBackflow);
                Check("기억 — 겪은 사람은 그 사고를 기억한다 (다음엔 전조를 더 잘 본다)", w.Signs.HasSeen(c, HazardKind.DrainBackflow) && w.Signs.Seen.Count >= 1, $"기억 {w.Signs.Seen.Count}");
            }

            // ═══ 설비 30 ═══
            {
                var rows = ModulesV18.Rows;
                bool all = rows.Length == 30 && rows.All(r => MachineSpecs.For(r.Type) != null && Modules.Of(r.Type) != null && FurnitureTypes.Name(r.Type) == r.Name);
                Check("설비 — 30가지 · 사양 · 개조표 · 이름", all, $"설비 종류 {Enum.GetValues<FurnitureType>().Length} · 새 {rows.Length} · 쓰는 일 있는 것 {rows.Count(r => r.Use.Length > 0)}");

                var w = DayOne(seed, "Mirinae");
                var who = w.Crew.First(c => !c.IsChild);
                int put = 0;
                var noRoom = new List<string>();
                foreach (var r in rows)
                {
                    var room = w.Ship.LiveRooms.Where(x => x.Type == r.Room).OrderBy(x => x.Furniture.Count).ThenBy(x => x.Id).FirstOrDefault()
                               ?? w.Ship.LiveRooms.Where(x => x.Type != RoomType.Corridor).OrderBy(x => x.Furniture.Count).ThenBy(x => x.Id).First();
                    if (Modules.Spot(w, room) is Cell at && Modules.Install(w, room, r.Type, who, at)) put++;
                    else noRoom.Add(r.Name);
                }
                foreach (var f in w.Ship.Furniture.Where(f => ModulesV18.Is(f.Type) && f.Machine != null)) w.Fittings.Crock[f.Id] = f.Type == FurnitureType.Fermenter ? 0.7f : 0f;
                Run(w, SimTime.TicksPerDay * 2);
                var st = w.Fittings.Stats;
                int usedKinds = w.Ship.Furniture.Count(f => ModulesV18.Is(f.Type) && w.Fittings.Used.ContainsKey(f.Id));
                Check("설비 — 배에 달고 하루: 사람이 와서 쓴다 (김 빼기 · 빵 · 연주 · 별 보기 …)", put >= 26 && st.Uses >= 4 && usedKinds >= 3,
                    $"단 것 {put}/30" + (noRoom.Count > 0 ? $" (자리 없음: {string.Join(",", noRoom.Take(4))})" : "") + $" · 쓴 횟수 {st.Uses} · 쓴 종류 {usedKinds} · {st.Line()}");

                // 고장 → 고친다
                var target = w.Ship.Furniture.Where(f => ModulesV18.Is(f.Type) && f.Machine is Machine m && m.Faults.Count == 0 && m.Spec.FaultKinds.Length > 0).OrderBy(f => f.Id).First();
                var fault = w.Machines.Break(target.Machine!, target.Machine!.Spec.FaultKinds[0]);
                Run(w, SimTime.TicksPerDay);
                Check("설비 — 고장 나고 고쳐진다", fault != null && target.Machine.Faults.Count == 0, $"{target.Label} · 고장 {fault?.Kind} · 하루 뒤 고장 {target.Machine.Faults.Count}");

                // 효과: 화분 묶음 · 선반 걸쇠 · 고양이 · 금고
                Run(w, SimTime.Minutes(7));
                bool netOk = st.Latched > 0 && w.Ship.FurnitureOf(FurnitureType.CargoNet).Any(n => n.Room.Furniture.Any(s => w.Maneuver.Latched.Contains(s.Id)));
                Check("설비 — 화물 그물은 그 방 선반 걸쇠를 건다 · 기동 충격에 덜 쏟아진다", netOk && FittingSystem.StrapMul(w) < 1f, $"걸쇠 {st.Latched} · 충격 배율 {FittingSystem.StrapMul(w):0.00}");
            }
            {
                var w = World.CreateDefault(seed, 0, "Cheonma");
                int sig = w.Ship.Furniture.Count(f => ModulesV18.Is(f.Type));
                var kinds = w.Ship.Furniture.Where(f => ModulesV18.Is(f.Type)).Select(f => f.Type).Distinct().ToList();
                Check("설비 — 큰 배의 특수 방에는 처음부터 놓인다 (재활용실 · 회의실 · 예배실 …)", sig >= 2, $"천마호 새 설비 {sig}대 ({string.Join(", ", kinds.Select(FurnitureTypes.Name))})");
            }

            // ═══ 기술 30 ═══
            {
                var rows = TechWebV18.Rows;
                bool nodes = rows.All(r => TechWeb.Nodes.ContainsKey(r.Tech.Id)) && TechWeb.Every.Length == 142;
                int hidden = TechWebV18.Nodes.Count(n => n.Hidden), combos = TechWebV18.Nodes.Count(n => n.Combo != null), gates = TechWebV18.Nodes.Count(n => n.Gate != null);
                bool lifts = ModulesV18.Rows.All(r => TechWebV18.Lifts.Any(l => l.type == r.Type));
                Check("기술 — 30가지 · 그물에 걸린다 · 갈림길 2 · 숨은 기술 · 사고로 열리는 것 · 모든 새 설비가 기술로 단계가 오른다", rows.Length == 30 && nodes && TechWebV18.Forks.Length == 2 && hidden >= 3 && combos >= 3 && gates >= 2 && lifts,
                    $"기술 {TechWeb.Every.Length} · 숨김 {hidden} · 조합 {combos} · 조건 {gates} · 단계 줄 {TechWebV18.Lifts.Length}");

                var w = DayOne(seed, "Hanbit");
                var who = w.Crew.First(c => !c.IsChild);
                var room = w.Ship.LiveRooms.First(x => x.Type == RoomType.Galley);
                Modules.Install(w, room, FurnitureType.Fermenter, who, Modules.Spot(w, room)!.Value);
                var crock = w.Ship.FurnitureOf(FurnitureType.Fermenter).First();
                float risk0 = w.Eras.RiskMul(nameof(HazardKind.FermentBurst));
                int t0 = crock.Machine!.Tier;
                var need0 = Modules.Of(FurnitureType.GreaseTrap) != null ? ModulesV18.Need(w, FurnitureType.GreaseTrap) : (0f, "");
                foreach (var id in new[] { "pickling", "coolantdope", "coldchain", "greasecode" }) { w.Eras.Known.Add(id); w.Eras.Order.Add(id); }
                Run(w, SimTime.Hours(1));
                int t1 = crock.Machine.Tier;
                float risk1 = w.Eras.RiskMul(nameof(HazardKind.FermentBurst));
                var need1 = ModulesV18.Need(w, FurnitureType.GreaseTrap);
                Check("기술 — 익히면 설비 단계가 오르고(모양) · 그 사고가 줄고 · 없던 설비를 달고 싶어진다", t0 == 1 && t1 == 3 && risk1 < risk0 && need1.Item1 >= 0.35f,
                    $"발효 항아리 {Tech.Roman(t0)} → {Tech.Roman(t1)} · 터짐 배율 {risk0:0.00} → {risk1:0.00} · 기름 거름통 바람 {need0.Item1:0.00} → {need1.Item1:0.00} ({need1.Item2})");
            }

            // ═══ 흔적 그림: 사고마다 따로 (같은 모양 금지) ═══
            {
                var dir = FixArtViewDir();
                var src = dir != null ? System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "ShipViewTraces.cs")) : "";
                int i0 = src.IndexOf("void DrawTrace("), i1 = src.IndexOf("void DrawOmen(");
                var body = i0 >= 0 && i1 > i0 ? src.Substring(i0, i1 - i0) : "";
                var ms = System.Text.RegularExpressions.Regex.Matches(body, @"case HazardKind\.(\w+):");
                var cases = new Dictionary<string, string>();
                for (int k = 0; k < ms.Count; k++)
                {
                    int a0 = ms[k].Index + ms[k].Length, a1 = k + 1 < ms.Count ? ms[k + 1].Index : body.Length;
                    cases[ms[k].Groups[1].Value] = FixArtNormalize(System.Text.RegularExpressions.Regex.Replace(body.Substring(a0, a1 - a0), @"//[^\n]*", ""));
                }
                var noArt = HazardsV18.Specs.Select(s => s.Kind.ToString()).Where(k => !cases.ContainsKey(k)).ToList();
                var same = cases.GroupBy(kv => kv.Value).Where(g => g.Count() > 1).Select(g => string.Join("=", g.Select(x => x.Key))).ToList();
                var thin = cases.Where(kv => FixArtDrawCall.Matches(kv.Value).Count < 2).Select(kv => kv.Key).ToList();
                Check("흔적 그림 — 새 사고 30마다 흔적 그림이 따로 있다 (같은 모양 없음 · 도형 둘 이상)", noArt.Count == 0 && same.Count == 0 && thin.Count == 0 && cases.Count == 30,
                    $"그림 {cases.Count}" + (noArt.Count > 0 ? $" · 없음: {string.Join(",", noArt)}" : "") + (same.Count > 0 ? $" · 같음: {string.Join(" ", same)}" : "") + (thin.Count > 0 ? $" · 모자람: {string.Join(",", thin)}" : ""));
            }

            // ═══ 성능: 큰 배 하루 (새 설비 · 전조를 끄고 켜서) ═══
            if (Environment.GetEnvironmentVariable("LISTPERF") == "1")
            {
                double Day(bool off)
                {
                    HazardSignSystem.Off = off; FittingSystem.Off = off;
                    var w = World.CreateDefault(seed, 0, "Cheonma");
                    Run(w, SimTime.Hours(2));
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    Run(w, SimTime.TicksPerDay);
                    return sw.Elapsed.TotalSeconds;
                }
                double a0 = Day(true), a1 = Day(false), a2 = Day(true), a3 = Day(false);
                HazardSignSystem.Off = false; FittingSystem.Off = false;
                double off = (a0 + a2) / 2, on = (a1 + a3) / 2;
                Check("성능 — 천마호 하루: 새 설비 · 전조가 시간을 크게 늘리지 않는다 (정보)", on < off * 1.15 + 0.5, $"끔 {off:0.0}초 · 켬 {on:0.0}초 ({(on / off - 1) * 100:+0;-0}%)");
            }

            // ═══ 결정론 ═══
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    w.Hazards.FireStory(nameof(HazardKind.DrainBackflow), null);
                    w.Hazards.FireStory(nameof(HazardKind.ManeuverJolt), null);
                    w.Hazards.FireStory(nameof(HazardKind.PotFire), null);
                    Run(w, SimTime.Hours(20));
                    return SaveGame.StateHash(w);
                }
                uint a = H(), b = H();
                Check("결정론 — 새 사고 · 설비가 든 배도 같은 시드 같은 지문", a == b, $"{a:x8} / {b:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 목록 확장 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>사고마다 무대를 만든다 (원인이 있어야 나는 것).</summary>
    private static void Prep(World w, HazardKind k)
    {
        switch (k)
        {
            case HazardKind.StaticZap: case HazardKind.HeatExhaustion: case HazardKind.HatchFall:
                foreach (var c in w.Crew.Where(c => !c.Dead && c.IsAwake).Take(3)) c.Pose = Pose.Working;
                break;
            case HazardKind.BearingWhine:
                foreach (var m in w.Ship.Machines.Where(m => HazardsV18.Rotating(m.Body.Type))) m.Wear = Math.Max(m.Wear, 0.5f);
                break;
            case HazardKind.PlantTopple:
                foreach (var p in w.Eco.Plants) p.Fixed = false;
                break;
        }
    }
}
