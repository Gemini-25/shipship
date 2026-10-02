using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ShipSim.Core;

/// <summary>관찰자가 한 일 하나 (그 틱에, 무엇을).</summary>
public sealed record PlayerCommand(long Tick, string Kind, string Arg)
{
    public override string ToString() => $"{Tick} {Kind} {Arg}".TrimEnd();
}

/// <summary>
/// 관찰자의 손. 사고를 일으키는 건 전부 여기를 거친다: 일으키면서 기록해 두면
/// 저장은 "시드 + 이 기록"만으로 끝나고, 불러오기는 같은 시드에서 같은 틱에 같은 일을 다시 하면 된다 (결정론).
/// </summary>
public static class Player
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static void Record(World w, string kind, string arg = "")
    {
        // v10.3: 되감은 역사를 다시 흘리는 중에 관찰자가 새 사고를 일으키면, 거기서 역사가 갈라진다 (뒤에 있던 사고는 일어나지 않는다)
        if (!w.ApplyingRecord && w.Scheduled.Count > 0)
        {
            int dropped = w.Scheduled.Count;
            w.Scheduled.Clear();
            w.Log.Add(w.Tick, LogKind.Ship, $"관찰자가 새 사고를 일으켰다 — 되감은 역사에서 갈라진다 (원래 뒤에 있던 사고 {dropped}건은 일어나지 않는다)");
        }
        w.Commands.Add(new PlayerCommand(w.Tick, kind, arg));
    }

    /// <summary>v10.1: 운석을 던진다 — 몇 분 동안 날아와 부딪힌다 (그 사이 누가 먼저 보느냐는 통신실이 정한다).</summary>
    public static IncomingMeteor? Meteor(World w, Cell target, float size)
    {
        Record(w, "meteor", $"{target.X} {target.Y} {size.ToString("R", Inv)}");
        var m = w.Sensors.Launch(target, size);
        if (m != null) m.ByObserver = true; // v12.2 관찰자가 던진 운석
        if (m != null) w.History.NoteCause(w, $"{(size >= 0.7f ? "큰" : "작은")} 운석({m.Room?.Name ?? "선체"})");
        return m;
    }

    public static bool Fire(World w, Cell cell)
    {
        Record(w, "fire", $"{cell.X} {cell.Y}");
        w.Causes.ObserverNext = true;
        bool ok = Incidents.Fire(w, cell);
        w.Causes.ObserverNext = false;
        if (ok) w.History.NoteCause(w, $"화재({w.Ship.RoomAt(cell)?.Name ?? "?"})");
        return ok;
    }

    public static bool Break(World w, Furniture f)
    {
        Record(w, "break", f.Id.ToString(Inv));
        bool ok;
        using (w.Causes.Observed()) ok = Incidents.Break(w, f);
        if (ok) w.History.NoteCause(w, $"고장({f.Label})");
        return ok;
    }

    public static bool BreakAll(World w, string typeName, bool all, FaultKind? kind = null)
    {
        Record(w, "breaktype", $"{typeName} {(all ? 1 : 0)}" + (kind != null ? $" {kind}" : ""));
        var targets = w.Ship.Furniture.Where(f => f.Type.ToString() == typeName && f.Machine != null).ToList();
        using (w.Causes.Observed())
            foreach (var t in all ? targets : targets.Take(1)) w.Machines.Break(t.Machine!, kind);
        if (targets.Count > 0) w.History.NoteCause(w, $"고장({targets[0].Name})");
        return targets.Count > 0;
    }

    /// <summary>v9: 배관을 터뜨린다 (그 칸 가까운 관).</summary>
    public static PipeSegment? PipeBurst(World w, Cell near, float severity)
    {
        Record(w, "pipe", $"{near.X} {near.Y} {severity.ToString("R", Inv)}");
        PipeSegment? s;
        int pnode = w.Causes.Root(CauseKind.Hazard, "배관 파손", w.Ship.RoomAt(near), near.Center, observer: true);
        using (w.Causes.Because(pnode)) s = w.Piping.Burst(near, severity);
        if (s == null) w.Causes.Discard(pnode);
        else w.Causes.Until(pnode, () => s.Integrity >= 0.6f, "배관을 고쳤다", $"room:{w.Ship.RoomAt(near)?.Id ?? -1}");
        if (s != null) w.Causes.Node(pnode).Text = $"배관 파손 — {s.Name}";
        if (s != null) w.History.NoteCause(w, $"배관 파손({s.Name})");
        return s;
    }

    public static bool Scenario(World w, string name, out Room? focus)
    {
        Record(w, "scenario", name);
        // v12.2 인과 사슬: 시나리오 하나가 뿌리
        int node = w.Causes.Root(CauseKind.Hazard, $"시나리오 — {name}", null, null, observer: true);
        bool ok;
        using (w.Causes.Because(node)) ok = Scenarios.Apply(w, name, out focus);
        if (!ok) w.Causes.Discard(node);
        return ok;
    }

    public static void Scarcity(World w)
    {
        Record(w, "scarcity");
        Scenarios.Scarcity(w);
    }

    public static void AllowDeath(World w, bool on)
    {
        Record(w, "death", on ? "1" : "0");
        w.CrewCanDie = on;
    }

    /// <summary>v13.4: 방침을 손으로 정한다 (시험·화면 시험 — 기록되어 되감기·불러오기가 같은 틱에 같은 방침으로). id가 "*"이면 모두 처음 값으로.</summary>
    public static void Policy(World w, string id, int value)
    {
        Record(w, "policy", $"{id} {value.ToString(Inv)}");
        if (id == "*") w.Policies.ResetDefaults();
        else w.Policies.Set(id, value, "관찰자가 정했다");
    }

    /// <summary>v10.7: 항해 중에 밸런스 수치를 바꾼다 (기록되어 되감기·불러오기가 같은 틱에 같은 값으로 바꾼다).</summary>
    public static bool Tune(World w, string key, float value)
    {
        if (Tuning.Find(key) is not TuningEntry e) return false;
        Record(w, "tune", $"{key} {value.ToString("R", Inv)}");
        float before = e.Get();
        Tuning.Apply(key, value);
        w.Log.Add(w.Tick, LogKind.Ship, $"관찰자가 수치를 바꿨다: {e.Label} {before:0.###} → {e.Get():0.###}");
        return true;
    }

    /// <summary>v11.2: 사고 목록의 사고를 건다 (칸 또는 승무원·로봇 번호). 걸었으면 무엇을 했는지.</summary>
    public static string? Hazard(World w, HazardKind kind, Cell at, int id = -1)
    {
        Record(w, "hazard", $"{kind} {at.X} {at.Y} {id}");
        w.Causes.ObserverNext = true;
        return Hazards.Apply(w, kind, at, id);
    }

    /// <summary>불러올 때: 기록된 일을 그대로 다시 한다 (다시 기록도 된다 → 불러온 뒤 또 저장할 수 있다).</summary>
    public static void Apply(World w, PlayerCommand cmd)
    {
        bool was = w.ApplyingRecord;
        w.ApplyingRecord = true;
        try { ApplyInner(w, cmd); }
        finally { w.ApplyingRecord = was; }
    }

    private static void ApplyInner(World w, PlayerCommand cmd)
    {
        var a = cmd.Arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (cmd.Kind)
        {
            case "meteor":
                if (w.Ship.Grid.InBounds(new Cell(int.Parse(a[0], Inv), int.Parse(a[1], Inv)))) Meteor(w, new Cell(int.Parse(a[0], Inv), int.Parse(a[1], Inv)), float.Parse(a[2], Inv)); // v16.10 증축 전이면 그 칸은 아직 없다
                break;
            case "fire":
                if (w.Ship.Grid.InBounds(new Cell(int.Parse(a[0], Inv), int.Parse(a[1], Inv)))) Fire(w, new Cell(int.Parse(a[0], Inv), int.Parse(a[1], Inv))); // v16.10 증축 전이면 그 칸은 아직 없다
                break;
            case "pipe":
                if (w.Ship.Grid.InBounds(new Cell(int.Parse(a[0], Inv), int.Parse(a[1], Inv)))) PipeBurst(w, new Cell(int.Parse(a[0], Inv), int.Parse(a[1], Inv)), float.Parse(a[2], Inv)); // v16.10 증축 전이면 그 칸은 아직 없다
                break;
            case "break":
            {
                int id = int.Parse(a[0], Inv);
                if (id < w.Ship.Furniture.Count) Break(w, w.Ship.Furniture[id]);
                break;
            }
            case "breaktype":
                BreakAll(w, a[0], a.Length > 1 && a[1] == "1", a.Length > 2 ? Enum.Parse<FaultKind>(a[2]) : null);
                break;
            case "hazard":
                Hazard(w, Enum.Parse<HazardKind>(a[0]), new Cell(int.Parse(a[1], Inv), int.Parse(a[2], Inv)), a.Length > 3 ? int.Parse(a[3], Inv) : -1);
                break;
            case "scenario":
                Scenario(w, cmd.Arg, out _);
                break;
            case "scarcity":
                Scarcity(w);
                break;
            case "death":
                AllowDeath(w, cmd.Arg == "1");
                break;
            case "tune":
                Tune(w, a[0], float.Parse(a[1], Inv));
                break;
            case "policy":
                Policy(w, a[0], int.Parse(a[1], Inv));
                break;
            default:
                throw new FormatException($"모르는 기록: {cmd}");
        }
    }
}

/// <summary>
/// 저장 파일: 시드 + 관찰자가 한 일 + 저장한 틱 + 그때 상태의 지문(해시).
/// 우주선의 모든 것(승무원의 기억, 벽의 이력, 작업 중인 일…)을 직렬화하는 대신 결정론으로 다시 만든다.
/// 불러온 뒤 지문이 같으면 한 틱도 어긋나지 않고 같은 역사를 되짚었다는 뜻이다.
/// </summary>
public static class SaveGame
{
    /// <summary>v8: 설계도(드론 거치대)와 구조가 바뀌어 v7 저장(1)은 같은 역사를 되짚을 수 없다.</summary>
    public const string Header = "shipsim-save 17"; // v16.22 기본 다섯 척 새 설계

    public static string Write(World w)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        sb.AppendLine($"seed {w.Seed}");
        if (w.ShipKey != ShipCatalog.Default.Key) sb.AppendLine($"ship {w.ShipKey}");
        if (w.StartCrew != (ShipCatalog.Find(w.ShipKey) ?? ShipCatalog.Default).Crew) sb.AppendLine($"crew {w.StartCrew}");
        foreach (var (k, v) in w.StartTuning) sb.AppendLine($"tune {k} {v.ToString("R", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"tick {w.Tick}");
        sb.AppendLine($"hash {StateHash(w):x8}");
        sb.AppendLine($"day {w.Day} {w.Clock}");
        foreach (var c in w.Commands.Concat(w.Scheduled)) sb.AppendLine($"cmd {c}"); // v10.3: 되감은 역사의 앞으로 올 사고도
        return sb.ToString();
    }

    public sealed record Data(int Seed, long Tick, uint Hash, List<PlayerCommand> Commands, int Crew = 0, string? Ship = null,
        List<(string key, float value)>? Tunes = null);

    /// <summary>
    /// v10 되감기: 그 틱으로 돌아간다. v10.3: 관찰자가 그 뒤에 한 일도 함께 적는다 — 불러오면 그 틱까지 빨리 감고,
    /// 뒤의 일은 그 틱이 오면 다시 일어난다 (같은 역사가 다시 흐른다). 지문은 모르니 0 — 불러온 뒤 확인하지 않는다.
    /// </summary>
    public static string WriteAt(World w, long tick)
    {
        tick = Math.Clamp(tick, 0, w.Tick);
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        sb.AppendLine($"seed {w.Seed}");
        if (w.ShipKey != ShipCatalog.Default.Key) sb.AppendLine($"ship {w.ShipKey}");
        if (w.StartCrew != (ShipCatalog.Find(w.ShipKey) ?? ShipCatalog.Default).Crew) sb.AppendLine($"crew {w.StartCrew}");
        foreach (var (k, v) in w.StartTuning) sb.AppendLine($"tune {k} {v.ToString("R", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"tick {tick}");
        sb.AppendLine("hash 00000000");
        sb.AppendLine($"day {SimTime.Day(tick)} {SimTime.Clock(tick)}");
        foreach (var c in w.Commands.Concat(w.Scheduled)) sb.AppendLine($"cmd {c}");
        return sb.ToString();
    }

    public static Data Parse(string text)
    {
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0 && lines[0].Trim() == "shipsim-save 1") throw new FormatException("아주 오래된 항해의 기록이다 — 그 뒤로 배의 설계와 구조가 바뀌어 같은 항해를 다시 돌릴 수 없다");
        if (lines.Length > 0 && lines[0].Trim() is "shipsim-save 2" or "shipsim-save 3" or "shipsim-save 4" or "shipsim-save 5" or "shipsim-save 6" or "shipsim-save 7" or "shipsim-save 8" or "shipsim-save 9" or "shipsim-save 10" or "shipsim-save 11" or "shipsim-save 12" or "shipsim-save 13" or "shipsim-save 14" or "shipsim-save 15" or "shipsim-save 16")
            throw new FormatException("예전 항해의 기록이다 — 그 뒤로 배의 설계와 구조가 바뀌어 같은 항해를 다시 돌릴 수 없다");
        if (lines.Length == 0 || lines[0].Trim() != Header) throw new FormatException("저장 파일이 아니다");
        int seed = 0, crew = 0;
        string? ship = null;
        long tick = 0;
        uint hash = 0;
        var cmds = new List<PlayerCommand>();
        var tunes = new List<(string, float)>();
        foreach (var raw in lines.Skip(1))
        {
            var line = raw.Trim();
            int sp = line.IndexOf(' ');
            if (sp < 0) continue;
            string key = line[..sp], rest = line[(sp + 1)..];
            switch (key)
            {
                case "seed": seed = int.Parse(rest, CultureInfo.InvariantCulture); break;
                case "crew": crew = int.Parse(rest, CultureInfo.InvariantCulture); break;
                case "ship": ship = ShipCatalog.Find(rest)?.Key ?? throw new FormatException($"모르는 배: {rest}"); break;
                case "tick": tick = long.Parse(rest, CultureInfo.InvariantCulture); break;
                case "tune":
                {
                    var kv = rest.Split(' ', 2);
                    if (Tuning.Find(kv[0]) == null) throw new FormatException($"모르는 수치: {kv[0]}");
                    tunes.Add((kv[0], float.Parse(kv[1], CultureInfo.InvariantCulture)));
                    break;
                }
                case "hash": hash = uint.Parse(rest, NumberStyles.HexNumber, CultureInfo.InvariantCulture); break;
                case "cmd":
                {
                    var parts = rest.Split(' ', 3);
                    cmds.Add(new PlayerCommand(long.Parse(parts[0], CultureInfo.InvariantCulture), parts[1], parts.Length > 2 ? parts[2] : ""));
                    break;
                }
            }
        }
        return new Data(seed, tick, hash, cmds, crew, ship, tunes);
    }

    /// <summary>지금 상태의 지문: 승무원·설비·벽·물자·공기·전력·역사를 훑은 FNV 해시.</summary>
    public static uint StateHash(World w)
    {
        uint h = 2166136261;
        void I(long v)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    h ^= (byte)(v >> (i * 8));
                    h *= 16777619;
                }
            }
        }
        void F(float v) => I(BitConverter.SingleToInt32Bits(v));
        I(w.Tick);
        // v13.4 방침과 사기도 지문에 (회의가 바꾼 방침이 저장·불러오기에서 갈라지면 바로 보이게)
        foreach (var p in PolicySystem.All) I(w.Policies[p.Id]);
        F(w.Society.Morale);
        foreach (var c in w.Crew)
        {
            F(c.Position.X); F(c.Position.Y);
            F(c.Needs.Food); F(c.Needs.Rest); F(c.Needs.Stress); F(c.Needs.Social);
            F(c.Vitals.Health); F(c.Vitals.Injury); F(c.Traits.Calm); F(c.Memory.Trauma);
            I(c.Dead ? 2 : c.Down ? 1 : 0);
            foreach (var f in c.Memory.Fear) F(f);
            foreach (var a in c.Ailments) { I(a.Id.Length); I(a.Since); F(a.Healed); } // v14.1
        }
        foreach (var b in w.Belongings.All) { I(b.Owner); I(b.Holder); I(b.At is Cell bc ? bc.X * 1000 + bc.Y : -1); F(b.Condition); F(b.Progress); } // v14.3
        foreach (var st in w.Movement.Stashes) { I(st.Owner); I(st.Cell.X * 1000 + st.Cell.Y); I((int)st.Stack.Kind); I(st.Stack.Count); } // v14.5 내려놓고 간 짐
        I(w.Movement.Stats.Yields); I(w.Movement.Stats.Reroutes); I(w.Movement.Stats.Startles);
        I(w.Daily.Stats.Fired); I(w.Daily.Stats.Seen.Count); // v15
        I(w.Culture.Customs.Count); I(w.Culture.Stats.Explained); I(w.Culture.Stats.Imitated); I(w.Culture.Stats.ExtChecks); // v14.9
        I(w.Flow.Stats.Brownouts); I(w.Flow.Stats.Backflows); I(w.Flow.Stats.Equalized); I(w.Flow.Stats.SpliceHot); I((int)(w.Flow.WaterQuality * 1000)); // v14.8
        I(w.Soil.Stats.HandWashes); I(w.Soil.Stats.TaintedMeals); I(w.Soil.LaundryLoad); I(w.Soil.Stats.Decons); // v14.7
        I(w.Parts.Stats.Replaced); I(w.Parts.Stats.Recurrences); I(w.Parts.Stats.BatchAlerts); I(w.Parts.Stats.Tested); // v14.6
        foreach (var m in w.Ship.Machines)
        {
            F(m.Wear); F(m.Condition); I(m.Faults.Count); I((int)m.Grade); I(m.FaultCount);
        }
        foreach (var (cell, wall) in w.Ship.Walls)
        {
            if (!wall.IsHull) continue;
            F(wall.Integrity); F(wall.MaxIntegrity); I(wall.Patched ? 1 : 0);
        }
        foreach (var k in ItemKinds.All) I(w.Ship.CountStored(k));
        foreach (var r in w.Ship.Rooms) { F(r.Air.O2); F(r.Air.Pressure); I(r.Abandoned ? 1 : 0); }
        F(w.Air.Reserve); F(w.Water.Level); F(w.Power.BatteryCharge);
        I(w.History.Events.Count); I(w.Ship.Furniture.Count);
        // v8 구조·드론·EVA
        foreach (var j in w.Structure.Joints) { F(j.Strength); F(j.Known); I(j.Released ? 1 : 0); }
        foreach (var f in w.Structure.Fragments) { F(f.Offset.X); F(f.Offset.Y); I((int)f.State); }
        foreach (var d in w.Drones.Drones) { F(d.Position.X); F(d.Position.Y); F(d.Battery); F(d.Condition); I((int)d.State); }
        foreach (var c in w.Crew) I(c.Outside ? 1 : 0);
        I(w.AirlockCycles);
        // v9 배관·냉각
        foreach (var s in w.Piping.Segments) { F(s.Integrity); F(s.RadiatorCondition); F(s.Bypass); I(s.Closed ? 1 : 0); I(s.Patched ? 1 : 0); }
        F(w.Piping.Coolant); F(w.Power.ReactorTemperature); I(w.SuitRefills);
        I(w.Automation.MainOnline ? 1 : 0); I(w.Automation.Backup ? 1 : 0); I(w.Automation.Outages); I(w.Power.Brownout ? 1 : 0); I(w.Power.Brownouts); I(w.Fixtures.DoorFailures); I(w.Fixtures.LightFailures); I(w.Ship.Doors.Count(d => d.MotorBroken)); I(w.Ship.Rooms.Count(r => r.LightsOut));
        foreach (var r in w.Ship.Rooms) I(r.DamperStuck ? 1 : 0);
        // v10.1 통신실
        I(w.Sensors.Incoming.Count); I(w.Sensors.Warned); I(w.Sensors.Unwarned); I(w.Sensors.PreSeals);
        I(w.Ship.Rooms.Count); I(w.History.Partitions); // v10.2
        // v10.3: 뒤의 결과를 바꾸는 상태를 더 — 혈중 산소·우주복·들고 있는 것·하는 일, 방의 온도·기체·연기·환기·조명,
        // 불(칸마다), 작물, 설비 전기, 원자로, 다가오는 운석, 그리고 난수를 몇 번 뽑았나
        foreach (var c in w.Crew)
        {
            F(c.Vitals.Oxygen); F(c.Suit?.Oxygen ?? -1f); I(c.Carrying is ItemStack st ? (int)st.Kind * 1000 + st.Count : -1);
            I(c.Cell.X); I(c.Cell.Y); I(c.Job?.Label?.Length ?? -1); I(c.Bed?.Id ?? -1);
        }
        foreach (var r in w.Ship.Rooms)
        {
            F(r.Air.Temperature); F(r.Air.CO2); F(r.Air.N2); F(r.Air.Smoke);
            I(r.VentOpen ? 1 : 0); I(r.LightsOut ? 1 : 0); I(r.Lockdown ? 1 : 0); I(r.Cells.Count);
        }
        foreach (var (cell, v) in w.Fire.Fires.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X)) { I(cell.X); I(cell.Y); F(v); }
        foreach (var m in w.Ship.Machines)
        {
            I(m.Powered ? 1 : 0); I(m.Parked ? 1 : 0); I(m.Tier);
            if (m.Crop is CropState crop) { F(crop.Growth); F(crop.Care); F(crop.DryHours); }
        }
        foreach (var d in w.Ship.Doors) { F(d.Openness); I(d.Locked ? 1 : 0); }
        F(w.Power.ReactorLimit); I(w.Power.ReactorOnline ? 1 : 0); I(w.Power.LowPowerMode ? 1 : 0);
        foreach (var m in w.Sensors.Incoming) { I(m.Arrive); I((int)m.Warned); }
        I(w.Rng.Draws); I(w.Ship.Furniture.Count); F(w.Research);
        // v10.10 선내 로봇
        foreach (var r in w.Robots.Robots) { F(r.Position.X); F(r.Position.Y); F(r.Battery); F(r.Condition); I((int)r.State); I(r.Fault is RobotFault rf ? (int)rf : -1); I(r.JobsDone); }
        // v11.0 전조
        foreach (var m in w.Ship.Machines) if (m.Omen is Omen om) { I((int)om.Kind); I(om.Due); I(om.Known ? 1 : 0); I((int)om.Cause); }
        // v12.0 당직 일지·감지기·친숙함
        foreach (var n in w.Watch.Notes) { I(n.Id); I((int)n.Stage); I(n.Suspect is OmenCause sc ? (int)sc : -1); I(n.Confirmed is OmenCause cf ? (int)cf : -1); I(n.Logged ? 1 : 0); I(n.Holders.Count); I(n.WrongFixes); }
        foreach (var m in w.Ship.Machines) { F(m.SensorCal); I(m.LastReading); }
        foreach (var c in w.Crew) foreach (var (t, v) in c.Familiarity.OrderBy(kv => kv.Key)) { I((int)t); F(v); }
        // v12.2 열·기체·분말·잔해·역화·일산화탄소
        foreach (var m in w.Ship.Machines) { F(m.Heat); F(m.Vapor); F(m.Fouled); I(m.CoolingDown ? 1 : 0); }
        foreach (var (cell, v) in w.Ship.Rubble.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X)) { I(cell.X); I(cell.Y); F(v); }
        foreach (var r in w.Ship.Rooms) { F(r.Air.CO); F(r.Backdraft); F(r.O2Leak); }
        F(w.Power.ReactorPoison); I(w.Volatile.Stats.Explosions);
        foreach (var c in w.Crew) I(c.Aboard?.Id ?? -1);
        // v12.1 설비 전선·관·재조립 불량
        foreach (var m in w.Ship.Machines) { F(m.Feed); F(m.Line); I(m.Spliced ? 1 : 0); I(m.Defect is FaultKind dk ? (int)dk : -1); }
        I(w.UsedParts.Count); I(w.Power.MaintenanceCap ? 1 : 0);
        foreach (var l in w.Net.Links) { F(l.Integrity); I(l.Temp ? 1 : 0); }
        I(w.Precursors.Prevented); I(w.Precursors.Missed);
        // v11.2 추진
        F(w.Propulsion.Propellant); I((int)w.Propulsion.Zone); I(w.Propulsion.Dodged); I(w.Propulsion.Evasions); F(w.Space.Density);
        // v11.2 사고 종류·무작위 사고
        var hz = w.Hazards;
        I(hz.NextRandom); I(hz.RandomRng.Draws); I(hz.RandomCount); I(hz.StormUntil); I(hz.Shower.Count); I(hz.Poisoned);
        foreach (var r in w.Ship.Rooms) F(r.Air.Toxin);
        foreach (var r in w.Ship.Rooms) { F(r.Flood); F(r.Humidity); I(r.BreakerOff ? 1 : 0); I(r.ValveShut ? 1 : 0); } // v12.3
        foreach (var c in w.Crew) { I((int)(c.InfectedAt % 1000003)); F(c.CureDays); I(c.Immune ? 1 : 0); } // v12.4
        I(w.Story.Fired);
        foreach (var m in w.Ship.Machines) if (m.Crop is CropState cr) { F(cr.Blight); I(cr.BlightKnown ? 1 : 0); }
        foreach (var f in w.Ship.Containers) { I(f.Storage!.Tainted); I(f.Storage.TaintKnown ? 1 : 0); }
        foreach (var c in w.Crew) { I(c.CarryTaint); I(c.PoisonAt); }
        // v10.11 배급
        I(w.Food.Rationing ? 1 : 0); I(w.Food.Rationings);
        // v11.2 교신
        I(w.Growth.Lessons); I(w.Growth.RehabSessions); foreach (var c in w.Crew) { F(c.Vitals.Scar); foreach (var sk in c.SkillLevels) F(sk); } // v11.3
        I(w.Comms.DistressAt); I(w.Comms.SupplyEta); I(w.Comms.SupplyDocked ? 1 : 0); I(w.Comms.SignalAt); I(w.Comms.PodEta); I(w.Comms.Rescued); I(w.Crew.Count);
        // v16.0 칭호 · 주 컴퓨터 모듈 (열거 순서대로 — HashSet 순회 순서에 기대지 않는다)
        I(w.Titles.Awards.Count); foreach (var t in w.Titles.Awards) { I(t.CrewId); I((int)(t.Tick % 1000003)); }
        foreach (var m in Enum.GetValues<ComputerModule>()) I(w.Automation.Has(m) ? 1 : 0);
        { var bs = w.Body.Stats; I(bs.Falls); I(bs.HatchOpens); I(bs.Calls); I(bs.Knocks); I(bs.Overheard); I(w.Body.Hatches.Count); I(w.Body.Marks.Count); foreach (var db in w.Body.Doors) { I(db.Pass); F(db.Gasket); } } // v16.3 배 본체
        I(w.Scenes.Hash()); // v16.1 일상 장면 · 쪽지 · 인수인계
        // v16.0 ④ · v16.6 주컴퓨터: 다섯 칸 기록 · 제안 · 확인 · 사람마다 신뢰 · 믿음 · 연산 자원 · 방송
        { var au = w.Automation; I(au.Book.Total); I(au.Book.Right); I(au.Book.Wrong); I(au.Asks.All.Count); I(au.Asks.Rejected); I(au.CheckFound); I(au.Reboots); I(au.Suspended.Count); I(au.Speak.Count); I(au.Belief.Repairs); foreach (var c in w.Crew) F(au.Trusts.Of(c)); foreach (var m in au.Modules.OrderBy(m => (int)m)) I((int)m); }
        w.Cooking.Hash(I, F); w.Smells.Hash(I, F); // v16.8 음식 · 냄새
        foreach (var d in w.Portable.Devices) { I(d.At.X * 1000 + d.At.Y); I((d.On ? 1 : 0) + (d.Stored ? 2 : 0) + (d.Broken ? 4 : 0) + (int)d.Plug * 8); F(d.Charge); } // v16.7
        { var bs = w.Body.Stats; I(bs.ComputerFlags); I(bs.ComputerWarnings); I(bs.Witnessed); I(bs.Mopped); I(bs.FoodSpills); I(bs.Grease); I(bs.Mildew); } // v16.3 배 본체 ↔ 주 컴퓨터 · 다른 시스템
        { var au = w.Automation; I(au.Passes); I(au.Denials); I(au.RationLeads); I(au.DoorBlinds); I(au.Emps); I(au.Forecasts); I(au.Foresight.Made); I(au.Foresight.Hits); I(au.Foresight.Plans); } // v16.6 문 · 식단 · 대재난 · 앞날 예측
        { var ps = w.Portable.Stats; foreach (var d in w.Portable.Devices) { F(d.Soak); F(d.Dust); } I(ps.ComputerWarns); I(ps.HeededWarns); I(ps.HeaterWarns); I(ps.HeaterOffs); I(ps.InventoryFound); I(ps.DustSniffs); I(ps.HotOutlets); } // v16.7 컴퓨터 · 냄새 · 배 본체
        { var os = w.Origin.Stats; I(os.Found); I(os.Told); I(os.Tips); I(os.Toasts); I(os.Reopened); I(os.Splits); I(os.Handovers); I(os.Rounds); I(os.Squeezes); I(os.Ranks); I(os.Calls); } // v16.9 배의 내력
        w.Cosmic.Hash(I, F); // v18.13 우주 대재난
        w.EvaRisk.Hash(I, F); foreach (var d in w.Drones.Drones) { F(d.Hurt.Thruster); F(d.Hurt.Battery); F(d.Hurt.Swell); } // v16.11 선외 위험 · 드론 부위
        w.Expedition.Hash(I, F); foreach (var c in w.Crew) I(c.Away ? 1 : 0); // v16.12 원정
        w.Blast.Hash(I, F); // v16.13 폭발 · 폭발성 물건
        w.RoomUse.Hash(I, F); w.RoomPlans.Hash(I, F); // v16.17 쓰임 · 방 이름 · 공사
        I(w.Brain2.Hash()); // v16.15 승무원 두뇌 2.0
        w.Matter.Hash(I, F); // v16.4 재질 × 원소 · 물건 물리
        w.TechWeb.Hash(I, F); // v16.14 기술 그물 · 실험
        w.Automation.HashBrain(I, F); // v16.16 주컴퓨터 두뇌 2.0
        w.Scale.Hash(I, F); // v16.18 사고 규모
        w.Annex.Hash(I, F); // v16.10 증축 (공정 · 격자 높이 · 더한 무게)
        w.Body2.Hash(I, F); // v17.1 머리카락 · 체중 · 우주복 치수 · 이발
        w.Coop.Hash(I, F); // v17.4 공간과 협력 · 줄 · 구경꾼
        w.Automation.HashShip(I, F); // v16.20 우주선급 주컴퓨터
        w.Failsafe.Hash(I); w.Major.Hash(I); // v16.19 차압 문 · 예비 회로 · 큰 사고
        w.CrisisCrew.Hash(I, F); // v16.21 승무원 위기 행동
        w.Fleet.Hash(I, F); // v16.20b 함대 지휘
        w.FoodSources.Hash(I, F); w.Scrap.Hash(I, F); // v16.22 식량원 · 고철
        w.Casualty.Hash(I, F); // v16.24 큰 상처 뒤
        w.After.Hash(I, F); // v17.5 사고 뒤 며칠 · 꿈 · 장소의 기억
        w.Info.Hash(I, F); // v17.3 자리 · 못 끝낸 일 · 정보 차이 · 메신저 · 사진 · 장부
        return h;
    }
}

/// <summary>
/// 불러오기: 같은 시드로 새 우주선을 띄우고, 기록된 틱마다 기록된 일을 하면서 저장한 틱까지 돌린다.
/// 게임 화면에서는 한 프레임에 조금씩 (역사가 빨리 감기로 다시 흐르는 걸 볼 수 있다).
/// </summary>
public sealed class ReplayRunner
{
    private readonly List<PlayerCommand> _commands;
    private int _next;

    public World World { get; }
    public long TargetTick { get; }
    public uint ExpectedHash { get; }
    private readonly long _startTick;

    public ReplayRunner(string saveText)
    {
        var data = SaveGame.Parse(saveText);
        // v10.7: 그 항해를 시작할 때의 수치로 되돌린 뒤 만든다 (항해 중에 바꾼 값은 기록(tune)으로 그 틱에 다시 바뀐다)
        Tuning.ResetDefaults();
        foreach (var (k, v) in data.Tunes ?? new()) Tuning.Apply(k, v);
        World = World.CreateDefault(data.Seed, data.Crew, data.Ship ?? ShipCatalog.Default.Key);
        // v10.3: 저장·되감은 틱 뒤의 기록은 앞으로 다시 일어날 일로 넘긴다 (불러오기가 100%에서 멈추지 않게)
        var all = data.Commands.OrderBy(c => c.Tick).ToList();
        foreach (var c in all) Validate(World, c);
        _commands = all.Where(c => c.Tick <= data.Tick).ToList();
        World.Scheduled.AddRange(all.Where(c => c.Tick > data.Tick));
        TargetTick = data.Tick;
        ExpectedHash = data.Hash;
        _startTick = World.Tick;
        ApplyDue();
    }

    public bool Done => World.Tick >= TargetTick && _next >= _commands.Count;

    /// <summary>되감은 뒤 앞으로 다시 일어날 관찰자의 사고 수.</summary>
    public int Upcoming => World.Scheduled.Count;
    public float Progress => TargetTick <= _startTick ? 1f : Math.Clamp((World.Tick - _startTick) / (float)(TargetTick - _startTick), 0f, 1f);

    /// <summary>지문이 저장할 때와 같은지 (불러오기가 끝난 뒤).</summary>
    public bool Verified => SaveGame.StateHash(World) == ExpectedHash;

    /// <summary>v10 되감기로 만든 재생 (지문을 모른다).</summary>
    public bool Rewind => ExpectedHash == 0;

    /// <summary>v10.3: 기록 한 줄이 이 배에서 다시 할 수 있는 일인지 미리 본다 (망가진 파일이 불러오는 도중에 터지지 않게).</summary>
    private static void Validate(World w, PlayerCommand c)
    {
        var a = c.Arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var inv = CultureInfo.InvariantCulture;
        bool Cell2() => a.Length >= 2 && int.TryParse(a[0], NumberStyles.Integer, inv, out var x) && int.TryParse(a[1], NumberStyles.Integer, inv, out var y)
                        && x >= 0 && x < w.Ship.Grid.Width && y >= 0 && y < w.Ship.Grid.Height + 64; // v16.10 증축으로 아래쪽 줄이 늘 수 있다 (적용할 때 다시 본다)
        bool ok = c.Kind switch
        {
            "meteor" or "pipe" => Cell2() && a.Length >= 3 && float.TryParse(a[2], NumberStyles.Float, inv, out _),
            "fire" => Cell2(),
            // v10.6: 모듈을 달면 설비가 늘어난다 — 나중에 생긴 설비의 고장도 기록될 수 있다
            "break" => a.Length >= 1 && int.TryParse(a[0], NumberStyles.Integer, inv, out var id) && id >= 0 && id < w.Ship.Furniture.Count + 500,
            "tune" => a.Length >= 2 && Tuning.Find(a[0]) != null && float.TryParse(a[1], NumberStyles.Float, inv, out _),
            "policy" => a.Length >= 2 && (a[0] == "*" || PolicySystem.All.Any(p => p.Id == a[0])) && int.TryParse(a[1], NumberStyles.Integer, inv, out _),
            "breaktype" => a.Length >= 2 && (a.Length < 3 || Enum.TryParse<FaultKind>(a[2], out _)),
            "scenario" => Scenarios.All.Any(s => s.Name == c.Arg),
            "scarcity" or "death" => true,
            "hazard" => a.Length >= 3 && Enum.TryParse<HazardKind>(a[0], out _) && int.TryParse(a[1], NumberStyles.Integer, inv, out _)
                        && int.TryParse(a[2], NumberStyles.Integer, inv, out _) && (a.Length < 4 || int.TryParse(a[3], NumberStyles.Integer, inv, out _)),
            _ => false,
        };
        if (!ok) throw new FormatException($"다시 할 수 없는 기록이다: {c}");
    }

    private void ApplyDue()
    {
        while (_next < _commands.Count && _commands[_next].Tick <= World.Tick)
            Player.Apply(World, _commands[_next++]);
    }

    /// <summary>최대 maxSteps틱만큼 앞으로. 다 되면 true.</summary>
    public bool Advance(int maxSteps)
    {
        for (int i = 0; i < maxSteps && World.Tick < TargetTick; i++)
        {
            World.Step();
            ApplyDue();
        }
        if (World.Tick >= TargetTick) ApplyDue();
        return Done;
    }
}
