using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.7 관찰 도구: 평화로운 장면도 잡는 눈 · 물건이 거친 손.
// 세상을 읽기만 한다 — World 에 붙지 않고 지문(StateHash)에도 들어가지 않는다 (카메라가 있든 없든 같은 역사).

/// <summary>카메라가 잡을 만한 장면의 종류.</summary>
public enum WatchKind { Gift, LateGuest, Reconcile, Artwork, Confession, Memorial }

/// <summary>장면 한 건: 언제 · 무엇 · 어디(방 · 칸) · 누가 · 얼마나 볼 만한가.</summary>
public sealed record WatchShot(long Tick, WatchKind Kind, string Text, int RoomId, Vector2? At, int[] Who, float Weight);

/// <summary>물건이 거친 한 걸음 (언제 · 누구 · 무엇을 했나).</summary>
public readonly record struct ItemStep(long Tick, int Who, string Text);

public sealed class WatchScenes
{
    private HistoryEvent? _lastEvent;
    private long _logVersion = -1;
    private readonly Dictionary<int, int> _trailSeen = new();

    /// <summary>장면 종류마다 볼 만한 정도 (같은 때 여럿이면 무거운 쪽).</summary>
    public static float WeightOf(WatchKind k) => k switch
    {
        WatchKind.Memorial => 1f, WatchKind.Confession => 0.9f, WatchKind.Reconcile => 0.85f,
        WatchKind.Gift => 0.8f, WatchKind.Artwork => 0.75f, _ => 0.6f,
    };

    /// <summary>글 한 줄이 어떤 평화로운 장면인지 (아니면 null).</summary>
    public static WatchKind? Classify(string text)
    {
        if (text.Contains("추모") || text.Contains("기리는")) return WatchKind.Memorial;
        if (text.Contains("고백") || text.Contains("연인이 됐다") || text.Contains("털어놓았다")) return WatchKind.Confession;
        if (text.Contains("화해") || text.Contains("사과했다") || text.Contains("일을 풀었다")) return WatchKind.Reconcile;
        if (text.Contains("선물") || text.Contains("생일 카드")) return WatchKind.Gift;
        if (text.Contains("완성") || text.Contains("원고를 다 썼다")) return WatchKind.Artwork;
        if (text.Contains("늦게 왔다")) return WatchKind.LateGuest;
        return null;
    }

    /// <summary>지난번 뒤로 새로 생긴 장면들 (오래된 것부터). 처음 부르면 지금까지는 건너뛴다.</summary>
    public List<WatchShot> Poll(World w)
    {
        var shots = new List<WatchShot>();
        bool first = _logVersion < 0;

        // 1) 연대기 (선물 · 완성 · 추모 · 화해 · 고백 — 방과 사람이 함께 적힌다)
        var evs = w.History.Events;
        int from = evs.Count;
        if (_lastEvent != null) { from = evs.Count; for (int i = evs.Count - 1; i >= 0 && !ReferenceEquals(evs[i], _lastEvent); i--) from = i; }
        if (!first)
            for (int i = from; i < evs.Count; i++)
                if (Classify(evs[i].Text) is WatchKind k)
                {
                    var e = evs[i];
                    Vector2? at = e.At is Cell c ? new Vector2(c.X + 0.5f, c.Y + 0.5f) : CenterOf(w, e.RoomId) ?? CrewAt(w, e.CrewIds.FirstOrDefault(-1));
                    shots.Add(new WatchShot(e.Tick, k, e.Text, e.RoomId, at, e.CrewIds, WeightOf(k)));
                }
        if (evs.Count > 0) _lastEvent = evs[^1];

        // 2) 하루 기록 (늦은 생일 선물 · 사과 · 연인 · 추모일 촛불 — 사람 한 명이 적힌다)
        var log = w.Log;
        if (!first)
        {
            long fresh = Math.Min(log.Version - _logVersion, log.Entries.Count);
            for (int i = log.Entries.Count - (int)fresh; i < log.Entries.Count; i++)
            {
                var e = log.Entries[i];
                if (e.Kind != LogKind.Life || Classify(e.Text) is not WatchKind k) continue;
                if (shots.Any(s => s.Tick == e.Tick && s.Kind == k)) continue; // 연대기에도 적힌 같은 장면
                var who = w.Crew.FirstOrDefault(c => c.Id == e.CrewId);
                shots.Add(new WatchShot(e.Tick, k, (who != null ? who.Name + " · " : "") + e.Text, who?.Room?.Id ?? -1, who?.Position, who != null ? new[] { who.Id } : Array.Empty<int>(), WeightOf(k) * 0.9f));
            }
        }
        _logVersion = log.Version;

        // 3) 함께하는 장면 (영화의 밤에 늦게 온 사람 · 바닥에 앉은 사람)
        foreach (var s in w.Scenes.Scenes)
        {
            int seen = _trailSeen.GetValueOrDefault(s.Id, first ? s.Trail.Count : 0);
            for (int i = Math.Min(seen, s.Trail.Count); i < s.Trail.Count && !first; i++)
                if (s.Trail[i].Contains("늦게 왔다"))
                {
                    string name = s.Trail[i].Split(' ').Skip(1).FirstOrDefault()?.TrimEnd(':') ?? "";
                    var late = w.Crew.FirstOrDefault(c => c.Name == name);
                    shots.Add(new WatchShot(w.Tick, WatchKind.LateGuest, $"{s.Title} · {(late != null ? Ko.IGa(late.Name) + " 늦게 왔다" : "늦게 온 사람")}", s.RoomId,
                        late?.Position ?? new Vector2(s.Spot.X + 0.5f, s.Spot.Y + 0.5f), late != null ? new[] { late.Id } : Array.Empty<int>(), WeightOf(WatchKind.LateGuest)));
                }
            _trailSeen[s.Id] = s.Trail.Count;
        }
        return shots;
    }

    private static Vector2? CenterOf(World w, int roomId)
    {
        if (roomId < 0 || roomId >= w.Ship.Rooms.Count) return null;
        var r = w.Ship.Rooms[roomId];
        if (r.Cells.Count == 0) return null;
        float x = 0f, y = 0f;
        foreach (var c in r.Cells) { x += c.X + 0.5f; y += c.Y + 0.5f; }
        return new Vector2(x / r.Cells.Count, y / r.Cells.Count);
    }

    private static Vector2? CrewAt(World w, int id) => w.Crew.FirstOrDefault(c => c.Id == id)?.Position;

    // ─────────────────────────── 물건 따라가기 ───────────────────────────

    /// <summary>따라가 볼 만한 물건: 선물 · 사진 · 공구 · 작품 · 유품 — 손을 많이 거친 것부터.</summary>
    public static List<Belonging> Notable(World w) =>
        w.Belongings.All
            .Where(b => b.From >= 0 || b.Memorial || b.Kind is BelongingKind.Photo or BelongingKind.Toolset or BelongingKind.Artwork)
            .OrderByDescending(b => Hands(b).Count * 3 + b.Marks.Count + (b.From >= 0 ? 2 : 0) + (b.Memorial ? 2 : 0))
            .ThenBy(b => b.Id).ToList();

    /// <summary>손을 거친 사람들 (만든 사람 → 준 사람 → 임자 → 빌려 간 · 들고 있는 사람), 겹치지 않게.</summary>
    public static List<int> Hands(Belonging b)
    {
        var list = new List<int>();
        void Add(int id) { if (id >= 0 && (list.Count == 0 || list[^1] != id) && !list.Contains(id)) list.Add(id); }
        Add(b.Maker); Add(b.From); Add(b.Owner); Add(b.BorrowedBy); Add(b.Holder);
        return list;
    }

    /// <summary>물건의 내력: 처음 (어디서 · 누가 만들었나) · 남은 자국들 · 지금 어디에.</summary>
    public static List<ItemStep> Trail(World w, Belonging b)
    {
        string N(int id) => w.Crew.FirstOrDefault(c => c.Id == id)?.Name ?? "누군가";
        var steps = new List<ItemStep>();
        if (b.Maker >= 0) steps.Add(new ItemStep(-1, b.Maker, $"{Ko.IGa(N(b.Maker))} 만들었다"));
        else if (b.Origin.Length > 0) steps.Add(new ItemStep(-1, b.Owner, b.Origin));
        if (b.From >= 0 && b.From != b.Maker) steps.Add(new ItemStep(-1, b.From, $"{Ko.IGa(N(b.From))} 건넸다"));
        if (b.From >= 0) steps.Add(new ItemStep(-1, b.Owner, $"{N(b.Owner)}에게 선물로 왔다"));
        foreach (var m in b.Marks) steps.Add(new ItemStep(m.Tick, -1, m.Text));
        steps.Add(new ItemStep(w.Tick, b.Holder >= 0 ? b.Holder : b.BorrowedBy >= 0 ? b.BorrowedBy : b.Owner, "지금 " + w.Belongings.Where(b)));
        return steps;
    }

    /// <summary>물건이 지금 있는 자리 (든 사람이 있으면 그 사람 자리).</summary>
    public static Vector2 PositionOf(World w, Belonging b)
    {
        if (b.Holder >= 0 && w.Crew.FirstOrDefault(c => c.Id == b.Holder) is CrewMember h) return h.Position;
        var c0 = w.Belongings.CellOf(b);
        return new Vector2(c0.X + 0.5f, c0.Y + 0.5f);
    }
}
