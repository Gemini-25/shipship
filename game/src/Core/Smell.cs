using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.8 냄새: 방마다 종류별 세기(요리 · 빵 · 탄내 · 악취)가 열린 문과 환기 덕트(공기 흐름)를 타고 번지고, 시간이 지나면 흩어진다 (3분 간격).
// 승무원은 자기가 맡은 것만 안다 — 맡은 방 · 때 · 세기. 빵 굽는 냄새에 배고픈 사람이 주방으로 모이고,
// 탄 냄새를 맡은 사람은 감지기보다 먼저 냄새를 따라 확인하러 간다(불 위에 남은 냄비 · 달아오른 임시 이음 · 감지기가 없는 방의 불),
// 쓰레기 · 상한 음식 냄새에는 얼굴을 찌푸리고 불평한다. 커피 냄새(일상의 커피 장면)에는 졸린 사람이 모이고, 엎지른 국은 오래 두면 쉰내가 된다.
// 공기청정기(이동식 장비)가 도는 방은 냄새가 빨리 빠진다 · 음식 냄새는 배를 고프게 한다(식욕).
// 불쾌한 냄새(탄내 · 악취)는 Ambience의 room.Smell(소음 · 진동과 함께 잠 · 휴식 · 스트레스를 깎는 값)에 합쳐진다 — 따로 두 번 세지 않는다.

public enum SmellKind { Cooking, Bread, Burnt, Foul, Coffee }

public sealed class SmellStats
{
    public int Sniffs, BurntSniffs, Gathered, Checks, Found, FireBySmell, Complaints, Woken, Appetite, CoffeeGathered, Refused;
    public string Summary() =>
        $"냄새 맡음 {Sniffs}(탄내 {BurntSniffs}) · 냄새 따라 모임 {Gathered} · 탄내 확인 {Checks}(찾아냄 {Found} · 감지기보다 먼저 불 {FireBySmell}) · 불평 {Complaints} · 탄내에 깸 {Woken} · 냄새에 배고파짐 {Appetite} · 커피 냄새에 모임 {CoffeeGathered} · 나눠 주지 않음 {Refused}";
}

public sealed class SmellSystem
{
    public const int Kinds = 5;
    public struct Sniff { public long Tick; public int Room; public float Strength; }

    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 31));
    private float[] _lvl = Array.Empty<float>(), _src = Array.Empty<float>(), _tmp = Array.Empty<float>();
    private float _acc;
    private readonly Dictionary<int, Sniff[]> _sniff = new();
    private readonly Dictionary<int, long> _gathered = new();
    private readonly Dictionary<int, long> _complained = new();
    private readonly Dictionary<int, (long tick, float strength)> _resolved = new();
    private readonly Dictionary<(int room, int crew), long> _checking = new(); // 방마다 확인하러 나선 사람들 (여럿이 같은 냄새를 따라갈 수 있다)
    private readonly Dictionary<int, (int room, long tick)> _asked = new();
    public SmellStats Stats { get; } = new();

    public SmellSystem(World w) => _w = w;

    public static string Name(SmellKind k) => k switch
    {
        SmellKind.Cooking => "요리 냄새", SmellKind.Bread => "빵 굽는 냄새", SmellKind.Burnt => "탄 냄새", SmellKind.Coffee => "커피 냄새", _ => "쓰레기 냄새",
    };

    /// <summary>코에 닿는 문턱 (빵 냄새는 멀리서도 · 악취는 진해야 불평).</summary>
    public static float Threshold(SmellKind k) => k switch { SmellKind.Cooking => 0.12f, SmellKind.Bread => 0.08f, SmellKind.Burnt => 0.03f, SmellKind.Coffee => 0.1f, _ => 0.3f };
    /// <summary>사람마다 코가 다르다 (0.8~1.2).</summary>
    public static float Nose(CrewMember c) => 0.8f + 0.4f * ((c.Id * 37 + 11) % 10) / 9f;

    private const float Tau0 = 0.6f, Tau1 = 0.8f, Tau2 = 1.2f, Tau3 = 2f, Tau4 = 0.7f; // 흩어지는 시간 (시간)
    private static float Tau(int k) => k switch { 0 => Tau0, 1 => Tau1, 2 => Tau2, 3 => Tau3, _ => Tau4 };

    public float Level(Room r, SmellKind k)
    {
        int i = r.Id * Kinds + (int)k;
        return i < _lvl.Length ? _lvl[i] : 0f;
    }

    /// <summary>Ambience 훅: 불쾌한 냄새 (탄내 · 악취) — room.Smell에 합쳐진다.</summary>
    public float Unpleasant(Room r) => MathF.Max(Level(r, SmellKind.Burnt) * 0.7f, Level(r, SmellKind.Foul));

    /// <summary>가장 센 냄새 (화면).</summary>
    public SmellKind? Dominant(Room r, out float v)
    {
        v = 0f;
        SmellKind? best = null;
        for (int k = 0; k < Kinds; k++)
        {
            float x = Level(r, (SmellKind)k);
            if (x > v && x >= Threshold((SmellKind)k) * 0.6f) { v = x; best = (SmellKind)k; }
        }
        return best;
    }

    /// <summary>냄새가 나는 곳 (Cooking · 불 · 전선이 부른다): 그 방은 적어도 이만큼.</summary>
    public void Emit(Room? r, SmellKind k, float v)
    {
        if (r == null || r.Detached) return;
        int i = r.Id * Kinds + (int)k;
        if (i < _src.Length && v > _src[i]) _src[i] = MathF.Min(1f, v);
    }

    private Sniff[] SniffsOf(CrewMember c)
    {
        if (!_sniff.TryGetValue(c.Id, out var a)) _sniff[c.Id] = a = new Sniff[Kinds];
        return a;
    }

    /// <summary>이 사람이 최근에 맡은 냄새 (모르면 null).</summary>
    public Sniff? Smelled(CrewMember c, SmellKind k, int withinTicks)
    {
        if (!_sniff.TryGetValue(c.Id, out var a)) return null;
        var s = a[(int)k];
        return s.Tick > 0 && _w.Tick - s.Tick <= withinTicks ? s : null;
    }

    /// <summary>냄새를 따라간다: 지금 방과 이웃 방 가운데 그 냄새가 가장 진한 쪽 (문간에서 킁킁 맡아 본다).</summary>
    public Room? Trail(CrewMember c, SmellKind k)
    {
        if (c.Room is not Room here) return null;
        Room best = here;
        float v = Level(here, k);
        foreach (var (o, _) in _w.Ambience.Neighbors(here))
        {
            if (o.Detached || o.OffLimits) continue;
            float x = Level(o, k);
            if (x > v * 1.05f) { v = x; best = o; }
        }
        return best;
    }

    /// <summary>냄새를 끝까지 따라간다: 문간마다 맡아 보며 더 진한 쪽으로 (몇 방까지) — 냄새가 나는 곳의 방.</summary>
    public Room? Source(CrewMember c, SmellKind k, int steps = 6)
    {
        if (c.Room is not Room here) return null;
        Room at = here;
        for (int i = 0; i < steps; i++)
        {
            Room next = at;
            float v = Level(at, k);
            foreach (var (o, _) in _w.Ambience.Neighbors(at))
            {
                if (o.Detached || o.OffLimits) continue;
                float x = Level(o, k);
                if (x > v * 1.05f) { v = x; next = o; }
            }
            if (next == at) break;
            at = next;
        }
        return at;
    }

    /// <summary>어디로 가 볼까: 냄새를 따라가고, 고루 퍼져 갈피를 못 잡으면 음식 · 탄 냄새는 주방부터 (냄새가 어디서 나기 쉬운지는 안다).</summary>
    public Room? Likely(CrewMember c, SmellKind k, int steps = 6)
    {
        var t = Source(c, k, steps);
        if (t == null || t != c.Room || k is SmellKind.Foul or SmellKind.Coffee) return t;
        Room? g = null;
        float best = -1f;
        foreach (var r in _w.Ship.RoomsOf(RoomType.Galley))
        {
            if (r.OffLimits || r.Detached || r == c.Room) continue;
            float x = Level(r, k);
            if (x > best) { best = x; g = r; }
        }
        return g != null && best >= Level(c.Room!, k) * 0.9f ? g : t;
    }

    public void Update(float dt)
    {
        _acc += dt;
        if (_acc < 0.05f) return;
        float h = _acc;
        _acc = 0f;
        var w = _w;
        var rooms = w.Ship.Rooms;
        int n = rooms.Count * Kinds;
        if (_lvl.Length != n)
        {
            var grown = new float[n];
            Array.Copy(_lvl, grown, Math.Min(_lvl.Length, n));
            _lvl = grown; _src = new float[n]; _tmp = new float[n];
        }
        Array.Clear(_src);
        Sources();
        for (int i = 0; i < n; i++) if (_src[i] > _lvl[i]) _lvl[i] += (_src[i] - _lvl[i]) * MathF.Min(1f, h * 8f);
        // 문: 열린 만큼 섞인다 (닫힌 문 틈으로도 조금)
        Array.Copy(_lvl, _tmp, n);
        foreach (var d in w.Ship.Doors)
        {
            if (d.RoomA is not Room a || d.RoomB is not Room b || a.Detached || b.Detached) continue;
            float open = d.Removed ? 1f : d.Openness;
            float conv = 1f + 0.4f * MathF.Min(4f, MathF.Abs(a.Air.Temperature - b.Air.Temperature)); // 더운 방(오븐 · 화구 · 불)의 공기는 문 위로 빠져나가고 찬 공기가 아래로 든다
            float k = MathF.Min(0.45f, (open > 0.05f ? 0.25f + 0.75f * open : 0.18f) * conv * h * 6f); // 닫힌 문도 틈 · 오가는 사람으로 샌다
            for (int s = 0; s < Kinds; s++)
            {
                float flux = (_lvl[a.Id * Kinds + s] - _lvl[b.Id * Kinds + s]) * k * 0.5f;
                _tmp[a.Id * Kinds + s] -= flux;
                _tmp[b.Id * Kinds + s] += flux;
            }
        }
        (_lvl, _tmp) = (_tmp, _lvl);
        // 환기 덕트: 팬이 도는 방끼리 평균 쪽으로 (공기가 덜 오는 방은 덜)
        Span<float> sum = stackalloc float[Kinds];
        float vol = 0f;
        foreach (var r in rooms)
        {
            if (r.Detached || !Atmosphere.Vented(r)) continue;
            float wv = r.Volume * r.AirFlow;
            vol += wv;
            for (int s = 0; s < Kinds; s++) sum[s] += _lvl[r.Id * Kinds + s] * wv;
        }
        float mix = 1f - MathF.Exp(-0.8f * h);
        Span<bool> clean = stackalloc bool[rooms.Count];
        foreach (var r in rooms) clean[r.Id] = w.Portable.SmellMul(r) < 1f;
        foreach (var r in rooms)
        {
            bool vented = !r.Detached && Atmosphere.Vented(r);
            for (int s = 0; s < Kinds; s++)
            {
                int i = r.Id * Kinds + s;
                if (vented && vol > 0f) _lvl[i] += (sum[s] / vol - _lvl[i]) * mix * MathF.Min(1f, r.AirFlow);
                float decay = MathF.Exp(-h / Tau(s)) * (vented ? MathF.Exp(-0.3f * h * r.AirFlow) : 1f) * (clean[r.Id] ? MathF.Exp(-2f * h) : 1f); // 공기청정기
                _lvl[i] = r.Detached ? 0f : MathF.Max(0f, _lvl[i] * decay);
                if (_lvl[i] < 0.002f) _lvl[i] = 0f;
            }
        }
        Perceive(h);
    }

    private void Sources()
    {
        var w = _w;
        var rooms = w.Ship.Rooms;
        w.Cooking.AddSmells(this);
        w.Portable.AddSmells(this); // v16.7 히터 먼지 · 달아오른 콘센트
        // 불 · 연기: 타는 냄새 (감지기는 불꽃을 봐야 울린다 — 코는 연기만 닿아도 안다)
        if (w.Fire.Count > 0)
            foreach (var (cell, v) in w.Fire.Fires) Emit(w.Ship.RoomAt(cell), SmellKind.Burnt, 0.5f + 0.5f * MathF.Min(1f, v));
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached) continue;
            if (r.Air.Smoke > 0.02f) Emit(r, SmellKind.Burnt, MathF.Min(0.8f, r.Air.Smoke * 1.5f));
            // 오염: 균 · 기름 · 분진이 쌓인 방은 퀴퀴하다
            var soil = w.Soil.RoomSoil(r);
            float foul = MathF.Max(soil[(int)SoilKind.Bio] * 0.7f, MathF.Max(soil[(int)SoilKind.Oil] * 0.3f, soil[(int)SoilKind.Soot] * 0.35f));
            if (foul > 0.05f) Emit(r, SmellKind.Foul, foul);
        }
        // 일상: 커피를 타는 곳 · 놓아 둔 잔 (커피 · 차) · 엎지른 국 (갓 엎지른 건 국 냄새, 오래 두면 쉰내)
        foreach (var sc in w.Scenes.Scenes)
        {
            if (!sc.Open && sc.Kind != SceneKind.Spill) continue;
            if (sc.Kind == SceneKind.Coffee && sc.Stage == SceneStage.Run && sc.RoomId >= 0 && sc.RoomId < rooms.Count) Emit(rooms[sc.RoomId], SmellKind.Coffee, sc.Tea ? 0.35f : 0.6f);
            foreach (var t in sc.Things)
            {
                if (t.Until >= 0 && w.Tick > t.Until) continue;
                if (t.Kind == ThingKind.Cup && !t.Tea) Emit(w.Ship.RoomAt(t.At), SmellKind.Coffee, 0.2f * t.Amount);
                else if (t.Kind == ThingKind.Stain)
                {
                    long age = w.Tick - t.Since;
                    if (age < SimTime.Hours(3)) Emit(w.Ship.RoomAt(t.At), SmellKind.Cooking, 0.2f);
                    else Emit(w.Ship.RoomAt(t.At), SmellKind.Foul, MathF.Min(0.5f, 0.2f + 0.05f * (age - SimTime.Hours(3)) / SimTime.TicksPerHour));
                }
            }
        }
        // 재활용실 · 쓰레기: 방 자체의 퀴퀴함이 문 · 덕트를 타고 번진다
        foreach (var r in rooms)
            if (!r.Detached && r.Kind == RoomType.Recycling) Emit(r, SmellKind.Foul, 0.35f);
        // 달아오른 임시 이음: 피복 타는 냄새 (불꽃이 튀기 전에)
        foreach (var l in w.Net.Links)
        {
            if (l.Kind != NetKind.Power || !l.Temp || l.Cut) continue;
            float heat = w.Flow.SpliceHeat(l);
            if (heat > 0.4f) Emit(l.Room, SmellKind.Burnt, (heat - 0.4f) * 1.6f);
        }
    }

    private void Perceive(float h)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Outside || c.Room is not Room room || c.Suit != null) continue;
            bool awake = c.IsAwake;
            var arr = SniffsOf(c);
            float nose = Nose(c);
            for (int k = 0; k < Kinds; k++)
            {
                var kind = (SmellKind)k;
                float v = Level(room, kind) * nose;
                if (!awake)
                {
                    // 자다가도 타는 냄새가 진하면 깬다
                    if (kind != SmellKind.Burnt || v < 0.3f || c.Down) continue;
                    Stats.Woken++;
                    c.Jolt(w);
                }
                if (v < Threshold(kind)) continue;
                bool fresh = w.Tick - arr[k].Tick > SimTime.Minutes(30) || arr[k].Tick <= 0;
                arr[k] = new Sniff { Tick = w.Tick, Room = room.Id, Strength = v };
                if (!fresh) continue;
                Stats.Sniffs++;
                if (kind == SmellKind.Burnt)
                {
                    Stats.BurntSniffs++;
                    c.Say(w, Persona.Say(c, "어디서 타는 냄새가…"));
                    w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {room.Name}에서 탄 냄새를 맡았다", c.Id);
                    if (c.Job?.Urgent != true && !c.Down) c.Interrupt(w); // 하던 걸 멈추고 킁킁
                }
                else if (kind is SmellKind.Bread or SmellKind.Cooking)
                {
                    if (c.Needs.Hunger > 0.25f) Stats.Appetite++; // 냄새에 배가 꼬르륵
                    if (kind == SmellKind.Bread && R.Chance(0.4f)) c.Say(w, Persona.Say(c, "빵 냄새…"));
                    else if (c.Needs.Hunger > 0.45f && R.Chance(0.3f)) c.Say(w, Persona.Say(c, "냄새 좋다… 배고프네"));
                }
                else if (kind == SmellKind.Coffee && c.Needs.Fatigue > 0.4f && R.Chance(0.4f)) c.Say(w, Persona.Say(c, "커피 냄새…"));
            }
            if (!awake) continue;
            if (Level(room, SmellKind.Bread) * nose > Threshold(SmellKind.Bread) || Level(room, SmellKind.Cooking) * nose > Threshold(SmellKind.Cooking))
            {
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.015f * h); // 좋은 냄새
                c.Needs.Food = MathF.Max(0f, c.Needs.Food - 0.04f * h); // 식욕: 냄새를 맡으면 배가 더 빨리 고파진다 (끼니를 당긴다)
            }
            if (Level(room, SmellKind.Coffee) * nose > Threshold(SmellKind.Coffee)) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.01f * h);
            if (room.Smell * nose > Threshold(SmellKind.Foul) && (!_complained.TryGetValue(c.Id, out var t) || w.Tick - t > SimTime.Hours(6)) && R.Chance(MathF.Min(1f, 1.5f * h)))
                Complain(c, room);
        }
        if (_checking.Count > 0)
            foreach (var key in _checking.Keys.OrderBy(k => k.room).ThenBy(k => k.crew).ToList())
                if (w.Tick - _checking[key] > SimTime.Hours(1)) _checking.Remove(key);
    }

    private void Complain(CrewMember c, Room room)
    {
        var w = _w;
        _complained[c.Id] = w.Tick;
        Stats.Complaints++;
        bool spoiled = w.Cooking.Batches.Any(b => b.Spoiled && b.Room == room);
        string what = spoiled ? "상한 음식" : Level(room, SmellKind.Burnt) * 0.7f >= Level(room, SmellKind.Foul) && Level(room, SmellKind.Burnt) > 0.1f ? "탄내" : Level(room, SmellKind.Foul) > 0.2f ? "쓰레기 · 오물" : "퀴퀴한 공기";
        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.02f);
        c.Say(w, Persona.Say(c, spoiled ? "뭐가 상했나 봐 — 버려야겠다" : "여기 냄새가 왜 이래"));
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {room.Name} 냄새({what})에 얼굴을 찌푸렸다", c.Id);
        if (spoiled && c.CanAct && c.Job?.Urgent != true) w.Cooking.Discard(room, c);
    }

    // ── 탄내 확인 ──

    /// <summary>확인하러 가는 사람을 적어 둔다 (불이 그 사람 덕에 먼저 드러나면 기록에 남긴다).</summary>
    public void NoteChecking(CrewMember c, Room room)
    {
        _checking[(room.Id, c.Id)] = _w.Tick;
        Stats.Checks++;
        LastCheck = (_w.Tick, c.Id, room.Id);
    }

    /// <summary>마지막으로 탄내를 확인하러 나선 때 · 사람 · 방 (기록 · 화면 — 1분이 안 걸리는 확인도 남는다).</summary>
    public (long Tick, int Crew, int Room) LastCheck { get; private set; } = (-1, -1, -1);

    /// <summary>주컴퓨터가 확인을 부탁했다 (화구 자리 비움) — 냄새를 못 맡았어도 간다.</summary>
    public void Ask(CrewMember c, Room room) => _asked[c.Id] = (room.Id, _w.Tick);

    public Room? AskedRoom(CrewMember c) =>
        _asked.TryGetValue(c.Id, out var a) && _w.Tick - a.tick < SimTime.Minutes(30) && a.room >= 0 && a.room < _w.Ship.Rooms.Count ? _w.Ship.Rooms[a.room] : null;

    /// <summary>탄내를 이미 확인했다 (더 진해지지 않으면 다시 가지 않는다).</summary>
    public bool Resolved(CrewMember c, float strength) =>
        _resolved.TryGetValue(c.Id, out var r) && _w.Tick - r.tick < SimTime.Minutes(40) && strength < r.strength * 1.5f;

    /// <summary>냄새의 끝에서 둘러본다: 불 위의 냄비 · 불 · 달아오른 이음 — 맡은 사람이 직접 본 것만 안다.</summary>
    public void Inspect(CrewMember c, Room room)
    {
        var w = _w;
        bool asked = _asked.Remove(c.Id);
        if (w.Cooking.ScorchingIn(room) is Furniture stove)
        {
            Stats.Found++;
            w.Cooking.TurnOff(stove, c);
            _resolved[c.Id] = (w.Tick, 1f);
            if (!asked) w.Cooking.Watch.Compare(room, c, "화구 위에 남은 냄비 (불 나기 전)");
            return;
        }
        if (w.Fire.CountIn(room) > 0)
        {
            Stats.Found++;
            c.Say(w, Persona.Say(c, "불이다!"));
            w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} 탄 냄새를 따라와 보니 {room.Name}에 불이 났다", c.Id);
            _resolved[c.Id] = (w.Tick, 1f);
            return;
        }
        var hot = w.Net.Links.FirstOrDefault(l => l.Room == room && l.Kind == NetKind.Power && l.Temp && !l.Cut && w.Flow.SpliceHeat(l) > 0.4f);
        if (hot != null)
        {
            Stats.Found++;
            c.Say(w, Persona.Say(c, "전선 타는 냄새다"));
            w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} 탄 냄새를 따라와 {room.Name} 임시로 이은 전선이 달아오른 걸 찾았다", c.Id);
            MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: 전선 타는 냄새");
            w.Board.RequestScan();
            _resolved[c.Id] = (w.Tick, 1f);
            return;
        }
        if (w.Portable.SmellFound(c, room)) { Stats.Found++; _resolved[c.Id] = (w.Tick, 1f); return; } // v16.7 이동식 장비 (먼지 타는 히터 · 뜨거운 콘센트)
        // 여기가 가장 진한데 아무것도 없다 — 어디서 흘러온 냄새다
        if (Source(c, SmellKind.Burnt) == room)
        {
            var s = Smelled(c, SmellKind.Burnt, SimTime.Hours(1));
            _resolved[c.Id] = (w.Tick, s?.Strength ?? 0.1f);
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 탄 냄새를 따라 {room.Name}까지 왔지만 어디서 나는지 못 찾았다", c.Id);
        }
    }

    /// <summary>Fire 훅: 불이 드러날 때 — 탄 냄새를 따라 먼저 와 있던 사람이 있으면 기록에 남긴다.</summary>
    public string FireFound(Room room, string how, CrewMember? witness)
    {
        // 발견한 사람이 냄새를 따라온 사람이면 그 사람 · 모르면 가장 먼저 나선 사람
        CrewMember? who = null;
        long first = long.MaxValue;
        foreach (var ((r, id), t) in _checking)
        {
            if (r != room.Id || CrewOfId(id) is not CrewMember x) continue;
            if (witness != null) { if (x == witness) { who = x; break; } continue; }
            if (t < first || (t == first && x.Id < (who?.Id ?? int.MaxValue))) { first = t; who = x; }
        }
        if (who == null) return how;
        Stats.FireBySmell++;
        _w.Cooking.Watch.Compare(room, who, "불");
        return how + $" · {Ko.IGa(who.Name)} 탄 냄새를 맡고 먼저 와 있었다";
    }

    private CrewMember? CrewOfId(int id) => id >= 0 && id < _w.Crew.Count ? _w.Crew[id] : null;

    // ── 모이기 ──

    public bool GatheredRecently(CrewMember c) => _gathered.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.Hours(5);
    public void NoteGathered(CrewMember c) => _gathered[c.Id] = _w.Tick;

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var v in _lvl) F(v);
        I(_asked.Count);
        I(Stats.Sniffs); I(Stats.Gathered); I(Stats.Checks); I(Stats.Found); I(Stats.Complaints);
    }
}

// ─────────────────────────────── 행동 ───────────────────────────────

/// <summary>빵 굽는 냄새 · 요리 냄새를 따라 주방으로 — 배고픈 사람 · 한가한 사람이 모인다.</summary>
public sealed class FollowSmellActivity : Activity
{
    public override string Id => "followsmell";
    public override string Label => "냄새를 따라";

    private static readonly SmellKind[] Follow = { SmellKind.Bread, SmellKind.Cooking, SmellKind.Coffee };

    private static (SmellKind kind, Room target)? Pick(CrewMember c, World w)
    {
        if (!c.IsAwake || c.Down || c.IsChild || c.Outside || c.Job?.Urgent == true || w.Smells.GatheredRecently(c)) return null;
        foreach (var k in Follow)
        {
            if (k == SmellKind.Coffee && c.Needs.Fatigue < 0.35f && c.Traits.Sociability < 0.6f) continue; // 커피 냄새는 졸린 사람 · 어울리길 좋아하는 사람이
            if (w.Smells.Smelled(c, k, SimTime.Minutes(20)) is null) continue;
            var t = w.Smells.Likely(c, k, 4);
            if (t == null) continue;
            // 냄새의 끝 (지금 방이 가장 진하면): 이미 와 있다
            if (t == c.Room) { if (k != SmellKind.Coffee && c.Room.Type is RoomType.Galley or RoomType.Mess) return null; if (k == SmellKind.Coffee) continue; }
            return (k, t);
        }
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (Pick(c, w) is not var (k, _)) return (0f, "—");
        float s = k == SmellKind.Coffee ? 0.2f + 0.45f * c.Needs.Fatigue + 0.1f * c.Traits.Sociability
            : 0.22f + 0.5f * c.Needs.Hunger + (k == SmellKind.Bread ? 0.18f : 0f) + 0.08f * c.Traits.Sociability;
        if (OnShift(c, w)) s -= 0.12f;
        return (s, k == SmellKind.Bread ? "빵 굽는 냄새" : k == SmellKind.Coffee ? "커피 냄새" : "맛있는 냄새");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Pick(c, w) is not var (k, target)) return null;
        // 냄새가 이끄는 방 — 이웃이 주방 · 식당이면 거기까지
        var heat = target.Furniture.FirstOrDefault(f => f.Type is FurnitureType.Stove or FurnitureType.Oven);
        var face = heat?.Center ?? new System.Numerics.Vector2(target.Cells[target.Cells.Count / 2].X + 0.5f, target.Cells[target.Cells.Count / 2].Y + 0.5f);
        Cell? at = null;
        foreach (var cell in target.Cells)
            if (w.Ship.IsWalkable(cell) && dist.Reachable(cell) && !w.IsSpotTaken(cell, c)
                && (at == null || (new System.Numerics.Vector2(cell.X + 0.5f, cell.Y + 0.5f) - face).LengthSquared() < (new System.Numerics.Vector2(at.Value.X + 0.5f, at.Value.Y + 0.5f) - face).LengthSquared()))
                at = cell;
        if (at is not Cell spot) return null;
        w.Smells.NoteGathered(c);
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Smells.Stats.Gathered++;
            if (k == SmellKind.Coffee) world.Smells.Stats.CoffeeGathered++;
            world.Log.Add(world.Tick, LogKind.Life, $"{Ko.IGa(cm.Name)} {SmellSystem.Name(k)}를 따라 {Ko.EuRo(target.Name)} 왔다", cm.Id);
            return true;
        }));
        toils.Add(new WaitToil(SimTime.Minutes(12), Pose.Standing, face, minTicks: SimTime.Minutes(4))
        {
            EveryTick = (cm, _) => cm.Needs.Social = MathF.Min(1f, cm.Needs.Social + 0.15f / SimTime.Minutes(12)),
        });
        toils.Add(new DoToil((cm, world) =>
        {
            // 갓 나온 것이 있으면 한 조각 맛본다
            if (cm.Room != null && world.Cooking.FreshIn(cm.Room, k) is Batch b)
            {
                cm.Needs.Food = MathF.Min(1f, cm.Needs.Food + 0.06f);
                cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.02f);
                if (world.Crew.FirstOrDefault(x => x.Id == b.Cook) is CrewMember cook && cook != cm && !cook.Dead && cook.Room == cm.Room)
                {
                    if (cook.AffinityTo(cm) < -0.2f)
                    {
                        // 사이가 나쁘면 맛보기도 없다 — 더 멀어진다
                        cm.Needs.Food = MathF.Max(0f, cm.Needs.Food - 0.06f);
                        cm.ChangeAffinity(cook, -0.02f);
                        cook.Say(world, Persona.Say(cook, "끼니 때 와"));
                        world.Smells.Stats.Refused++;
                        world.Log.Add(world.Tick, LogKind.Life, $"{Ko.IGa(cook.Name)} 갓 만든 {Ko.EulReul(b.Spec.Name)} {cm.Name}에게는 맛보이지 않았다", cm.Id);
                    }
                    else
                    {
                        // 갓 구운 걸 한 조각 나눠 준다 — 사이가 조금 가까워진다
                        cook.Needs.Social = MathF.Min(1f, cook.Needs.Social + 0.05f);
                        cm.ChangeAffinity(cook, 0.02f);
                        cook.ChangeAffinity(cm, 0.01f);
                        world.Cooking.Stats.Shared++;
                    }
                }
            }
            else if (k == SmellKind.Coffee) cm.Needs.Social = MathF.Min(1f, cm.Needs.Social + 0.05f); // 커피 냄새 곁에서 한마디씩
            return true;
        }));
        return new Job(this, k == SmellKind.Bread ? "빵 냄새를 따라" : k == SmellKind.Coffee ? "커피 냄새를 따라" : "냄새를 따라", toils) { LogText = $"{SmellSystem.Name(k)}를 따라 {Ko.EuRo(target.Name)}", TargetRoom = target };
    }
}

/// <summary>탄 냄새를 맡으면 냄새를 따라 확인하러 간다 — 불이 났다는 경보가 없어도 (경보가 울리기 전에).</summary>
public sealed class CheckSmellActivity : Activity
{
    public override string Id => "checksmell";
    public override string Label => "탄 냄새 확인";

    private static (Room target, float strength)? Pick(CrewMember c, World w)
    {
        if (!c.IsAwake || c.Down || c.IsChild || c.Outside) return null;
        if (w.Smells.AskedRoom(c) is Room asked && w.Cooking.ScorchingIn(asked) != null) return (asked, 0.6f); // 주컴퓨터가 부탁했다
        if (w.Smells.Smelled(c, SmellKind.Burnt, SimTime.Minutes(20)) is not SmellSystem.Sniff s) return null;
        if (w.Smells.Resolved(c, s.Strength)) return null;
        var t = w.Smells.Likely(c, SmellKind.Burnt); // 문간마다 맡아 보며 더 진한 쪽으로 (갈피를 못 잡으면 주방부터)
        if (t == null || w.Fire.IsKnown(t) || t.OffLimits || Atmosphere.Danger(t) > 0.6f) return null; // 이미 알려진 불은 대응하는 일로
        return (t, s.Strength);
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (Pick(c, w) is not var (t, s)) return (0f, "—");
        return (0.9f + 0.4f * MathF.Min(1f, s) + 0.1f * c.Traits.Bravery, $"탄 냄새 — {t.Name} 쪽");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Pick(c, w) is not var (target, _)) return null;
        Cell? spot = null;
        if (w.Cooking.ScorchingIn(target) is Furniture st) spot = SetAsidePlateActivity.Spot(st, w, dist);
        if (spot == null)
            foreach (var cell in target.Cells)
                if (w.Ship.IsWalkable(cell) && dist.Reachable(cell) && w.Fire.At(cell) <= 0f && (spot == null || dist.Get(cell) < dist.Get(spot.Value))) spot = cell;
        if (spot is not Cell at) return null;
        w.Smells.NoteChecking(c, target);
        var toils = new List<Toil>
        {
            new GotoToil(at),
            new WaitToil(SimTime.Minutes(0.5f), Pose.Standing),
            new DoToil((cm, world) => { world.Smells.Inspect(cm, target); return true; }),
        };
        return new Job(this, "탄 냄새 확인", toils) { LogText = $"탄 냄새를 따라 {Ko.EuRo(target.Name)} 간다", TargetRoom = target, InterruptMargin = 0.2f };
    }
}
