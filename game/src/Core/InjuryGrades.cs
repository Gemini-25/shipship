using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// 부상 등급 — 다친 사람을 한눈에 가른다 (경상 · 중상 · 위중).
//   경상: 스스로 감고 하던 일을 한다 (손 · 발이 조금 느리다)
//   중상: 뼈가 부러졌거나 · 으스러졌거나 · 깊이 찢겨 피가 나거나 · 넓게 데었다 — 치료를 받아야 낫는다
//   위중: 지금 손대지 않으면 죽는다 — 심정지 · 멎지 않는 출혈 · 다쳐서 쓰러짐 · 체력 바닥
// 등급이 오르는 순간: 본인과 곁의 사람이 안다 (기록 · 기억) · 주컴퓨터가 생체 신호로 알아채면 다친 사람들을 등급순으로 방송한다 (위중부터).
// 치료 일감은 등급이 높을수록 앞선다 (같은 순간 둘이 다치면 위중한 쪽부터).
// 등급이 내려가는 순간: 위중을 벗어나면 "고비를 넘겼다" — 연대기에 남는다.
public enum InjuryGrade : byte { None, Minor, Serious, Critical }

public sealed class InjuryGradeSystem
{
    private readonly World _w;
    private readonly Dictionary<int, InjuryGrade> _now = new();
    private readonly Dictionary<int, InjuryGrade> _peak = new(); // 이번에 다친 동안 가장 높았던 등급 (다 나으면 지운다)
    public readonly int[] Rises = new int[4];   // 등급별로 그 등급에 오른 횟수 (경상 · 중상 · 위중)
    public readonly int[] Peaks = new int[4];   // 다 나았을 때(또는 숨졌을 때) 그 다침의 가장 높은 등급
    public int Eased, Broadcasts;
    private long _lastCast = -1;
    private string _lastCastText = "";

    public InjuryGradeSystem(World w) => _w = w;

    public static string Name(InjuryGrade g) => g switch { InjuryGrade.Minor => "경상", InjuryGrade.Serious => "중상", InjuryGrade.Critical => "위중", _ => "" };

    /// <summary>지금 이 사람의 부상 등급 (다친 데가 없으면 None).</summary>
    public static InjuryGrade Of(World w, CrewMember c)
    {
        if (c.Dead) return InjuryGrade.None;
        var v = c.Vitals;
        var t = w.Casualty.Of(c);
        bool hurt = v.Injury > 0.03f || t != null;
        if (t != null && (t.Kind == TraumaKind.Arrest || w.Casualty.Urgent(c))) return InjuryGrade.Critical;
        if (hurt && (c.Down || v.Health < 0.25f)) return InjuryGrade.Critical;
        if (t != null) return InjuryGrade.Serious;
        if (v.Injury >= 0.3f) return InjuryGrade.Serious;
        foreach (var x in v.Wounds)
            if (!x.Lost && x.Kind is WoundKind.Fracture or WoundKind.Crush or WoundKind.Burn && Wounds.Severity(v, x) >= 0.15f) return InjuryGrade.Serious;
        return v.Injury > 0.03f ? InjuryGrade.Minor : InjuryGrade.None;
    }

    public InjuryGrade Now(CrewMember c) => _now.TryGetValue(c.Id, out var g) ? g : InjuryGrade.None;

    /// <summary>어디를 어떻게 — 등급 옆에 붙는 짧은 말.</summary>
    public string Detail(CrewMember c)
    {
        var w = _w;
        var bits = new List<string>();
        if (w.Casualty.Of(c) is Trauma t) bits.Add(CasualtySystem.KindWord(t.Kind));
        var worst = c.Vitals.Wounds.Where(x => !x.Lost).OrderByDescending(x => Wounds.Severity(c.Vitals, x)).FirstOrDefault();
        if (worst != null && Wounds.Severity(c.Vitals, worst) > 0.03f) bits.Add($"{Wounds.PartName(worst.Part)} {Wounds.KindName(worst.Kind)}");
        else if (c.Vitals.InjuryCause is string ic && c.Vitals.Injury > 0.03f) bits.Add(ic);
        if (c.Down) bits.Add("의식 없음");
        return string.Join(" · ", bits);
    }

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick % SimTime.Minutes(1) >= World.SystemInterval) return;
        List<(CrewMember c, InjuryGrade g)>? risen = null;
        foreach (var c in w.Crew)
        {
            var prev = Now(c);
            var g = Of(w, c);
            if (c.Dead)
            {
                if (_peak.TryGetValue(c.Id, out var pk)) { Peaks[(int)pk]++; _peak.Remove(c.Id); }
                _now.Remove(c.Id);
                continue;
            }
            if (g == prev) continue;
            if (g == InjuryGrade.None) _now.Remove(c.Id); else _now[c.Id] = g;
            var peak = _peak.TryGetValue(c.Id, out var p0) ? p0 : InjuryGrade.None;
            if (g > peak) _peak[c.Id] = g;
            if (g == InjuryGrade.None && peak != InjuryGrade.None) { Peaks[(int)peak]++; _peak.Remove(c.Id); }
            if (g > prev)
            {
                Rises[(int)g]++;
                Rose(c, prev, g);
                if (g >= InjuryGrade.Serious) (risen ??= new()).Add((c, g));
            }
            else if (prev == InjuryGrade.Critical && g != InjuryGrade.None)
            {
                Eased++;
                w.Log.Add(w.Tick, LogKind.Ship, $"{c.Name} — 고비를 넘겼다 (이제 {Name(g)})", c.Id);
                w.History.Add(w, HistoryKind.Casualty, $"{Ko.IGa(c.Name)} 고비를 넘겼다 — 위중에서 {Name(g)}으로", c.Room, new[] { c });
                MarkLog.Add(c.Memory.Marks, w.Tick, "고비를 넘겼다");
            }
        }
        if (risen != null) Cast();
    }

    private void Rose(CrewMember c, InjuryGrade from, InjuryGrade g)
    {
        var w = _w;
        string what = Detail(c);
        string line = $"{c.Name} — {Name(g)}" + (what != "" ? $" ({what})" : "");
        w.Log.Add(w.Tick, g >= InjuryGrade.Serious ? LogKind.Warning : LogKind.Work, line, c.Id);
        if (g < InjuryGrade.Serious) return;
        if (c.Job?.Activity is MeetingActivity) c.Interrupt(w); // 회의 자리에서 일어나 눕는다 (치료를 기다린다)
        w.History.Add(w, HistoryKind.Casualty, $"{Ko.IGa(c.Name)} {(g == InjuryGrade.Critical ? "위중하다" : "크게 다쳤다")}" + (what != "" ? $" — {what}" : ""), c.Room, new[] { c });
        MarkLog.Add(c.Memory.Marks, w.Tick, $"{Name(g)} — {what}");
        // 곁에서 본 사람: 무섭다 (위중이면 더) — 그 방이 그 사람의 기억에 남는다
        if (c.Room is Room r)
            foreach (var o in w.Crew)
                if (o != c && !o.Dead && !o.Down && o.Room == r && o.Pose != Pose.Sleeping)
                    Memory.Frighten(w, o, r, g == InjuryGrade.Critical ? 0.12f : 0.06f, $"{Ko.IGa(c.Name)} {(g == InjuryGrade.Critical ? "쓰러져 숨을 몰아쉬는" : "크게 다친")} 걸 봤다");
    }

    /// <summary>주컴퓨터: 생체 신호가 닿는 방의 다친 사람을 등급순으로 한 번에 알린다 (위중부터 · 같은 말은 되풀이하지 않는다).</summary>
    private void Cast()
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline) return;
        var seen = w.Crew.Where(c => !c.Dead && Now(c) >= InjuryGrade.Serious && c.Room is Room r && r.DataLinked)
            .OrderByDescending(c => Now(c)).ThenBy(c => c.Id).ToList();
        if (seen.Count == 0) return;
        int crit = seen.Count(c => Now(c) == InjuryGrade.Critical), ser = seen.Count - crit;
        string Who(CrewMember c) => $"{c.Name}({c.Room!.Name})";
        string text = (crit > 0 ? $"위중 {crit} — {string.Join(" · ", seen.Where(c => Now(c) == InjuryGrade.Critical).Select(Who))}" : "")
                      + (crit > 0 && ser > 0 ? " / " : "") + (ser > 0 ? $"중상 {ser} — {string.Join(" · ", seen.Where(c => Now(c) == InjuryGrade.Serious).Select(Who))}" : "")
                      + (crit > 0 ? " · 위중한 사람부터" : "");
        if (text == _lastCastText && w.Tick - _lastCast < SimTime.Minutes(20)) return;
        _lastCast = w.Tick; _lastCastText = text;
        Broadcasts++;
        a.Speak.Announce(a.Voice.Style($"다친 사람 — {text}"), seen[0].Room, crit > 0 ? 2 : 1); // 위중이 있을 때만 하던 일을 멈추게 한다 (중상만이면 알리기만 — 치료 일감이 이미 앞선다)
    }

    /// <summary>치료 일감의 급함에 더하는 몫 — 위중한 사람부터.</summary>
    public float UrgencyBonus(CrewMember c) => Now(c) switch { InjuryGrade.Critical => 0.3f, InjuryGrade.Serious => 0.1f, _ => 0f };

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var kv in _now.OrderBy(k => k.Key)) { I(kv.Key); I((int)kv.Value); }
        foreach (var x in Rises) I(x);
        I(Eased); I(Broadcasts);
    }
}
