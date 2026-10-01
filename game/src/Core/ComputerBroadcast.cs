using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.6 선내 방송: 주 컴퓨터가 방마다 달린 스피커로 말한다 — 들은 사람만 안다 (방 단위).
// 스피커는 그 방 데이터선(또는 통신 중계) · 전기가 있어야 울리고, 불에 녹거나 누전되면 몇 시간 못 쓴다.
// 깨어 있는 사람은 다 듣고, 자는 사람은 경보 방송(2)만 듣는다 (곯아떨어진 사람은 못 듣는다).

public sealed class Broadcast
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public string Text { get; init; } = "";
    public int RoomId { get; init; } = -1;
    /// <summary>0 알림 · 1 안내 · 2 경보.</summary>
    public int Priority { get; init; }
    /// <summary>방송에 실린 지시 ("shelter" 대피소로 · "gather:방번호" 모여라 …) — 들은 사람만 따른다 (HeedBroadcastActivity).</summary>
    public string Order { get; init; } = "";
    public List<int> Rooms { get; } = new();
    public List<int> Silent { get; } = new();
    public List<int> HeardBy { get; } = new();
    public List<int> Missed { get; } = new();
}

public sealed class PublicAddress
{
    private readonly World _w;
    private int _next = 1;
    private readonly Dictionary<int, long> _broken = new();
    private long _check;
    public List<Broadcast> Recent { get; } = new();
    public int Count, HeardTotal, MissedTotal;

    public PublicAddress(World w) => _w = w;

    /// <summary>이 방 스피커가 울리나.</summary>
    public bool SpeakerWorks(Room r) =>
        !r.Detached && r.Powered && !r.BreakerOff && (r.DataLinked || ComputerV15.Relay(_w)) && !(_broken.TryGetValue(r.Id, out var t) && t > _w.Tick);

    public bool SpeakerBroken(Room r) => _broken.TryGetValue(r.Id, out var t) && t > _w.Tick;

    public void BreakSpeaker(Room r, string why, float hours = 6f)
    {
        _broken[r.Id] = _w.Tick + SimTime.Hours(hours);
        _w.Log.Add(_w.Tick, LogKind.Warning, $"{r.Name} 스피커 고장 — {why} (방송이 안 들린다)");
    }

    public bool Heard(CrewMember c, int id) => Recent.FirstOrDefault(b => b.Id == id)?.HeardBy.Contains(c.Id) == true;

    /// <summary>방송한다. 주 컴퓨터가 멎으면 못 한다 (null).</summary>
    public Broadcast? Announce(string text, Room? about, int priority, string order = "")
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline) return null;
        var b = new Broadcast { Id = _next++, Tick = w.Tick, Text = text, RoomId = about?.Id ?? -1, Priority = priority, Order = order };
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached) continue;
            if (SpeakerWorks(r)) b.Rooms.Add(r.Id); else b.Silent.Add(r.Id);
        }
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Outside || c.Room == null) continue;
            bool loud = b.Rooms.Contains(c.Room.Id);
            bool wakes = c.IsAwake || priority >= 2 && !c.DeepAsleep && !c.Down;
            if (loud && wakes)
            {
                b.HeardBy.Add(c.Id);
                if (priority >= 2) c.Interrupt(w);
                // 경보가 닿지 않는 방의 불도 방송으로는 안다 (경보가 닿는 방은 원래대로 경보로 안다)
                if (about != null && w.Fire.IsKnown(about) && !w.Minds.AlarmReaches(about) && !c.Mind.Knows.ContainsKey($"fire:{about.Id}"))
                {
                    c.Mind.Knows[$"fire:{about.Id}"] = (KnowSource.Alarm, w.Tick, $"{about.Name} 불 (방송)");
                    c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1);
                }
            }
            else if (!c.Down) b.Missed.Add(c.Id);
        }
        Recent.Add(b);
        if (Recent.Count > 40) Recent.RemoveAt(0);
        Count++;
        HeardTotal += b.HeardBy.Count;
        MissedTotal += b.Missed.Count;
        w.Log.Add(w.Tick, LogKind.Ship, $"[방송] {text}" + (b.Silent.Count > 0 ? $" (스피커가 안 울린 방 {b.Silent.Count})" : ""));
        if (priority >= 1) a.Book.Add(ActKind.Broadcast, about, text, $"들은 사람 {b.HeardBy.Count} · 못 들은 사람 {b.Missed.Count}", "선내 방송", "", "pa:" + (about?.Id ?? -1) + ":" + priority, SimTime.Minutes(2), 6f,
            (world, act) => (b.Missed.Count == 0 ? 1 : 2, b.Missed.Count == 0 ? "모두 들었다" : $"{b.Missed.Count}명은 못 들었다 (스피커·잠)"));
        return b;
    }

    /// <summary>이 사람이 최근(within) 이 지시가 실린 방송을 들었나 — 못 들은 사람은 모른다.</summary>
    public Broadcast? Ordered(CrewMember c, string order, long within)
    {
        for (int i = Recent.Count - 1; i >= 0; i--)
        {
            var b = Recent[i];
            if (_w.Tick - b.Tick > within) break;
            if (b.Order == order && b.HeardBy.Contains(c.Id)) return b;
        }
        return null;
    }

    /// <summary>한 시간마다: 오래 탄 방 스피커는 녹는다.</summary>
    public void Update()
    {
        var w = _w;
        if (w.Tick < _check) return;
        _check = w.Tick + SimTime.Minutes(5);
        foreach (var r in w.Ship.LiveRooms)
            if (w.Fire.CountIn(r) >= 4 && !SpeakerBroken(r)) BreakSpeaker(r, "불에 녹았다");
    }
}

public sealed partial class AutomationSystem
{
    private PublicAddress? _speak;
    /// <summary>v16.6 선내 방송.</summary>
    public PublicAddress Speak => _speak ??= new PublicAddress(_world);
}
