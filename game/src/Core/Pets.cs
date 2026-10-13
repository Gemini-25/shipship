using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v23 반려동물 · 살아 있는 행성 (사용자가 고른 볼거리):
//  반려동물: 개(좋아하는 사람을 따라다닌다 · 꼬리) · 새(횃대에서 노래 · 가끔 날아다닌다) · 물고기(수조) · 벌(재배실 — 작물이 더 자란다) · 외계 애완동물(길들였다).
//   쓰다듬으면 긴장이 풀리고, 마음 병이 있는 사람 곁에 오래 있으면 '곁에 누가 있어야' 낫는 병이 조금 빨리 낫는다.
//  살아 있는 행성: 온화한 바다 · 늪 · 얼음 위성 속 바다 등에 외계 식물 · 동물. 내려앉아 바깥에 나가면 표본을 모으고, 실험실 · 의무실에서 연구한다 —
//   연구 점수 · 외계 씨앗(작물이 잘 자라거나 시든다) · 기생체(Parasite) · 배 안으로 숨어든 생물(로봇이 잡는다) · 길들인 외계 애완동물.

public enum PetKind : byte { Dog, Bird, Fish, Bees, Alien, Wild }

public sealed class Pet
{
    public int Id { get; init; }
    public PetKind Kind { get; init; }
    public string Name { get; init; } = "";
    public Vector2 Pos { get; internal set; }
    public Vector2 Facing { get; internal set; } = new(1f, 0f);
    public int RoomId { get; internal set; } = -1;
    public int Favorite { get; internal set; } = -1;
    public List<Cell>? Path { get; internal set; }
    public int PathIndex { get; internal set; }
    public long NextMove { get; internal set; }
    public long PettedAt { get; internal set; } = -1;
    public int Petted { get; internal set; }
    public bool Gone { get; internal set; }
    public string Doing { get; internal set; } = "";
    public Cell Cell => Cell.FromPosition(Pos);
}

public sealed class Specimen
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Kind { get; init; } = ""; // 식물 · 동물 · 미생물
    public string Planet { get; init; } = "";
    public float Studied { get; set; }
    public bool Done { get; set; }
}

public sealed class PetSystem
{
    public static bool Off;
    private readonly World _w;
    private readonly Rng _rng;
    private long _nextMin, _nextHour;
    private int _nextId, _nextSpec;
    public List<Pet> Pets { get; } = new();
    public List<Specimen> Specimens { get; } = new();
    public int Pettings, Comforts, Escapes, Caught, Tamed, SeedsGood, SeedsBad, Honey;

    private static readonly PathProfile Walk = new(0.3f, false, false, null, false, Robot: true);

    public PetSystem(World w) { _w = w; _rng = new Rng(unchecked(w.Seed * 67 + 777)); }

    public static string KindName(PetKind k) => k switch { PetKind.Dog => "개", PetKind.Bird => "새", PetKind.Fish => "물고기", PetKind.Bees => "벌", PetKind.Alien => "외계 애완동물", _ => "외계 생물" };

    private static readonly string[] DogNames = { "보리", "콩이", "별이", "누렁", "해피", "구름" }, BirdNames = { "짹짹", "노랑", "삐삐", "하늘" }, AlienNames = { "꼬물", "반디", "말랑", "초롱" };

    /// <summary>배를 띄울 때: 사람이 많은 배일수록 동물이 많다 (고양이는 Eco).</summary>
    public void Seed()
    {
        var w = _w;
        if (Off || Pets.Count > 0) return;
        int crew = w.Crew.Count(c => !c.Dead);
        uint h = (uint)w.Seed * 2654435761U;
        if (crew >= 6) Add(PetKind.Dog, DogNames[(int)(h % (uint)DogNames.Length)], RoomType.Lounge, RoomType.Mess);
        if (crew >= 8) Add(PetKind.Bird, BirdNames[(int)((h >> 8) % (uint)BirdNames.Length)], RoomType.Mess, RoomType.Lounge);
        if (w.Ship.FurnitureOf(FurnitureType.Aquarium).Any() || crew >= 12) Add(PetKind.Fish, "수조 물고기", RoomType.Mess, RoomType.Lounge);
        if (w.Ship.Rooms.Any(r => r.Kind == RoomType.Hydroponics)) Add(PetKind.Bees, "재배실 벌", RoomType.Hydroponics);
    }

    private Pet? Add(PetKind k, string name, params RoomType[] homes)
    {
        var w = _w;
        Room? room = null;
        foreach (var t in homes) { room = w.Ship.Rooms.FirstOrDefault(r => r.Kind == t && !r.Detached); if (room != null) break; }
        room ??= w.Ship.Rooms.FirstOrDefault(r => r.Type == RoomType.Lounge || r.Type == RoomType.Mess);
        var at = room?.Cells.Where(w.Ship.IsOpenFloor).OrderBy(c => (c.Center - room.Center).LengthSquared()).Cast<Cell?>().FirstOrDefault();
        if (room == null || at is not Cell cell) return null;
        var fav = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderByDescending(c => c.Traits.Sociability).ThenBy(c => c.Id).Skip(Pets.Count % 3).FirstOrDefault();
        var p = new Pet { Id = ++_nextId, Kind = k, Name = name, Pos = cell.Center, RoomId = room.Id, Favorite = fav?.Id ?? -1 };
        Pets.Add(p);
        return p;
    }

    // ─────────────────────────────── 시간 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (Off) return;
        if (Pets.Count == 0 && w.Tick > 10) Seed();
        foreach (var p in Pets) if (!p.Gone && p.Kind is not (PetKind.Fish or PetKind.Bees)) Move(p);
        if (w.Tick >= _nextMin) { _nextMin = w.Tick + SimTime.Minutes(5); foreach (var p in Pets.Where(p => !p.Gone)) Minute(p); }
        if (w.Tick >= _nextHour) { _nextHour = w.Tick + SimTime.Hours(1); Hour(); }
    }

    private void Move(Pet p)
    {
        var w = _w;
        if (p.Path != null && p.PathIndex < p.Path.Count)
        {
            var to = p.Path[p.PathIndex].Center;
            var d = to - p.Pos;
            float sp = p.Kind switch { PetKind.Dog => 0.06f, PetKind.Bird => 0.09f, PetKind.Wild => 0.08f, _ => 0.04f };
            if (d.Length() <= sp) { p.Pos = to; p.PathIndex++; }
            else { p.Facing = Vector2.Normalize(d); p.Pos += p.Facing * sp; }
            p.RoomId = w.Ship.RoomAt(p.Cell)?.Id ?? p.RoomId;
            return;
        }
        if (w.Tick < p.NextMove) return;
        p.NextMove = w.Tick + SimTime.Minutes(_rng.Range(4, 20));
        // 개는 좋아하는 사람 곁 · 새는 식당 · 휴게실 · 외계 생물은 아무 데나 (숨는다)
        Cell? goal = null;
        var fav = w.Crew.FirstOrDefault(c => c.Id == p.Favorite && !c.Dead && !c.Away && !c.Outside && c.Room != null);
        if (p.Kind is PetKind.Dog or PetKind.Alien && fav != null && _rng.Chance(0.7f)) goal = Near(fav.Cell, fav.Room);
        else
        {
            var rooms = w.Ship.Rooms.Where(r => !r.Detached && !r.Abandoned && (p.Kind == PetKind.Wild ? r.Type != RoomType.Corridor : r.Type is RoomType.Lounge or RoomType.Mess or RoomType.Quarters or RoomType.Corridor)).ToList();
            if (rooms.Count > 0) { var r = rooms[_rng.Range(0, rooms.Count)]; var floor = r.Cells.Where(w.Ship.IsOpenFloor).ToList(); if (floor.Count > 0) goal = floor[_rng.Range(0, floor.Count)]; }
        }
        if (goal is Cell g) { p.Path = w.Paths.Find(p.Cell, g, Walk); p.PathIndex = 0; }
    }

    private Cell? Near(Cell c, Room? room)
    {
        foreach (var d in Cell.Dirs8) { var n = c + d; if (_w.Ship.IsWalkable(n) && _w.Ship.RoomAt(n) == room) return n; }
        return null;
    }

    private void Minute(Pet p)
    {
        var w = _w;
        if (p.Kind == PetKind.Wild) { Wild(p); return; }
        // 곁의 사람: 쓰다듬기 (쉬는 사람 · 지친 사람이 더) · 마음 병 곁에 오래
        var near = w.Crew.Where(c => !c.Dead && c.IsAwake && !c.Outside && (c.Position - p.Pos).LengthSquared() < (p.Kind == PetKind.Fish ? 9f : 4f)).ToList();
        foreach (var c in near)
        {
            bool resting = c.Pose != Pose.Working;
            if (!resting || !_rng.Chance(0.15f + 0.4f * c.Needs.Stress)) continue;
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - (p.Kind == PetKind.Fish ? 0.02f : 0.05f));
            c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.03f);
            p.Petted++; p.PettedAt = w.Tick; Pettings++;
            if (c.Ailments.Any(MindCareSystem.MindAilment)) Comforts++;
            if (p.Petted % 25 == 1) w.Log.Add(w.Tick, LogKind.Life, p.Kind switch
            {
                PetKind.Dog => $"{Ko.IGa(c.Name)} {Ko.EulReul(p.Name)} 쓰다듬는다 — 꼬리가 바쁘다",
                PetKind.Bird => $"{p.Name}가 {c.Name} 어깨에 앉아 노래한다",
                PetKind.Fish => $"{Ko.IGa(c.Name)} 수조 앞에 오래 서 있다",
                PetKind.Alien => $"{Ko.IGa(c.Name)} {Ko.EulReul(p.Name)} 쓰다듬자 몸에서 불빛이 난다",
                _ => $"{c.Name}과 {p.Name}",
            }, c.Id);
            if (c.Id != p.Favorite && _rng.Chance(0.05f)) p.Favorite = c.Id;
        }
        p.Doing = p.Path != null && p.PathIndex < p.Path.Count ? "돌아다닌다" : near.Count > 0 ? "사람 곁" : "쉰다";
    }

    /// <summary>쓰다듬은 지 열두 시간 안 — 마음 돌봄 회복에 조금 더한다.</summary>
    public float ComfortGain(CrewMember c) => Off ? 0f : Pets.Any(p => !p.Gone && p.Kind != PetKind.Wild && p.PettedAt >= 0 && _w.Tick - p.PettedAt < SimTime.Hours(12) && (p.Pos - c.Position).LengthSquared() < 16f) ? 0.3f : 0f;

    // ─────────────────────────────── 살아 있는 행성 ───────────────────────────────

    /// <summary>행성의 생명 (0 없음 ~ 1 넘친다) — 행성 씨앗으로 정한다 (난수 흐름을 건드리지 않는다).</summary>
    public static float LifeOf(Planet p)
    {
        if (p.Pressure <= 0.5f && p.Body != PlanetBody.IceMoon) return 0f;
        float b = p.Body switch { PlanetBody.IceMoon => 0.5f, PlanetBody.Titan => 0.25f, PlanetBody.GasGiant => 0.2f, PlanetBody.Venus => 0.1f, _ => 0f };
        if (p.Body == PlanetBody.Rocky) b = p.Climate switch { PlanetClimate.Mild => 0.6f, PlanetClimate.Cold => 0.25f, PlanetClimate.Hot => 0.2f, _ => 0f } + p.Surface switch { PlanetSurface.Shallows => 0.3f, PlanetSurface.Mud => 0.25f, _ => 0f };
        uint h = (uint)p.Seed * 0x9E3779B1U;
        return Math.Clamp(b * (0.4f + 0.9f * ((h >> 8) & 0xff) / 255f), 0f, 1f);
    }

    public static string LifeLine(Planet p)
    {
        float l = LifeOf(p);
        return l <= 0.05f ? "생명 없음" : l < 0.3f ? "미생물 · 이끼" : l < 0.6f ? "외계 식물 · 작은 동물" : "숲 · 무리 지은 동물";
    }

    private static readonly string[] Plants = { "유리 이끼", "빛 고사리", "노래하는 갈대", "숨 쉬는 덩굴", "푸른 버섯 숲" }, Animals = { "여섯 다리 도마뱀", "날개 해파리", "모래 두더지", "빛 나방 떼", "돌 껍질 게" }, Microbes = { "붉은 점균", "빛나는 포자", "얼음 속 미생물" };

    private void Hour()
    {
        var w = _w;
        // 표본: 내려앉은 살아 있는 땅에서 바깥에 나간 사람이 있으면
        if (w.Planet.Landed && w.Planet.Here is Planet pl && LifeOf(pl) > 0.05f && w.Crew.Any(c => !c.Dead && (c.Outside || c.Away)) && _rng.Chance(0.25f + 0.4f * LifeOf(pl)))
            Collect(pl);
        // 연구: 실험실 · 의무실 · 연구자
        var open = Specimens.Where(s => !s.Done).ToList();
        if (open.Count > 0)
        {
            var who = w.Crew.Where(c => !c.Dead && c.CanAct && !c.Outside).OrderByDescending(c => c.RawSkill(Skill.Botany) + c.RawSkill(Skill.Medicine)).ThenBy(c => c.Id).FirstOrDefault();
            float lab = w.Ship.KindOf(RoomType.Lab).Any() ? 1.6f : 1f;
            var s = open[0];
            s.Studied += (0.04f + 0.08f * ((who?.RawSkill(Skill.Botany) ?? 0f) + (who?.RawSkill(Skill.Medicine) ?? 0f))) * lab;
            if (s.Studied >= 1f) Finish(s, who);
        }
        // 벌: 재배실 작물이 조금 더 자라고 꿀
        if (Pets.Any(p => p.Kind == PetKind.Bees && !p.Gone) && _rng.Chance(0.04f)) { Honey++; w.Log.Add(w.Tick, LogKind.Life, "재배실 벌통에서 꿀을 한 숟갈 땄다 — 오늘 식탁이 달다"); foreach (var c in w.Crew.Where(c => !c.Dead)) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.01f); }
    }

    public Specimen Collect(Planet pl, string? kind = null)
    {
        var w = _w;
        kind ??= _rng.Chance(0.5f) ? "식물" : _rng.Chance(0.6f) ? "동물" : "미생물";
        var pool = kind == "식물" ? Plants : kind == "동물" ? Animals : Microbes;
        var s = new Specimen { Id = ++_nextSpec, Name = pool[_rng.Range(0, pool.Length)], Kind = kind, Planet = pl.Name };
        Specimens.Add(s);
        w.Log.Add(w.Tick, LogKind.Life, $"표본 — {pl.Name}의 {s.Name} ({kind})");
        w.History.Add(w, HistoryKind.Incident, $"{pl.Name}에서 {s.Name}을 가져왔다 — 처음 보는 {kind}");
        return s;
    }

    private void Finish(Specimen s, CrewMember? who)
    {
        var w = _w;
        s.Done = true;
        w.Research += 6f;
        string by = who?.Name ?? "주 컴퓨터";
        float roll = _rng.Float();
        string end;
        if (s.Kind == "식물")
        {
            if (roll < 0.65f) { SeedsGood++; end = "외계 씨앗을 재배실에 섞었다 — 잘 자란다"; foreach (var r in w.Ship.Rooms.Where(r => r.Kind == RoomType.Hydroponics)) MarkLog.Add(r.Marks, w.Tick, $"{s.Name} 씨앗을 섞어 심었다"); }
            else { SeedsBad++; end = "외계 씨앗이 다른 작물을 시들게 했다"; w.Hazards.FireStory("CropBlight", w.Ship.Rooms.FirstOrDefault(r => r.Kind == RoomType.Hydroponics)); }
        }
        else if (s.Kind == "동물")
        {
            if (roll < 0.35f) { var p = Add(PetKind.Alien, AlienNames[Tamed % AlienNames.Length], RoomType.Lab, RoomType.Lounge); Tamed++; end = $"길들였다 — {p?.Name ?? "외계 애완동물"}"; }
            else if (roll < 0.6f) { Escape(s.Name); end = "우리를 빠져나가 배 안으로 숨었다"; }
            else end = "연구를 마치고 표본으로 보관했다";
        }
        else
        {
            if (roll < 0.3f && w.Crew.Where(c => !c.Dead && c.CanAct).OrderBy(c => c.Id).FirstOrDefault(c => c.Name == by) is CrewMember infected) { w.Parasite.Infect(infected, s.Name); end = "연구하던 사람에게 무언가 옮았다"; }
            else end = "약 재료를 얻었다";
        }
        w.Log.Add(w.Tick, LogKind.Ship, $"{s.Name} 연구 끝 ({by}) — {end}");
        w.History.Add(w, HistoryKind.Incident, $"{s.Name} 연구 — {end}");
    }

    /// <summary>외계 생물이 배 안으로 숨어들었다 — 로봇이 잡는다.</summary>
    public Pet? Escape(string name)
    {
        var w = _w;
        var p = Add(PetKind.Wild, name, RoomType.Lab, RoomType.Storage, RoomType.Hydroponics);
        if (p == null) return null;
        Escapes++;
        w.RaiseAlert($"{name}이 배 안으로 숨었다 — 로봇이 찾는다", w.Ship.Rooms.FirstOrDefault(r => r.Id == p.RoomId), AlertLevel.Warning, shipWide: true);
        return p;
    }

    private void Wild(Pet p)
    {
        var w = _w;
        // 사람을 놀라게 하고 · 식량을 뒤진다 · 로봇이 같은 방에 들어오면 잡힐 수 있다
        foreach (var c in w.Crew.Where(c => !c.Dead && c.IsAwake && (c.Position - p.Pos).LengthSquared() < 6f).Take(1))
            if (_rng.Chance(0.2f)) { c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.06f); w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {p.Name}을 보고 비명을 질렀다", c.Id); }
        if (_rng.Chance(0.02f) && w.Ship.Rooms.FirstOrDefault(r => r.Id == p.RoomId) is Room room) w.Hazards.FireStory("WeevilSwarm", room);
        bool bot = w.Robots.Robots.Any(r => r.Operational && (r.Position - p.Pos).LengthSquared() < 4f && r.Kind is RobotKind.Safety or RobotKind.Sentry or RobotKind.Cleaner or RobotKind.Utility or RobotKind.Hauler);
        if (bot && _rng.Chance(0.25f) || w.Tick % SimTime.TicksPerDay < SimTime.Minutes(5) && _rng.Chance(0.3f))
        {
            p.Gone = true; Caught++;
            w.Log.Add(w.Tick, LogKind.Work, $"{p.Name}을 붙잡아 실험실 우리에 다시 넣었다");
        }
    }

    public string? Line() => Off || Pets.Count(p => !p.Gone) == 0 ? null : string.Join(" · ", Pets.Where(p => !p.Gone).Select(p => p.Kind == PetKind.Wild ? $"⚠ {p.Name}" : p.Name));

    public void Hash(Action<long> I, Action<float> F) { I(Pets.Count); foreach (var p in Pets) { F(p.Pos.X); F(p.Pos.Y); } I(Specimens.Count); }
}
