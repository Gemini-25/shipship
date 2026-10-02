using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.24 점검 항해 고치기 1차 시험 (--auditfixtest)
//   AUDITFIX_PROBE=배,일수 를 주면 진단만: 사고마다 사람 체력이 어디까지 내려갔나.
public static partial class Program
{
    private static int RunAuditFixTest(int seed)
    {
        if (Environment.GetEnvironmentVariable("AUDITFIX_PROBE") is string probe) return AuditFixProbe(seed, probe);
        if (Environment.GetEnvironmentVariable("AUDITFIX_STALL") is string stall) return AuditFixStall(seed, stall);
        if (Environment.GetEnvironmentVariable("AUDITFIX_AUX") is string aux) return AuditFixAux(seed, aux);
        _fails = 0;
        Console.WriteLine($"점검 항해 고치기 시험 (v16.24) · 시드 {seed}\n");
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }

    private static int AuditFixProbe(int seed, string spec)
    {
        var parts = spec.Split(',');
        Storyteller.PersonaValue = 1f; Storyteller.LevelValue = 3f;
        var w = World.CreateDefault(seed, 0, parts[0]);
        var seen = new Dictionary<int, ScaleCase>();
        w.CrewCanDie = true;
        float days = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 3f;
        long total = (long)(days * SimTime.TicksPerDay);
        var minHp = new Dictionary<int, (float hp, float ox, string who, string room, long t)>();
        var caseMin = new Dictionary<int, (float hp, float ox, string who)>();
        for (long t = 1; t <= total; t++)
        {
            w.Step();
            if (t % SimTime.Minutes(1) != 0) continue;
            foreach (var k in w.Scale.Cases) seen[k.Id] = k;
            var open = w.Scale.Cases.Where(k => k.End < 0 || w.Tick - k.End < SimTime.Minutes(30)).ToList();
            foreach (var c in w.Crew)
            {
                if (c.Dead) continue;
                float hp = c.Vitals.Health, ox = c.Vitals.Oxygen;
                if (!minHp.TryGetValue(c.Id, out var m) || hp < m.hp) minHp[c.Id] = (hp, ox, c.Name, c.Room?.Name ?? "밖", w.Tick);
                foreach (var k in open)
                {
                    bool hit = k.CrewId == c.Id || (c.Room != null && (k.RoomId == c.Room.Id || k.Rooms.Contains(c.Room.Id))) || k.Peak >= IncidentScale.Ship;
                    if (!hit) continue;
                    if (!caseMin.TryGetValue(k.Id, out var cm) || hp < cm.hp) caseMin[k.Id] = (hp, MathF.Min(ox, cm.hp == 0 ? 1f : cm.ox), c.Name);
                }
            }
        }
        string H(long t) => $"{(t - SimTime.Hours(7)) / (float)SimTime.TicksPerHour:0.0}h";
        foreach (var k in seen.Values.OrderBy(k => k.Id).Where(k => k.Peak >= IncidentScale.Room))
        {
            var cm = caseMin.GetValueOrDefault(k.Id, (1f, 1f, "-"));
            float hrs = ((k.End < 0 ? w.Tick : k.End) - k.Start) / (float)SimTime.TicksPerHour;
            Console.WriteLine($"[{k.Peak}] {H(k.Start)} {k.Key} {k.Name} · {hrs:0.0}h · 방 {k.RoomId}+{k.Rooms.Count} · 최저 체력 {cm.Item1:0.00} 산소 {cm.Item2:0.00} ({cm.Item3})");
        }
        foreach (var kv in minHp.OrderBy(x => x.Value.hp)) Console.WriteLine($"  {kv.Value.who}: 최저 {kv.Value.hp:0.00} 산소 {kv.Value.ox:0.00} @ {kv.Value.room} {H(kv.Value.t)}");
        foreach (var c in w.Crew)
            foreach (var wd in c.Vitals.Wounds) Console.WriteLine($"  상처 {c.Name}: {wd.Cause} {wd.Kind} {wd.Weight:0.00}");
        Console.WriteLine($"사망 {w.Crew.Count(c => c.Dead)} · 쓰러짐 {w.Crew.Sum(c => c.Stats.TimesDown)}");
        return 0;
    }

    private static int AuditFixStall(int seed, string spec)
    {
        var parts = spec.Split(',');
        Storyteller.PersonaValue = 1f; Storyteller.LevelValue = 3f;
        var w = World.CreateDefault(seed, 0, parts[0]);
        w.CrewCanDie = true;
        float days = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 3f;
        long total = (long)(days * SimTime.TicksPerDay);
        var claims = new Dictionary<int, (int who, long since, float prog)>();
        var shown = new HashSet<int>();
        string H(long t) => $"{(t - SimTime.Hours(7)) / (float)SimTime.TicksPerHour:0.0}h";
        for (long t = 1; t <= total; t++)
        {
            w.Step();
            if (t % SimTime.Minutes(10) != 0) continue;
            foreach (var o in w.Board.All)
            {
                if (o.Closed || o.Assignee == null) { claims.Remove(o.Id); continue; }
                if (!claims.TryGetValue(o.Id, out var c) || c.who != o.Assignee.Id || MathF.Abs(c.prog - o.Progress) > 1e-4f) { claims[o.Id] = (o.Assignee.Id, w.Tick, o.Progress); continue; }
                float hours = (w.Tick - c.since) / (float)SimTime.TicksPerHour;
                if (hours < 1f || (hours > 1.2f && hours < 3f) || (hours > 3.2f)) continue;
                var a = o.Assignee;
                Console.WriteLine($"{H(w.Tick)} [{hours:0.0}h] #{o.Id} {o.Title} 진척 {o.Progress:0.00} 막힘 {(o.BlockedUntil > w.Tick ? o.BlockedReason : "-")} · {a.Name}: 자세 {a.Pose} 일 {a.Job?.Label ?? "-"} (주문 {(a.Job?.Order?.Id.ToString() ?? "-")}) 방 {a.Room?.Name} 할수 {a.CanAct} 깨어 {a.IsAwake}");
            }
        }
        return 0;
    }

    private static int AuditFixAux(int seed, string spec)
    {
        var parts = spec.Split(',');
        Storyteller.PersonaValue = 1f; Storyteller.LevelValue = 3f;
        var w = World.CreateDefault(seed, 0, parts[0]);
        w.CrewCanDie = true;
        float from = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), to = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        var ess = w.Ship.Rooms.Where(r => !r.Detached && (r.Type is RoomType.LifeSupport or RoomType.Bridge or RoomType.Medbay || r.Furniture.Any(f => f.Type == FurnitureType.MainComputer))).ToList();
        long end = SimTime.Hours(7) + (long)(to * SimTime.TicksPerHour), start = SimTime.Hours(7) + (long)(from * SimTime.TicksPerHour);
        string H(long t) => $"{(t - SimTime.Hours(7)) / (float)SimTime.TicksPerHour:0.00}h";
        while (w.Tick < end)
        {
            w.Step();
            if (w.Tick < start || w.Tick % SimTime.Minutes(2) != 0) continue;
            var p = w.Power;
            var dark = ess.Where(r => !r.Powered && !r.Detached).ToList();
            if (dark.Count == 0) continue;
            var auxF = w.Ship.FurnitureOf(FurnitureType.AuxGenerator).FirstOrDefault(f => !f.Room.Detached);
            var tr = w.Automation.TriageOrNull;
            Console.WriteLine($"{H(w.Tick)} 어두운: {string.Join(",", dark.Select(r => $"{r.Name}(회로{r.Circuit} 급전{p.CircuitFed[Math.Clamp(r.Circuit,0,3)]} 끊{r.PowerCut} 차{r.BreakerOff} 연{r.PowerLinked})"))} · 원자로 {p.ReactorOnline} 배터리 {p.BatteryPercent:0.00} 흐름 {p.BatteryFlow:0.0} 수요 {p.Demand:0.0}/{p.Delivered:0.0} 부족 {p.DeficitSince >= 0} · 보조 {(auxF == null ? "없음" : $"{auxF.Room.Name} 돌{p.AuxRunning} 연료{p.AuxFuel:0} 멈춤{auxF.Machine?.Stopped} 데이터{auxF.Room.DataLinked} 고장{auxF.Machine?.Faults.Count}")} 손필요 {tr?.AuxNeedsHands} · 주문 {string.Join(",", w.Board.Open.Where(o => o.Kind is WorkKind.StartAux or WorkKind.ResetBreaker or WorkKind.BreakerOn or WorkKind.RestoreCircuit).Select(o => o.Title + "/" + (o.Assignee?.Name ?? "-")))}");
        }
        return 0;
    }
}
