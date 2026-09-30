using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// 한 사람 안에 남은 것 (v7 승무원의 역사).
/// - 공포: 감압·화재·쓰러짐을 겪은 방은 돌아서 가고, 그 방의 일은 남에게 미룬다. 아무 일 없이 지내면 옅어진다.
/// - 긴장(스트레스 기저치): 중상, 쓰러짐, 동료의 죽음, 이어진 과로가 남긴다. 스트레스가 이 아래로 내려가지 않는다.
/// - 함께 넘긴 사고: 같은 사고에 함께 대응한 사람끼리 가까워지고, 여러 번이면 전우가 된다.
/// </summary>
public sealed class CrewMemory
{
    /// <summary>방 Id → 공포 0~1.</summary>
    public float[] Fear { get; private set; }

    /// <summary>방 Id → 무서워진 까닭.</summary>
    public string?[] FearCause { get; private set; }

    internal bool[] FearNoted { get; private set; }

    /// <summary>긴장 0~0.35: 스트레스가 이 아래로 내려가지 않고, 스트레스가 쌓이는 속도도 빨라진다.</summary>
    public float Trauma { get; set; }
    public string? TraumaCause { get; set; }
    internal bool TraumaNoted { get; set; }

    /// <summary>승무원 Id → 함께 넘긴 사고 수.</summary>
    public Dictionary<int, int> Shared { get; } = new();

    /// <summary>전우 (함께 사고를 여러 번 넘기고 서로 믿게 된 사람).</summary>
    public HashSet<int> Comrades { get; } = new();

    public List<Mark> Marks { get; } = new();

    public int OverworkDays { get; internal set; }
    internal long WorkTicksToday { get; set; }
    internal int Day { get; set; } = -1;
    internal bool SevereNoted { get; set; }

    public bool AnyFear { get; internal set; }

    /// <summary>방 Id → 마지막으로 그 방에서 크게 놀란 틱 (한 사고에 한 번만 크게 놀란다).</summary>
    internal long[] Scared { get; private set; }

    public CrewMemory(int rooms)
    {
        Fear = new float[rooms];
        FearCause = new string?[rooms];
        FearNoted = new bool[rooms];
        Scared = new long[rooms];
        Array.Fill(Scared, -1_000_000L);
    }

    public float FearOf(Room? r) => r != null && r.Id < Fear.Length ? Fear[r.Id] : 0f;

    /// <summary>v10.2: 방이 늘었다 (칸막이로 나눔).</summary>
    public void GrowRooms(int rooms)
    {
        if (rooms <= Fear.Length) return;
        int old = Fear.Length;
        var fear = Fear; var cause = FearCause; var noted = FearNoted; var scared = Scared;
        System.Array.Resize(ref fear, rooms); System.Array.Resize(ref cause, rooms); System.Array.Resize(ref noted, rooms); System.Array.Resize(ref scared, rooms);
        for (int i = old; i < rooms; i++) scared[i] = -1_000_000L;
        Fear = fear; FearCause = cause; FearNoted = noted; Scared = scared;
    }

    /// <summary>가장 무서운 방 (없으면 null).</summary>
    public (int room, float fear)? WorstFear()
    {
        int best = -1;
        for (int i = 0; i < Fear.Length; i++)
            if (Fear[i] > 0.1f && (best < 0 || Fear[i] > Fear[best])) best = i;
        return best < 0 ? null : (best, Fear[best]);
    }
}

public static class Memory
{
    public const float TraumaMax = 0.35f;

    /// <summary>얼마나 쉽게 겁을 먹는지: 용감하고 침착할수록 덜.</summary>
    private static float Susceptibility(CrewMember c) => (1f - 0.5f * c.Traits.Bravery) * (1f - 0.5f * c.Traits.Calm);

    /// <summary>그 방이 무서워진다.</summary>
    public static void Frighten(World w, CrewMember c, Room? room, float amount, string cause)
    {
        if (room == null || c.Dead || room.Type == RoomType.Corridor) return;
        var m = c.Memory;
        if (room.Id >= m.Fear.Length) return;
        float add = amount * Susceptibility(c);
        m.Fear[room.Id] = MathF.Min(1f, m.Fear[room.Id] + add);
        if (add > 0.02f || m.FearCause[room.Id] == null) m.FearCause[room.Id] = cause;
        if (m.Fear[room.Id] > 0.05f) m.AnyFear = true;
        if (!m.FearNoted[room.Id] && m.Fear[room.Id] >= 0.35f)
        {
            m.FearNoted[room.Id] = true;
            string text = $"{Ko.EunNeun(c.Name)} 이제 {Ko.IGa(room.Name)} 무섭다 — {m.FearCause[room.Id]}";
            MarkLog.Add(m.Marks, w.Tick, $"{Ko.IGa(room.Name)} 무서워졌다 ({m.FearCause[room.Id]})");
            w.History.Add(w, HistoryKind.Memory, text, room, new[] { c }, log: true, crewLog: c.Id);
        }
    }

    /// <summary>긴장이 남는다 (스트레스 기저치). 큰일을 겪으면 침착함도 조금 흔들린다.</summary>
    public static void Shake(World w, CrewMember c, float amount, string cause)
    {
        if (c.Dead) return;
        var m = c.Memory;
        float add = amount * (1f - 0.4f * c.Traits.Calm);
        m.Trauma = MathF.Min(TraumaMax, m.Trauma + add);
        if (add >= 0.03f || m.TraumaCause == null) m.TraumaCause = cause;
        c.Traits.Calm = MathF.Max(0.1f, c.Traits.Calm - 0.3f * add);
        if (!m.TraumaNoted && m.Trauma >= 0.15f)
        {
            m.TraumaNoted = true;
            MarkLog.Add(m.Marks, w.Tick, $"{m.TraumaCause} 뒤로 긴장이 풀리지 않는다");
            w.History.Add(w, HistoryKind.Memory, $"{Ko.EunNeun(c.Name)} {m.TraumaCause} 뒤로 늘 긴장해 있다", crew: new[] { c }, log: true, crewLog: c.Id);
        }
    }

    /// <summary>사고를 넘길 때마다 조금씩 침착해진다.</summary>
    public static void Steady(World w, CrewMember c, float amount)
    {
        float before = c.Traits.Calm;
        c.Traits.Calm = MathF.Min(0.9f, before + amount * (1f - before));
        if (before < 0.7f && c.Traits.Calm >= 0.7f)
        {
            MarkLog.Add(c.Memory.Marks, w.Tick, "사고를 여러 번 넘기며 침착해졌다");
            w.History.Add(w, HistoryKind.Memory, $"{Ko.EunNeun(c.Name)} 사고를 여러 번 넘기며 침착해졌다", crew: new[] { c }, log: true, crewLog: c.Id);
        }
    }

    public static bool AreComrades(CrewMember a, CrewMember b) => a.Memory.Comrades.Contains(b.Id);

    /// <summary>같은 사고에 함께 대응했다: 가까워지고, 여러 번이면 전우가 된다.</summary>
    public static void ShareCrisis(World w, CrewMember a, CrewMember b, Episode ep)
    {
        a.ChangeAffinity(b, 0.08f);
        b.ChangeAffinity(a, 0.08f);
        int n = a.Memory.Shared.GetValueOrDefault(b.Id) + 1;
        a.Memory.Shared[b.Id] = n;
        b.Memory.Shared[a.Id] = n;
        if (a.Memory.Comrades.Contains(b.Id) || n < 2 || a.AffinityTo(b) < 0.35f || b.AffinityTo(a) < 0.35f) return;
        a.Memory.Comrades.Add(b.Id);
        b.Memory.Comrades.Add(a.Id);
        MarkLog.Add(a.Memory.Marks, w.Tick, $"{Ko.WaGwa(b.Name)} 전우가 됐다");
        MarkLog.Add(b.Memory.Marks, w.Tick, $"{Ko.WaGwa(a.Name)} 전우가 됐다");
        w.History.Add(w, HistoryKind.Bond, $"{Ko.WaGwa(a.Name)} {Ko.EunNeun(b.Name)} 이제 전우다 — 사고를 {ShipHistory.Times(n)} 함께 넘겼다",
            crew: new[] { a, b }, log: true);
    }

    /// <summary>시스템 주기(15틱)마다: 공포가 생기고 옅어지고, 긴장이 풀리고, 과로를 센다.</summary>
    public static void Update(World w, CrewMember c)
    {
        if (c.Dead) return;
        const float dt = World.SystemInterval / (float)SimTime.TicksPerHour;
        var m = c.Memory;
        var room = c.Room;

        // 공포가 생기는 순간: 공기가 빠지는 방에 있었다, 불길 곁에 있었다 (한 번 크게 놀라고, 머무는 만큼 더)
        // 우주복을 입고 일하러 들어간 사람, 소화기를 든 사람은 덜 놀란다 — 각오하고 들어갔다
        if (room != null && room.Id < m.Scared.Length && c.CanAct)
        {
            bool suited = c.Suit is { Oxygen: > 0f };
            bool armed = c.Carrying?.Kind == ItemKind.Extinguisher;
            bool fresh = w.Tick - m.Scared[room.Id] > SimTime.Hours(3);
            if (room.Leaking && room.Air.Pressure < 85f && !room.Abandoned)
            {
                if (fresh && !suited) { m.Scared[room.Id] = w.Tick; Frighten(w, c, room, 0.3f, "감압을 겪었다"); }
                if (room.Air.Pressure < 55f) Frighten(w, c, room, (suited ? 0.2f : 0.9f) * dt, "감압을 겪었다");
            }
            if (w.Fire.AnyWithin(c.Cell, 2f))
            {
                if (fresh && !armed && !suited) { m.Scared[room.Id] = w.Tick; Frighten(w, c, room, 0.2f, "불길에 갇힐 뻔했다"); }
                Frighten(w, c, room, (armed || suited ? 0.2f : 0.7f) * dt, "불길 속에 있었다");
            }
        }

        // 공포는 천천히 옅어진다. 그 방에서 아무 일 없이 지내면 더 빨리 (익숙해진다)
        bool any = false;
        for (int i = 0; i < m.Fear.Length; i++)
        {
            if (m.Fear[i] <= 0f) continue;
            float perDay = 0.012f; // 한 달쯤 간다
            if (room != null && room.Id == i && c.IsAwake && Atmosphere.Danger(room) < 0.1f && !room.Leaking && w.Fire.CountIn(room) == 0)
                perDay += 0.1f;
            m.Fear[i] = MathF.Max(0f, m.Fear[i] - perDay / 24f * dt);
            if (m.Fear[i] < 0.1f) m.FearNoted[i] = false;
            if (m.Fear[i] > 0.05f) any = true;
        }
        m.AnyFear = any;

        // 긴장은 아주 천천히 풀린다 (몇 주에 걸쳐. 편히 쉬면 두 배)
        float relax = c.Job?.Activity is RelaxActivity ? 3f : 1f;
        m.Trauma = MathF.Max(0f, m.Trauma - 0.002f / 24f * relax * dt);
        if (m.Trauma < 0.08f) m.TraumaNoted = false;

        // 중상은 오래 남는다
        if (c.Vitals.Injury >= 0.5f && !m.SevereNoted)
        {
            m.SevereNoted = true;
            MarkLog.Add(m.Marks, w.Tick, $"크게 다쳤다 ({c.Vitals.InjuryCause ?? "사고"})");
            Shake(w, c, 0.1f, $"{Ko.EuRo(c.Vitals.InjuryCause ?? "사고")} 크게 다친 일");
        }
        else if (c.Vitals.Injury < 0.2f) m.SevereNoted = false;

        // 과로: 하루 11시간 넘게 일한 날이 쌓이면 긴장이 남는다
        int day = SimTime.Day(w.Tick);
        if (m.Day != day)
        {
            if (m.Day >= 0 && m.WorkTicksToday > SimTime.Hours(11))
            {
                m.OverworkDays++;
                MarkLog.Add(m.Marks, w.Tick, $"과로 (하루 {m.WorkTicksToday / (float)SimTime.TicksPerHour:0}시간)");
                Shake(w, c, 0.012f, "이어진 과로");
            }
            m.Day = day;
            m.WorkTicksToday = 0;
        }
        if (c.Pose == Pose.Working) m.WorkTicksToday += World.SystemInterval;
    }

    /// <summary>화면용: 이 사람 안에 남은 것 한 줄씩.</summary>
    public static IEnumerable<string> Describe(CrewMember c, World w)
    {
        var m = c.Memory;
        for (int i = 0; i < m.Fear.Length; i++)
            if (m.Fear[i] >= 0.15f)
                yield return $"{Ko.IGa(w.Ship.Rooms[i].Name)} 무섭다 {m.Fear[i] * 100:0}% ({m.FearCause[i]})";
        if (m.Trauma >= 0.05f) yield return $"긴장 {m.Trauma * 100:0}% ({m.TraumaCause})";
        foreach (var id in m.Comrades) yield return $"전우: {w.Crew[id].Name}";
    }
}
