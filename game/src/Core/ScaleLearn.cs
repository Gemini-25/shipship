using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.18 사고 규모 — 겪은 것이 다음을 바꾼다.
//  · 자세한 종류: 사슬 고리의 열쇠(고장 종류 · 질병 · 폭발 · 과열 폭발)를 표의 줄로 읽는다 → 도감이 "설비 고장 몇 번"이 아니라 "단락 몇 번"으로 센다.
//    회로 단락 · 차단기 · 저장장치 고장은 처음부터 계통, 옮는 병은 방 (표의 기본 규모).
//  · 컴퓨터의 교훈: 처음 판정보다 크게 번진 종류는 기억했다가, 다음에 같은 종류가 나면 한 칸 높여 미리 사람을 부른다.
//  · 승무원의 경험: 배 전체 · 우주급을 함께 넘긴 사람은 다음엔 덜 두려워하고(침착해진다), 처음 겪은 사람은 긴장이 남는다.
//  · 위기 단계: 배 전체 사고가 뜨거우면 적어도 비상 (반란 · 우주급 예보는 제 규칙대로).
public sealed partial class ScaleSystem
{
    /// <summary>고리 → 자세한 종류 열쇠 ("" = 없음, 한 번만 읽는다).</summary>
    private readonly Dictionary<int, string> _detail = new();
    /// <summary>컴퓨터의 교훈: 사건 열쇠 → 다음에 미리 대비할 규모.</summary>
    public SortedDictionary<string, IncidentScale> Lessons { get; } = new(StringComparer.Ordinal);
    /// <summary>승무원 Id → 함께 넘긴 배 전체 · 우주급 사건 수.</summary>
    public SortedDictionary<int, int> Veterans { get; } = new();
    public int Wary, LessonsLearned, Steadied, Shaken;

    /// <summary>폭발 · 과열 폭발을 건 코드가 고리에 표의 열쇠를 붙인다 (도감용 — 규모는 피해로 잰다).</summary>
    public void Kind(int node, string key) { if (node >= 0) _detail[node] = key; }

    /// <summary>고리의 자세한 종류 (표에 있는 열쇠만): 고장 "fault:종류" · 질병 "ail:병" · 폭발 "blast:종류" · 과열 폭발 "blow:방식".</summary>
    private string? DetailKey(CauseNode n)
    {
        if (!_detail.TryGetValue(n.Id, out var key))
        {
            key = "";
            if (n.Kind is CauseKind.Fault or CauseKind.Illness && n.Key.Length > 0)
            {
                var p = n.Key.Split(':');
                if (p.Length >= 3 && (p[0] == "fault" || p[0] == "ail")) key = p[0] + ":" + p[2];
            }
            if (key.Length > 0 && ScaleTable.Row(key) == null) key = "";
            _detail[n.Id] = key;
        }
        return key.Length > 0 && ScaleTable.Row(key) != null ? key : null;
    }

    /// <summary>고리 규모에 자세한 종류의 기본 규모를 더한다 (고장 · 질병만 — 폭발은 피해 기록으로).</summary>
    private IncidentScale WithDetail(CauseNode n, IncidentScale s)
    {
        if (n.Kind is not (CauseKind.Fault or CauseKind.Illness) || DetailKey(n) is not string d) return s;
        var b = ScaleTable.OfKey(d);
        return b > s && b <= IncidentScale.System ? b : s;
    }

    /// <summary>배 전체 사고가 뜨거운가 (위기 단계를 비상으로 올린다 — 반란 · 우주급은 빼고).</summary>
    public string? ShipWide()
    {
        foreach (var k in _open)
            if (k.Hot && k.Now >= IncidentScale.Ship && k.CosmicId < 0 && k.Key != "unrest") return $"{ScaleTable.Label(k.Now)} — {k.Name}";
        return null;
    }

    /// <summary>처음 판정에 교훈을 얹는다: 지난번 같은 종류가 크게 번졌으면 한 칸 높여 부른다 (컴퓨터가 있을 때만).</summary>
    private IncidentScale Prepare(ScaleCase k, IncidentScale s, bool computer, out string note)
    {
        note = "";
        if (!computer || k.Steps.Count != 1 || s < IncidentScale.Room || s >= IncidentScale.Ship || k.CosmicId >= 0) return s;
        if (!Lessons.TryGetValue(k.Key, out var to) || to <= s) return s;
        Wary++;
        note = $"지난번 같은 사고가 {ScaleTable.Name(to)}까지 번졌다 — 미리 {ScaleTable.Name(to)} 대비";
        return to;
    }

    /// <summary>사건이 끝났다: 컴퓨터는 낮게 본 것을 기억하고, 함께 넘긴 사람은 경험이 쌓인다.</summary>
    private void Learn(ScaleCase k)
    {
        var w = _w;
        // 컴퓨터: 처음 판정(또는 미리 대비한 규모)보다 크게 번졌다 → 다음엔 한 칸 높여
        if (k.JudgedBy == "주 컴퓨터" && k.Root >= 0 && k.CosmicId < 0 && k.Key != "unrest" && k.Steps.Count > 0)
        {
            var guess = k.Guess > k.Steps[0].To ? k.Guess : k.Steps[0].To;
            if (k.Peak > guess && guess < IncidentScale.Ship)
            {
                var to = (IncidentScale)Math.Min((int)k.Peak, (int)guess + 1);
                if (!Lessons.TryGetValue(k.Key, out var old) || to > old)
                {
                    Lessons[k.Key] = to;
                    LessonsLearned++;
                    var room = k.RoomId >= 0 && k.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[k.RoomId] : null;
                    w.Automation.Reason($"scalelesson:{k.Key}", $"{k.Name} — {ScaleTable.Label(guess)}로 봤는데 {ScaleTable.Label(k.Peak)}까지 번졌다 · 같은 종류는 다음에 {ScaleTable.Name(to)}로 미리 대비한다", SimTime.Hours(6));
                    w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터 교훈 — {ScaleTable.Row(k.Key)?.Name ?? k.Name}: 다음엔 처음부터 {ScaleTable.Label(to)}로 부른다");
                    if (room != null) MarkLog.Add(room.Marks, w.Tick, $"주 컴퓨터: {k.Name} 규모를 낮게 봤다");
                }
            }
        }
        // 승무원: 배 전체 · 우주급을 함께 넘겼다
        if (k.Peak < IncidentScale.Ship) return;
        foreach (int id in k.Feared)
        {
            if (id < 0 || id >= w.Crew.Count) continue;
            var c = w.Crew[id];
            if (c.Dead) continue;
            int n = (Veterans.TryGetValue(id, out var v) ? v : 0) + 1;
            Veterans[id] = n;
            if (n == 1)
            {
                // 처음 겪었다: 긴장이 남는다 (침착한 사람은 덜)
                Memory.Shake(w, c, k.Peak >= IncidentScale.Cosmic ? 0.06f : 0.04f, $"{k.Name} ({ScaleTable.Name(k.Peak)})");
                MarkLog.Add(c.Memory.Marks, w.Tick, $"{ScaleTable.Label(k.Peak)} — {Ko.EulReul(k.Name)} 처음 겪었다");
                Shaken++;
            }
            else
            {
                // 여러 번 넘겼다: 침착해진다
                Memory.Steady(w, c, 0.08f);
                MarkLog.Add(c.Memory.Marks, w.Tick, $"{ScaleTable.Label(k.Peak)} — {k.Name} ({ShipHistory.Times(n)}째 넘겼다)");
                Steadied++;
            }
        }
    }

    /// <summary>두려움 배율: 배 전체를 넘겨 본 사람은 덜 무섭다 (한 번 ×0.6 · 여러 번 ×0.4).</summary>
    public float FearMul(CrewMember c) => Veterans.TryGetValue(c.Id, out var n) ? n >= 2 ? 0.4f : 0.6f : 1f;

    private void HashLearn(Action<long> I)
    {
        I(Lessons.Count); I(Wary); I(LessonsLearned); I(Steadied); I(Shaken);
        foreach (var kv in Lessons) I((int)kv.Value);
        foreach (var kv in Veterans) { I(kv.Key); I(kv.Value); }
    }
}
