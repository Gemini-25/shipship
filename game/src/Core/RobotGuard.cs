using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v22 연출 1: 로봇이 몸으로 막아 준다.
//   폭발 압력 · 화구 · 증기 · 파편 · 불이 옮겨붙는 순간 — 사람 곁(같은 방 · 1.8칸 안)에 있던 로봇이 뛰어들어 그 사이에 선다.
//   사람은 피해를 덜 받고 (무거운 몸일수록 많이 막는다) · 로봇은 그 몫을 부위로 받는다 (몸통 · 맞은 쪽 팔 · 불이면 센서) — 막다 부서질 수도 있다.
//   남는 것: 로봇 이력 · 지켜 준 사람 · 사람의 기억 · 일기 · 연대기 · 그 로봇과의 정. 화면은 그 순간을 잠깐 느리게 · 가까이 잡는다.

public sealed partial class Robot
{
    /// <summary>v22 몸으로 막아 준 횟수.</summary>
    public int Shielded { get; internal set; }
    /// <summary>v22 지켜 준 사람 (이름 · 처음 지킨 순서).</summary>
    public List<string> Guarded { get; internal set; } = new();
}

/// <summary>화면용: 막아 선 순간 (로봇 · 사람 · 어디서 날아왔나 · 막다 부서졌나).</summary>
public readonly record struct GuardMoment(long Tick, int Robot, int Crew, Vector2 At, Vector2 From, bool Wrecked, string Text);

public sealed class GuardStats
{
    public int Shields, Wrecked, Missed, Late;
    public float Absorbed;
    public string Line() => $"몸으로 막음 {Shields} (막다 부서짐 {Wrecked}) · 대신 받은 피해 {Absorbed:0.00} · 곁에 로봇이 없었다 {Missed} · 늦었다 {Late}";
}

public sealed partial class RobotSystem
{
    /// <summary>시험: 로봇이 몸으로 막아 준다 (끄면 예전처럼).</summary>
    public static bool Guarding = true;
    /// <summary>점검 · 화면 시험: 막아 서기를 반드시 해낸다 (반사 판정을 건너뛴다).</summary>
    public static bool SureReflex;

    /// <summary>점검 · 화면 시험: 로봇을 그 자리에 움직이는 중으로 세운다.</summary>
    public void Stand(Robot r, Vector2 at)
    {
        if (r.Wrecked) return;
        DropTask(r);
        SetState(r, RobotState.Active);
        r.Position = at;
        r.PreviousPosition = at;
        r.Room = _world.Ship.RoomAt(Cell.FromPosition(at));
    }
    public GuardStats Guard { get; } = new();
    [NotSaved] private readonly List<GuardMoment> _moments = new();
    /// <summary>화면용 최근 순간 (저장하지 않는다 · 30개까지).</summary>
    public IReadOnlyList<GuardMoment> Moments => _moments;
    [NotSaved] private int _momentSerial;
    /// <summary>지금까지 생긴 순간 수 (화면이 새 순간을 알아본다).</summary>
    public int MomentSerial => _momentSerial;

    /// <summary>화면용 순간 하나 (막아 섬 · 부서짐) — 같은 틱 같은 로봇이면 나중 것으로 바꾼다.</summary>
    internal void Moment(Robot r, CrewMember? c, Vector2 from, bool wrecked, string text)
    {
        var m = new GuardMoment(_world.Tick, r.Id, c?.Id ?? -1, r.Position, from, wrecked, text);
        if (_moments.Count > 0 && _moments[^1].Tick == m.Tick && _moments[^1].Robot == r.Id) { _moments[^1] = m with { Crew = m.Crew >= 0 ? m.Crew : _moments[^1].Crew }; return; }
        _moments.Add(m);
        _momentSerial++;
        if (_moments.Count > 30) _moments.RemoveAt(0);
    }
    private readonly Dictionary<int, int> _coverNow = new(); // 이번 틱: 사람 → 막아 선 로봇 (-1 아무도 못 막음)
    private readonly HashSet<long> _shieldNow = new();        // 이번 틱에 이미 적은 (로봇 · 사람)
    private long _coverTick = -1;

    /// <summary>몸무게 · 덩치: 얼마나 막아 주나 (0~1).</summary>
    public static float Bulk(RobotKind k) => k switch
    {
        RobotKind.Lifter or RobotKind.Hauler or RobotKind.Stocker or RobotKind.Tanker or RobotKind.Sealer or RobotKind.Rescuer => 0.85f,
        RobotKind.Nurse or RobotKind.Medic => 0.65f,
        RobotKind.Stretcher => 0.7f,
        RobotKind.Courier or RobotKind.Utility => 0.6f,
        _ => RobotsV15.Base(k) switch { RobotKind.Safety => 0.8f, RobotKind.Gardener => 0.7f, RobotKind.Hauler => 0.85f, _ => 0.75f },
    };

    /// <summary>얼마나 빨리 뛰어드나 (0~1): 구조 · 방재 로봇이 가장 빠르다.</summary>
    public static float Reflex(RobotKind k) => k switch
    {
        RobotKind.Rescuer or RobotKind.Lifter or RobotKind.Sentry or RobotKind.Firefighter => 0.95f,
        RobotKind.Nurse or RobotKind.Medic or RobotKind.Stretcher => 0.8f,
        _ => RobotsV15.Base(k) switch { RobotKind.Safety => 0.95f, RobotKind.Hauler => 0.75f, _ => 0.85f },
    };

    private static bool CanCover(Robot r, CrewMember c)
    {
        if (r.Wrecked || r.Disabled || r.Aboard != null || r.Parts[(int)BotPart.Frame] <= 0f) return false;
        if (r.State is not (RobotState.Active or RobotState.Docked)) return false;
        float d = Vector2.Distance(r.Position, c.Position);
        return d <= (r.State == RobotState.Docked ? 1.1f : 1.8f) && (r.Room == c.Room || d < 1.1f);
    }

    /// <summary>
    /// 사람에게 닿으려는 피해(dmg)를 곁의 로봇이 몸으로 막는다. 사람이 받을 몫(0~1)을 돌려준다 (막지 못하면 1).
    /// from: 피해가 날아온 곳 (없으면 사람 앞을 가린다) · burn: 불 · 열이면 둘러 감겨 덜 막고 센서가 그을린다.
    /// </summary>
    public float Cover(CrewMember c, Vector2? from, float dmg, string cause, bool burn = false)
    {
        var w = _world;
        if (!Guarding || c.Dead || c.Outside || c.Room == null || dmg < 0.04f || Robots.Count == 0) return 1f;
        if (_coverTick != w.Tick) { _coverTick = w.Tick; _coverNow.Clear(); _shieldNow.Clear(); }
        Robot? r = null;
        bool fresh = false;
        if (_coverNow.TryGetValue(c.Id, out int rid))
        {
            if (rid < 0) return 1f;
            r = Robots.FirstOrDefault(x => x.Id == rid);
            if (r == null || !CanCover(r, c)) return 1f; // 앞의 한 방에 부서졌으면 이번엔 못 막는다
        }
        else
        {
            // 사이에 선 로봇부터 · 가까운 로봇부터
            Vector2 toward = from is Vector2 f && Vector2.DistanceSquared(f, c.Position) > 0.0001f ? Vector2.Normalize(f - c.Position) : Vector2.Zero;
            float best = float.MaxValue;
            foreach (var x in Robots)
            {
                if (!CanCover(x, c)) continue;
                var rel = x.Position - c.Position;
                float d = rel.Length();
                float score = d - (toward != Vector2.Zero && d > 0.001f ? 0.6f * Vector2.Dot(rel / d, toward) : 0f);
                if (score < best) { best = score; r = x; }
            }
            if (r == null) { _coverNow[c.Id] = -1; Guard.Missed++; return 1f; }
            float dist = Vector2.Distance(r.Position, c.Position);
            float chance = Reflex(r.Kind) * (r.State == RobotState.Active ? 1f : 0.75f) * (1f - 0.35f * dist / 1.8f) * (r.Fault != null ? 0.6f : 1f) * MathF.Min(1f, RobotBody.SpeedMul(r) + 0.2f);
            if (!SureReflex && !RB.Chance(chance)) { _coverNow[c.Id] = -1; Guard.Late++; return 1f; }
            _coverNow[c.Id] = r.Id;
            fresh = true;
        }
        float block = Bulk(r.Kind) * (r.Parts[(int)BotPart.Frame] >= 0.25f ? 1f : 0.65f) * (burn ? 0.85f : 1f);
        float took = dmg * block;
        Guard.Absorbed += took;
        // 사이에 선다 (움직이던 로봇만 — 정비대 위 로봇은 그 자리에서 몸을 튼다)
        var src = from ?? r.Position;
        if (fresh && r.State == RobotState.Active)
        {
            var dir = Vector2.DistanceSquared(src, c.Position) > 0.0001f ? Vector2.Normalize(src - c.Position) : Vector2.UnitX;
            var spot = c.Position + dir * 0.55f;
            var cell = Cell.FromPosition(spot);
            if (w.Ship.IsWalkable(cell) && w.Ship.RoomAt(cell) == c.Room) { r.Position = spot; r.PreviousPosition = spot; r.Room = c.Room; }
        }
        if (Vector2.DistanceSquared(src, r.Position) > 0.0001f) r.Facing = Vector2.Normalize(src - r.Position);
        // 로봇이 그 몫을 부위로 받는다
        string why = $"{Ko.EulReul(c.Name)} 막아서다 ({cause})";
        var side = RB.Chance(0.5f) ? BotPart.ArmL : BotPart.ArmR;
        Hurt(r, BotPart.Frame, took * 0.8f, why); // 금속 몸은 사람보다 단단하다 — 그래도 여러 번 막으면 부서진다
        if (!r.Wrecked) Hurt(r, side, took * 0.6f, why);
        if (burn && !r.Wrecked) Hurt(r, BotPart.Sensor, took * 0.7f, why);
        bool wrecked = r.Wrecked;
        if (fresh || wrecked) Shield(r, c, cause, src, wrecked);
        return 1f - block;
    }

    /// <summary>막아 선 일을 남긴다: 로봇 이력 · 지켜 준 사람 · 사람의 기억 · 일기 · 연대기 · 정.</summary>
    private void Shield(Robot r, CrewMember c, string cause, Vector2 from, bool wrecked)
    {
        var w = _world;
        if (_shieldNow.Add(r.Id * 100000L + c.Id))
        {
            r.Shielded++;
            Guard.Shields++;
            if (!r.Guarded.Contains(c.Name)) r.Guarded.Add(c.Name);
            MarkLog.Add(r.Marks, w.Tick, $"{Ko.EulReul(c.Name)} 몸으로 막았다 ({cause})");
            MarkLog.Add(c.Memory.Marks, w.Tick, $"{Ko.IGa(r.Name)} 몸으로 막아 줬다 ({cause})");
            w.Fleet.Bond(r, c, 3f); // 지켜 준 로봇에게는 정이 든다 (부서지면 이 사람이 가장 아쉬워한다)
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.03f);
        }
        else if (!wrecked) return; // 이번 틱엔 한 번만 적는다 (막다 부서지면 그것은 따로 남긴다)
        string text = wrecked
            ? $"{Ko.IGa(r.Name)} {Ko.EulReul(c.Name)} 몸으로 막다 부서졌다 ({cause})"
            : $"{Ko.IGa(r.Name)} {Ko.EulReul(c.Name)} 몸으로 막았다 ({cause})";
        if (wrecked)
        {
            Guard.Wrecked++;
            Life.Diary(w, c, Persona.Say(c, $"{Ko.IGa(r.Name)} 내 앞을 막아섰다. 그 자리에 나 대신 쓰러져 있었다"));
            Memory.Shake(w, c, 0.05f, $"{Ko.IGa(r.Name)} 나 대신 부서졌다");
            w.History.Add(w, HistoryKind.Response, text, r.Room, new[] { c }, Cell.FromPosition(r.Position));
        }
        else
        {
            Life.Diary(w, c, Persona.Say(c, $"{cause} — {Ko.IGa(r.Name)} 내 앞을 막아섰다. 아니었으면 크게 다쳤을 거다"));
            w.History.Add(w, HistoryKind.Response, text, r.Room, new[] { c }, Cell.FromPosition(r.Position));
        }
        w.Log.Add(w.Tick, wrecked ? LogKind.Warning : LogKind.Work, text, c.Id);
        r.Mind.Say(wrecked ? $"{Ko.EulReul(c.Name)} 막다 부서졌다" : $"{Ko.EulReul(c.Name)} 막았다 — 몸이 상했다", w.Tick);
        Moment(r, c, from, wrecked, text);
    }
}
