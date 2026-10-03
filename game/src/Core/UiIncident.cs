using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.24 사고 카드 — 원인 · 전조 · 대응 · 쓴 방법 · 흔적 · 누가 기억하나 (읽기만).
// 인과 사슬 · 규모 사건 · 계통 사고(전조가 깃든 설비) · 역사 · 컴퓨터 조치 · 사고 뒤 흔적 · 사람의 기억을 한 장에 모은다.
// 칸마다 실제 기록에서 찾은 줄만 쓰고, 없으면 "없었다"를 그대로 적는다 (꾸미지 않는다).

public sealed class IncidentStory
{
    public static readonly string[] Fields = { "원인", "전조", "대응", "쓴 방법", "흔적", "누가 기억하나" };
    public static readonly string[] FieldIcons = { "why", "omen", "people", "wrench", "patch", "memory" };

    public string Title { get; init; } = "";
    public IncidentScale? Scale { get; init; }
    public long Start { get; init; }
    public long End { get; init; }
    public bool Open { get; init; }
    public List<string>[] Lines { get; } = Enumerable.Range(0, 6).Select(_ => new List<string>()).ToArray();
    /// <summary>칸마다 실제 기록에서 찾은 줄이 있나 (없으면 "없었다" 한 줄).</summary>
    public bool[] Found { get; } = new bool[6];
    public List<string> Cause => Lines[0];
    public List<string> Omen => Lines[1];
    public List<string> Response => Lines[2];
    public List<string> Method => Lines[3];
    public List<string> Traces => Lines[4];
    public List<string> Memory => Lines[5];

    private static readonly string[] Empty =
    {
        "까닭을 아직 모른다", "눈에 띈 기척은 없었다", "아직 아무도 나서지 않았다", "아직 손쓴 것이 없다", "남은 자국이 없다", "아직 이 일을 떠올리는 사람이 없다",
    };

    /// <summary>사고 하나의 카드.</summary>
    public static IncidentStory Of(World w, CauseIncident inc)
    {
        var log = w.Causes;
        var root = log.Node(inc.Root);
        long end = inc.End >= 0 ? inc.End : w.Tick;
        var k = w.Scale.Cases.FirstOrDefault(x => x.Root == inc.Root);
        var mc = w.Major.Cases.FirstOrDefault(x => x.Node >= 0 && log.IncidentOf(x.Node) == inc);
        var rooms = new List<int>();
        foreach (var id in inc.Nodes) { int r = log.Node(id).RoomId; if (r >= 0 && !rooms.Contains(r)) rooms.Add(r); }
        if (k != null) foreach (var r in k.Rooms) if (!rooms.Contains(r)) rooms.Add(r);
        if (mc != null) foreach (var r in mc.Rooms) if (!rooms.Contains(r)) rooms.Add(r);
        string RoomName(int id) => id >= 0 && id < w.Ship.Rooms.Count ? w.Ship.Rooms[id].Name : "?";
        string CrewName(int id) => w.Crew.FirstOrDefault(c => c.Id == id)?.Name ?? "?";

        var s = new IncidentStory
        {
            Title = root.Text, Scale = k?.Peak, Start = inc.Start, End = end, Open = inc.Open,
        };

        // ① 원인
        if (w.Inquiry.FindingFor(inc.Root) is string found) s.Cause.Add(found); // v18.7 조사 결과 · 털어놓은 실수
        if (mc != null) s.Cause.Add(mc.Spec.Cause);
        s.Cause.Add(root.Text);
        if (root.Kind != CauseKind.Hazard || mc == null) s.Cause.Add($"갈래: {ScaleTable.CauseName(root.Kind)}");

        // ② 전조: 계통 사고의 기척 → 그 방의 정비 · 당직 기록 → 컴퓨터가 먼저 본 것
        if (mc != null && (mc.Machine >= 0 || mc.OmenDue >= 0))
            s.Omen.Add(mc.Spec.Omen + (mc.OmenRead ? " — 미리 봤지만 막지 못했다" : " — 아무도 알아채지 못했다"));
        long before = inc.Start - SimTime.Hours(24);
        foreach (var e in w.History.Events)
        {
            if (s.Omen.Count >= 3) break;
            if (e.Tick < before || e.Tick >= inc.Start || e.Kind is not (HistoryKind.Maintenance or HistoryKind.Memory) || !rooms.Contains(e.RoomId)) continue;
            s.Omen.Add($"{SimTime.Clock(e.Tick)} {ChronicleBook.Short(e.Text, 60)}");
        }
        foreach (var a in w.Automation.Book.Acts)
        {
            if (s.Omen.Count >= 4) break;
            if (a.Tick < inc.Start - SimTime.Hours(12) || a.Tick >= inc.Start || a.Kind is not (ActKind.Forecast or ActKind.Advice or ActKind.Alarm) || !rooms.Contains(a.RoomId)) continue;
            s.Omen.Add($"{SimTime.Clock(a.Tick)} 주 컴퓨터 — {ChronicleBook.Short(a.Observe != "" ? a.Observe : a.Act, 56)}");
        }

        // ③ 대응: 누가 판정했나 · 방송 · 나선 사람 · 대응의 고비
        if (k != null)
        {
            if (k.JudgedBy != "") s.Response.Add($"{Ko.IGa(k.JudgedBy)} {Ko.EuRo(ScaleTable.Label(k.Planned))} 봤다");
            if (k.Broadcast != "") s.Response.Add($"방송 — {ChronicleBook.Short(k.Broadcast, 60)}");
            var hands = k.Workers.Take(4).Select(CrewName).ToList();
            if (hands.Count > 0) s.Response.Add($"나선 사람 — {string.Join(" · ", hands)}" + (k.Workers.Count > 4 ? $" 외 {k.Workers.Count - 4}명" : ""));
            if (k.MusterAt >= 0) s.Response.Add($"{SimTime.Clock(k.MusterAt)} 전원 소집" + (k.MusterDone ? " — 모두 모였다" : ""));
        }
        foreach (var e in w.History.Events)
        {
            if (s.Response.Count >= 5) break;
            if (e.Tick < inc.Start || e.Tick > end || e.Kind != HistoryKind.Response || e.RoomId >= 0 && !rooms.Contains(e.RoomId)) continue;
            s.Response.Add($"{SimTime.Clock(e.Tick)} {ChronicleBook.Short(e.Text, 60)}");
        }

        // ④ 쓴 방법: 되돌린 고리(누가 · 무엇으로) · 컴퓨터가 원격으로 한 일 · 원래 설계와 달라진 것
        foreach (var id in inc.Nodes)
        {
            if (s.Method.Count >= 3) break;
            var n = log.Node(id);
            if (n.Kind == CauseKind.Recovery) s.Method.Add(ChronicleBook.Short(n.Text, 64));
            else if (n.ResolvedBy is string by && by != "") s.Method.Add($"{ChronicleBook.Short(n.Text, 36)} — {by}");
        }
        foreach (var a in w.Automation.Book.Acts)
        {
            if (s.Method.Count >= 5) break;
            if (a.Tick < inc.Start || a.Tick > end || !rooms.Contains(a.RoomId)) continue;
            if (a.Kind is not (ActKind.Damper or ActKind.Bulkhead or ActKind.Valve or ActKind.Breaker or ActKind.Suppress or ActKind.Shed or ActKind.Door)) continue;
            s.Method.Add($"주 컴퓨터 — {ChronicleBook.Short(a.Act, 56)}");
        }
        foreach (var e in w.History.Events)
        {
            if (s.Method.Count >= 6) break;
            if (e.Tick < inc.Start || e.Tick > end + SimTime.Hours(12) || e.Kind != HistoryKind.Adaptation || e.RoomId >= 0 && !rooms.Contains(e.RoomId)) continue;
            s.Method.Add(ChronicleBook.Short(e.Text, 64));
        }

        // ⑤ 흔적: 사고 뒤 남은 것 · 계통 사고 자국 · 잃은 것
        foreach (var t in w.After.Traces)
        {
            if (s.Traces.Count >= 3) break;
            if (t.Gone || t.Tick < inc.Start || !rooms.Contains(t.Room)) continue;
            s.Traces.Add(ChronicleBook.Short(t.Text != "" ? t.Text : t.Event, 64));
        }
        foreach (var t in w.After.TraceList()) // 바닥 · 벽에 남은 얼룩 (그을음 · 물 얼룩 …)
        {
            if (s.Traces.Count >= 4) break;
            if (t.Kind == "stain" && rooms.Contains(t.Room)) s.Traces.Add(ChronicleBook.Short(t.Text, 64));
        }
        if (mc != null)
        {
            int marks = w.Major.Traces.Count(t => t.Kind == mc.Kind && t.Tick >= inc.Start && rooms.Contains(t.Room));
            if (marks > 0) s.Traces.Add($"{mc.Spec.Trace} ({marks}곳)");
        }
        foreach (var e in w.History.Events)
        {
            if (s.Traces.Count >= 5) break;
            if (e.Tick < inc.Start || e.Kind != HistoryKind.Structure || !rooms.Contains(e.RoomId)) continue;
            s.Traces.Add(ChronicleBook.Short(e.Text, 64));
        }
        foreach (var p in w.After.Places)
        {
            if (s.Traces.Count >= 6) break;
            if (p.Tick < inc.Start || !rooms.Contains(p.Room)) continue;
            s.Traces.Add(ChronicleBook.Short(p.Text, 64));
        }

        // ⑥ 누가 기억하나: 마음에 남은 줄 · 그 방이 무서워진 사람 · 꿈 · 함께 넘긴 사람
        var who = new List<int>();
        foreach (var c in w.Crew)
        {
            if (s.Memory.Count >= 5) break;
            string? line = null;
            foreach (var mk in c.Memory.Marks)
                if (mk.Tick >= inc.Start && mk.Tick <= end + SimTime.Hours(72) && (rooms.Count == 0 || rooms.Any(r => mk.Text.Contains(RoomName(r)))))
                { line = mk.Text; break; }
            if (line == null)
                foreach (var r in rooms)
                    if (r < c.Memory.Fear.Length && c.Memory.Fear[r] >= 0.2f && c.Memory.FearCause[r] is string why) { line = $"{Ko.IGa(RoomName(r))} 무섭다 — {why}"; break; }
            if (line == null && w.After.Minds.TryGetValue(c.Id, out var am) && am.Last is Dream d && d.Tick >= inc.Start && rooms.Contains(d.Room))
                line = $"꿈에 다시 본다 — {ChronicleBook.Short(d.Text, 40)}";
            if (line == null) continue;
            who.Add(c.Id);
            s.Memory.Add($"{c.Name}{(c.Dead ? "(고인)" : "")} — {ChronicleBook.Short(line, 56)}");
        }
        if (mc != null && s.Memory.Count < 6 && mc.Witnesses.Count > 0)
            s.Memory.Add($"{mc.Spec.Memory} — 본 사람 {string.Join(" · ", mc.Witnesses.Where(id => !who.Contains(id)).Take(3).Select(CrewName))}".TrimEnd(' ', '—'));

        for (int i = 0; i < 6; i++)
        {
            s.Found[i] = s.Lines[i].Count > 0;
            if (!s.Found[i]) s.Lines[i].Add(Empty[i]);
        }
        return s;
    }

}
