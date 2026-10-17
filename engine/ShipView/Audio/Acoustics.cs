using System.Collections.Concurrent;
using ShipEngine;

namespace ShipView.Audio;

/// <summary>이어 도는 소리의 종류 (섞개가 이 열쇠로 하나씩 둔다).</summary>
public enum LoopKind : byte { Jet, Fire, Machine, Room, Breath }

/// <summary>이어 도는 소리 하나의 이번 모습.</summary>
public struct LoopUpdate
{
    public long Key;
    public LoopKind Kind;
    public string Role;      // 기계: 장치 역할
    public float A, B;       // 쉬익: 띠 Hz · 세기 / 불: kW / 기계 · 방: 세기 / 숨: 빠르기 · 세기
    public float Gain, Cutoff, Pan, Send;
}

/// <summary>귀가 한 번 들은 것: 새로 울린 소리들 + 이어 도는 소리들의 지금 모습 + 방 울림.</summary>
public sealed class SoundFrame
{
    public readonly List<Voice> Shots = new();
    public readonly List<LoopUpdate> Loops = new();
    public float Rt60 = 0.5f, Wet = 0.25f;
    public bool Reset;       // 세계가 바뀌었다 (다시보기 · 다른 배) — 이어 도는 소리를 모두 끈다
    public string Mood = ""; // 연출의 분위기 (바탕 음악이 따른다)
}

/// <summary>
/// 소리 엔진의 귀 (docs/ENGINE.md §새 화면 소리) — 세계를 읽기만 한다.
/// <para><b>퍼짐</b>: 듣는 칸에서 다익스트라로 칸마다 '길에서 준 크기'를 잰다 — 칸마다 흡수 0.6dB · 벽 45dB(구멍이면 15 · 뚫렸으면 0) ·
/// 닫힌 문 25dB(조금 열렸으면 8) · 엷은 공기(−10·log 짙기) · 진공은 건너지 못한다. 벽 · 문에서 준 몫은 '가림'으로 따로 모아
/// 높은 음을 깎는다 (질량 법칙: 가림 9dB마다 자르는 높이가 반으로 — 벽 너머는 먹먹하게). 엷은 공기도 높은 음부터 줄인다.
/// 거리는 구면 확산 20·log d.</para>
/// <para><b>만들기</b>: 엔진이 낸 소리(쾅 · 쩍 · 끼익 · 경보 · 외침 · 비명 · 기계 고장)는 그 자리 재질로 · 새는 틈은 압력 차와 넓이로(분출 속도 → 띠) ·
/// 불은 열 방출량으로 · 돌아가는 장치는 역할로 · 걸음은 바닥 재질로 · 문은 움직일 때 · 방 공기는 짙기로.</para>
/// <para><b>귀</b>: 카메라 가운데 칸 (고른 사람이 있으면 그 사람 — 우주복이면 바깥은 먹먹하고 내 숨소리가 들린다).
/// 방 울림은 듣는 방의 크기 · 벽 재질로 (사빈: RT60 = 0.161·V / (S·α)).</para>
/// </summary>
public sealed class Acoustics
{
    public const float CellM = 1f, Height = 2.5f, Audible = 22f; // 이보다 작으면 (dB) 내지 않는다
    public const int MaxShots = 8, MaxMachines = 6, MaxJets = 6, MaxFires = 3;

    private Sim? _world;
    private Layout L = null!;
    private Field F = null!;
    private float[] _extra = Array.Empty<float>(), _occl = Array.Empty<float>(), _thin = Array.Empty<float>(), _path = Array.Empty<float>();
    /// <summary>소리 빠르기 (m/s): 공기 · 뼈대(강철 · 알루미늄 판의 세로파 약 5,000).</summary>
    public const float AirMs = 343, StructMs = 5000;
    private int[] _seen = Array.Empty<int>();
    private float[] _sExtra = Array.Empty<float>();             // 구조 길: 벽 · 바닥 판을 타고 온 소리가 귀까지 준 크기
    private int[] _sSeen = Array.Empty<int>();
    public const float Couple = 15, StructWall = 1.0f, StructDeck = 1.6f, Contact = 10;
    private int _stamp, _ear = -1;
    private double _sinceMap = 99;
    private long _cursor;
    private float[] _doorWas = Array.Empty<float>();
    private readonly Dictionary<int, double> _stepPhase = new();
    private readonly Dictionary<int, long> _sealHiss = new();   // 장치 봉함이 새는 쉬익 (칸 → 마지막 들린 틱)
    public readonly ConcurrentQueue<SoundFrame> Out = new();
    public bool Enabled = true;
    /// <summary>귀를 이 칸에 둔다 (시험 · 실험실 — -1이면 카메라 · 고른 사람).</summary>
    public int ForceEar = -1;
    /// <summary>확인용: 들은 소리마다 한 줄 (실험실 --listen).</summary>
    public Action<string>? Log;
    public int EarCell => _ear;
    public float EarThin { get; private set; } = 1;
    private uint _seed = 1;
    private int _logMachines;
    private readonly HashSet<long> _logJets = new();

    /// <summary>듣는 칸의 짙기 (1: 1기압 · 0: 진공) · 지금 귀에서 그 칸까지 준 크기 (dB · 거리 빼고) · 가림 (dB).</summary>
    public (float extra, float occl, float thin) PathTo(int cell) =>
        (uint)cell < (uint)_seen.Length && _seen[cell] == _stamp ? (_extra[cell], _occl[cell], _thin[cell]) : (999f, 999f, 0f);

    /// <summary>귀를 지금 자리에서 다시 잰다 (시험 — 문 · 공기가 바뀐 뒤 바로).</summary>
    public void Remap(int ear) { Map(ear); _ear = ear; _sinceMap = 0; }

    /// <summary>화면이 한 장 나아간 뒤: 귀를 옮기고 · 들은 것을 소리 목록으로 (작업 스레드).</summary>
    public void Update(LiveShow show, double realSec)
    {
        if (!Enabled) return;
        var w = show.World;
        var frame = new SoundFrame { Mood = show.View.Mood };
        if (!ReferenceEquals(w, _world))
        {
            _world = w; L = w.Layout; F = w.Get<Field>();
            int n = L.N;
            _extra = new float[n]; _occl = new float[n]; _thin = new float[n]; _seen = new int[n]; _path = new float[n];
            _sExtra = new float[n]; _sSeen = new int[n];
            _doorWas = (float[])F.DoorOpen.Clone();
            _cursor = w.Find<Sound>()?.Logged ?? 0;
            _stepPhase.Clear(); _sealHiss.Clear();
            frame.Reset = true; _ear = -1;
        }
        // ── 귀 ──
        int ear = EarCellFor(show, out var person);
        _sinceMap += realSec;
        if (ear != _ear || _sinceMap > 0.2) { Map(ear); _ear = ear; _sinceMap = 0; }
        EarThin = ear >= 0 ? Thin(ear) : 0;
        bool suit = false; float breath = 0;
        if (person >= 0)
        {
            var life = w.Find<Life>(); var mind = w.Find<Mind>();
            if (life != null && mind != null)
            {
                var b = life.Bodies[mind.Agents[person].C.Body];
                suit = b.Suit != 0 && b.Alive != 0;
                breath = b.Alive == 0 ? 0 : 0.22f + Math.Clamp((b.ActivityW - 90) / 600f, 0, 0.8f);
            }
        }
        float halfW = person >= 0 ? 10f : (float)Math.Max(6, show.Camera().w / 2);
        float speed = (float)show.Speed;
        _screenPerGame = 1f / (Math.Max(0.05f, speed) * (float)Time.SecondsPerTick); // 게임 1초가 화면 몇 초인가 (늦게 닿는 소리)

        var sndAmb = w.Find<Sound>();
        float ambient = ear >= 0 && sndAmb != null ? sndAmb.Ambient(ear) : 0;
        // ── 방 울림: 듣는 방의 크기 · 벽 재질 ──
        int room = ear >= 0 ? L.Room[ear] : -1;
        if (room >= 0)
        {
            int n = L.Rooms[room].Cells.Length;
            float V = n * CellM * CellM * Height, S = 2 * n * CellM * CellM + 4 * MathF.Sqrt(n) * CellM * Height;
            float alpha = Acoustic.Of(MaterialAt(WallNear(L.Rooms[room].Cells[0]))).Absorb + 0.12f; // 가구 · 사람이 먹는 몫
            frame.Rt60 = Math.Clamp(0.161f * V / (S * alpha), 0.15f, 3.5f) * Math.Clamp(EarThin, 0.2f, 1f);
            frame.Wet = (0.18f + 0.06f * MathF.Log10(n)) * Math.Clamp(EarThin, 0, 1);
        }
        else { frame.Rt60 = 0.3f; frame.Wet = 0.1f; }

        // ── 엔진이 낸 소리 ──
        var snd = w.Find<Sound>();
        var shots = new List<(float lp, Voice v)>();
        if (snd != null)
        {
            if (_cursor > snd.Logged) _cursor = snd.Logged;
            foreach (var e in snd.LogSince(_cursor))
            {
                if (e.Kind == SoundKind.Hiss) { if (e.Actor < 0 && e.Cause != 0 && L.Kind[e.Cell] != CellKind.Wall) _sealHiss[e.Cell] = e.Tick; continue; }
                int structAt = e.Kind is SoundKind.Bang or SoundKind.Crack or SoundKind.Creak or SoundKind.Machine or SoundKind.Thud ? WallNear(e.Cell) : -1;
                if (!Hear(e.Cell, e.Db, ear, halfW, suit, out float lp, out float gain, out float cut, out float pan, structAt))
                {
                    Log?.Invoke($"{Time.Clock(e.Tick)} {e.Kind} {e.Db:0}dB ({L.X(e.Cell)},{L.Y(e.Cell)}) — 못 들음 (귀 ({(ear >= 0 ? L.X(ear) : -1)},{(ear >= 0 ? L.Y(ear) : -1)}) · 공기 길 {(_seen[e.Cell] == _stamp ? _extra[e.Cell] : -1):0} · 구조 길 {(structAt >= 0 && _sSeen[structAt] == _stamp ? _sExtra[structAt] : -1):0} · 들은 크기 {lp:0})");
                    continue;
                }
                var v = MakeShot(e, w);
                if (v == null) continue;
                v.Gain = gain; v.Cutoff = cut; v.Pan = pan; v.Delay = DelayOf(e.Cell, structAt);
                shots.Add((lp, v));
                Log?.Invoke($"{Time.Clock(e.Tick)} {e.Kind} {e.Db:0}dB → 귀 {lp:0}dB · 거름 {cut:0}Hz · 좌우 {pan:0.0}");
            }
            _cursor = snd.Logged;
        }

        // ── 걸음 (화면에서 천천히 흐를 때만 — 빨리 감기에선 발소리가 기관총이 된다) ──
        var mind2 = w.Find<Mind>();
        if (mind2 != null && speed <= 8.5f)
        {
            foreach (var a in mind2.Agents)
            {
                var (nx, ny) = show.Nudge(a);
                bool walking = nx != 0 || ny != 0;
                if (!walking) { _stepPhase.Remove(a.Id); continue; }
                double ph = _stepPhase.GetValueOrDefault(a.Id, 0.99) + realSec * (a.IsPerson ? 1.9 : 2.6) * Math.Max(0.4, speed / 2.0);
                if (ph >= 1)
                {
                    ph -= Math.Floor(ph);
                    int c = a.C.Cell;
                    if (Hear(c, a.IsPerson ? 58 : 62, ear, halfW, suit, out float lp, out float gain, out float cut, out float pan, c))
                    {
                        var floor = Acoustic.Of(MaterialAt(c));
                        if (!a.IsPerson) floor = floor with { Hard = 1f };
                        shots.Add((lp, new Step(floor, NextSeed()) { Gain = gain, Cutoff = cut, Pan = pan, Send = 0.15f }));
                    }
                }
                _stepPhase[a.Id] = ph;
            }
        }

        // ── 문: 움직이기 시작할 때 ──
        for (int d = 0; d < L.Doors.Count && d < _doorWas.Length; d++)
        {
            float now = F.DoorOpen[d], was = _doorWas[d];
            if (MathF.Abs(now - was) > 0.04f && Hear(L.Doors[d].Cell, 72, ear, halfW, suit, out float lp, out float gain, out float cut, out float pan))
                shots.Add((lp, new Door(NextSeed()) { Gain = gain, Cutoff = cut, Pan = pan }));
            _doorWas[d] = now;
        }

        foreach (var s in shots.OrderByDescending(x => x.lp).Take(MaxShots)) frame.Shots.Add(s.v);

        // ── 이어 도는 소리 ──
        Jets(frame, ear, halfW, suit, w.Tick);
        Fires(frame, w, ear, halfW, suit);
        Machines(frame, w, ear, halfW, suit);
        if (ear >= 0 && EarThin > 0.02f) // 방 공기: 엔진의 바탕 소음을 따라 (조용한 방 30dB · 기계실 70dB — 0.3배로 눌러 기계 소리와 겹치지 않게)
            frame.Loops.Add(new LoopUpdate { Key = 1, Kind = LoopKind.Room, A = 0.05f * Math.Min(1.2f, EarThin) * (suit ? 0.3f : 1f) * Dsp.Db(Math.Clamp(ambient - 35, -10, 30) * 0.3f), Gain = 1, Cutoff = suit ? 900 : 10000 });
        if (suit) frame.Loops.Add(new LoopUpdate { Key = 2, Kind = LoopKind.Breath, A = breath, B = 0.35f, Gain = 1, Cutoff = 10000 });
        Out.Enqueue(frame);
        while (Out.Count > 30 && Out.TryDequeue(out _)) { } // 아무도 안 들으면 쌓지 않는다
    }

    private uint NextSeed() => _seed = _seed * 1664525u + 1013904223u;

    private Voice? MakeShot(Emission e, Sim w)
    {
        uint seed = (uint)(e.Tick * 31 + e.Cell * 7 + (int)e.Kind);
        var mat = Acoustic.Of(MaterialAt(WallNear(e.Cell)));
        switch (e.Kind)
        {
            case SoundKind.Bang:
                float kJ = MathF.Pow(10, (e.Db - 85) / 10f);
                return new Hit(mat, kJ, seed) { Send = 0.35f };
            case SoundKind.Crack: return new Crack(mat, seed) { Send = 0.3f };
            case SoundKind.Creak: return new Creak(mat, Math.Clamp((e.Db - 50) / 30f, 0, 1), seed) { Send = 0.3f };
            case SoundKind.Alarm: return new Alarm(e.Actor < 0) { Send = 0.3f };
            case SoundKind.Voice:
            case SoundKind.Scream:
            {
                var mind = w.Find<Mind>();
                bool robot = mind != null && e.Actor >= 0 && e.Actor < mind.Agents.Count && !mind.Agents[e.Actor].IsPerson;
                float f0 = robot ? 150 : 95 + (uint)(e.Actor * 2654435761u) % 140; // 사람마다 목소리 높이
                return new Shout(f0, e.Kind == SoundKind.Scream, seed) { Send = 0.25f };
            }
            case SoundKind.Machine: return new Grind(seed) { Send = 0.25f };
            case SoundKind.Thud: return new Hit(Acoustic.Of("polymer"), 0.5f, seed);
            default: return null;
        }
    }

    // ── 새는 틈: 분출 속도 · 틈 크기 → 띠 · 세기 ──
    private void Jets(SoundFrame frame, int ear, float halfW, bool suit, long tick)
    {
        var list = new List<(float lp, LoopUpdate u)>();
        foreach (var k in F.Links)
        {
            if (k.Area <= 0) continue;
            double pa = F.ZoneKPa(k.RA), pb = k.RB >= 0 ? F.ZoneKPa(k.RB) : F.Outside.KPa; // 구역 기압 (엔진의 쉬익과 같게)
            double hi = Math.Max(pa, pb), lo = Math.Min(pa, pb);
            if (hi - lo < 0.5 || hi < 2) continue;
            int at = pa >= pb ? k.A : k.B;
            if (at < 0) continue;
            double rho = hi * 1000 * 0.029 / (8.314 * 293);
            double v = lo / hi < 0.528 ? 310 : Math.Sqrt(2 * (hi - lo) * 1000 / rho); // 막힘 흐름이면 소리 빠르기
            double d = Math.Sqrt(k.Area);
            float hz = (float)(0.2 * v / d);
            float db = Math.Max(30, Sound.JetDb(k.Area, hi, lo));  // 엔진의 쉬익과 같은 식 (라이트힐 — 속도의 8제곱 · 넓이)
            if (!Hear(at, db, ear, halfW, suit, out float lp, out float gain, out float cut, out float pan)) continue;
            long key = 1000 + (long)(k.Via >= 0 ? k.Via : at) * 4 + (k.B < 0 ? 1 : 0);
            list.Add((lp, new LoopUpdate { Key = key, Kind = LoopKind.Jet, A = hz, B = 1, Gain = gain, Cutoff = cut, Pan = pan, Send = 0.2f }));
        }
        // 장치 봉함이 새는 쉬익 (엔진이 낸 소리 — 6틱 넘게 안 들리면 그친다)
        foreach (var (cell, last) in _sealHiss.ToList())
        {
            if (tick - last > 6) { _sealHiss.Remove(cell); continue; }
            if (!Hear(cell, 70, ear, halfW, suit, out float lp, out float gain, out float cut, out float pan)) continue;
            list.Add((lp, new LoopUpdate { Key = 900_000_000L + cell, Kind = LoopKind.Jet, A = 3500, B = 1, Gain = gain, Cutoff = cut, Pan = pan, Send = 0.2f }));
        }
        foreach (var j in list.OrderByDescending(x => x.lp).Take(MaxJets)) { frame.Loops.Add(j.u); if (Log != null && _logJets.Add(j.u.Key)) Log($"  쉬익 시작 {j.u.A:0}Hz → 귀 {j.lp:0}dB · 거름 {j.u.Cutoff:0}Hz"); }
    }

    // ── 불: 방마다 열 방출량을 모아 ──
    private void Fires(SoundFrame frame, Sim w, int ear, float halfW, bool suit)
    {
        var chem = w.Find<Chemistry>();
        if (chem == null || chem.BurningCount == 0) return;
        var byRoom = new Dictionary<int, (double kw, int cell, float best)>();
        for (int c = 0; c < L.N; c++)
        {
            if (chem.Burning[c] == 0) continue;
            int r = L.Room[c];
            float kw = chem.Hrr[c];
            var cur = byRoom.GetValueOrDefault(r, (0, c, -1));
            byRoom[r] = (cur.kw + kw, kw > cur.best ? c : cur.cell, Math.Max(cur.best, kw));
        }
        var list = new List<(float lp, LoopUpdate u)>();
        foreach (var (r, f) in byRoom)
        {
            float db = (float)(60 + 10 * Math.Log10(Math.Max(1, f.kw)));
            if (!Hear(f.cell, db, ear, halfW, suit, out float lp, out float gain, out float cut, out float pan)) continue;
            list.Add((lp, new LoopUpdate { Key = 2_000_000 + r, Kind = LoopKind.Fire, A = (float)f.kw, Gain = gain, Cutoff = cut, Pan = pan, Send = 0.3f }));
        }
        foreach (var j in list.OrderByDescending(x => x.lp).Take(MaxFires)) frame.Loops.Add(j.u);
    }

    // ── 돌아가는 장치: 엔진의 장치 표 크기 + 낡은 몫 (바탕 소음과 같은 값) ──

    private void Machines(SoundFrame frame, Sim w, int ear, float halfW, bool suit)
    {
        var net = w.Find<Network>();
        if (net == null) return;
        var list = new List<(float lp, LoopUpdate u)>();
        foreach (var dev in net.Devices)
        {
            float db = net.NoiseDb(dev.Id);
            if (db <= 30) continue;
            if (!Hear(dev.Cell, db, ear, halfW, suit, out float lp, out float gain, out float cut, out float pan, dev.Cell)) continue;
            list.Add((lp, new LoopUpdate { Key = 3_000_000 + dev.Id, Kind = LoopKind.Machine, Role = dev.Def.Role, A = 1, B = net.WornDb(dev.Id), Gain = gain, Cutoff = cut, Pan = pan, Send = 0.15f }));
        }
        foreach (var j in list.OrderByDescending(x => x.lp).Take(MaxMachines)) frame.Loops.Add(j.u);
        if (Log != null && _logMachines++ % 600 == 0) Log("  기계: " + string.Join(" · ", list.OrderByDescending(x => x.lp).Take(MaxMachines).Select(x => $"{x.u.Role} {x.lp:0}dB")));
    }

    /// <summary>
    /// 소리 한 자리 → 귀에서의 크기 · 자르는 높이 · 좌우. 들리지 않으면 false.
    /// 크기: 1m에서의 dB − 20·log 거리 − 길에서 준 것 → 화면 소리로 (88dB가 0dB · 0.6배로 눌러 넓은 차이를 들을 만하게 — 귀의 크기 감각도 로그다).
    /// </summary>
    public bool Hear(int src, float db, int ear, float halfW, bool suit, out float lp, out float gain, out float cutoff, out float pan, int structAt = -1)
    {
        lp = -999; gain = 0; cutoff = 10000; pan = 0;
        if (ear < 0 || (uint)src >= (uint)_seen.Length) return false;
        float dx = (L.X(src) - L.X(ear)) * CellM, dy = (L.Y(src) - L.Y(ear)) * CellM;
        float dist = Math.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
        // 공기 길
        int s = _seen[src] == _stamp ? src : NearestHeard(src);
        float occl = 0, thin = 1;
        if (s >= 0) { lp = db - 20 * MathF.Log10(dist) - _extra[s]; occl = _occl[s]; thin = _thin[s]; }
        // 구조 길: 맞은 판 · 장치가 선 바닥에서 뼈대를 타고 (판에서는 원통으로 퍼진다 — 10·log 거리)
        if (structAt >= 0 && _sSeen[structAt] == _stamp)
        {
            float ls = db - 10 * MathF.Log10(dist) - _sExtra[structAt];
            if (ls > lp) { lp = ls; occl = 30 + _sExtra[structAt] * 0.3f; thin = 1; }
        }
        if (suit) lp -= 12;                                       // 우주복 투구 너머
        else if (EarThin < 1) lp += 20 * MathF.Log10(Math.Max(EarThin, 0.01f)); // 엷은 공기에선 고막도 덜 울린다
        if (lp < Audible) return false;
        gain = Dsp.Db((lp - 88) * 0.6f);
        cutoff = 9000 * MathF.Pow(2, -occl / 9f) * Math.Clamp(MathF.Pow(thin, 1.2f), 0.08f, 1f);
        if (suit) cutoff = Math.Min(cutoff, 1200);
        cutoff = Math.Clamp(cutoff, 120, 10000);
        pan = Math.Clamp(dx / (halfW * CellM), -1, 1) * 0.85f;
        return true;
    }

    private float _screenPerGame = 1;

    /// <summary>
    /// 늦게 닿는 소리 (표본): 더 크게 온 길이 공기면 길 따라 343m/s · 뼈대면 곧은 거리 / 5,000m/s — 게임 시간을 화면 시간으로 (빠르게 흐를 때는 그만큼 짧다 · 많아야 1.5초).
    /// </summary>
    public int DelayOf(int src, int structAt = -1)
    {
        if (_ear < 0 || (uint)src >= (uint)_seen.Length) return 0;
        float dx = L.X(src) - L.X(_ear), dy = L.Y(src) - L.Y(_ear), straight = MathF.Max(1, MathF.Sqrt(dx * dx + dy * dy) * CellM);
        int s = _seen[src] == _stamp ? src : NearestHeard(src);
        float airLoss = s >= 0 ? _extra[s] + 20 * MathF.Log10(straight) : 999;                                   // 공기 길: 길에서 준 것 + 구면 확산
        float structLoss = structAt >= 0 && _sSeen[structAt] == _stamp ? _sExtra[structAt] + 10 * MathF.Log10(straight) : 999; // 뼈대: 판의 원통 확산
        float sec = structLoss < airLoss ? straight / StructMs : (s >= 0 ? Math.Max(_path[s], straight) : straight) / AirMs;
        return (int)(Math.Min(1.5f, sec * _screenPerGame) * Dsp.Rate);
    }

    /// <summary>벽 칸 · 구멍 칸처럼 길이 안 닿은 칸은 곁의 닿은 칸으로.</summary>
    private int NearestHeard(int c)
    {
        int best = -1; float bv = float.MaxValue;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = L.X(c) + dx, y = L.Y(c) + dy;
                if (!L.In(x, y)) continue;
                int j = L.Index(x, y);
                if (_seen[j] == _stamp && _extra[j] + (dx != 0 && dy != 0 ? 0.5f : 0) < bv) { bv = _extra[j]; best = j; }
            }
        return best;
    }

    /// <summary>
    /// 귀 칸에서 다익스트라 — 두 겹: 공기 길(칸마다 흡수 · 벽 · 문 · 엷은 공기)과 구조 길(벽 · 바닥 판을 타고 칸마다 1 · 1.6dB).
    /// 공기 ↔ 판은 15dB (소리가 판을 흔들고 · 판이 공기를 흔든다). 귀가 진공에 있으면 발바닥으로만 (판에 닿아 10dB).
    /// 칸마다: 공기 길에서 준 크기 · 가림 · 가장 엷은 공기 / 구조 길에서 준 크기.
    /// </summary>
    private void Map(int ear)
    {
        _stamp++;
        if (ear < 0) return;
        var pq = new PriorityQueue<int, float>();                 // 상태 = 칸 × 2 + 겹 (0 공기 · 1 구조)
        bool earAir = Thin(ear) > 0;
        if (earAir) { _extra[ear] = 0; _occl[ear] = 0; _thin[ear] = Thin(ear); _seen[ear] = _stamp; _path[ear] = 0; pq.Enqueue(ear * 2, 0); }
        _sExtra[ear] = earAir ? Couple + 3 : Contact; _sSeen[ear] = _stamp; pq.Enqueue(ear * 2 + 1, _sExtra[ear]);
        Span<int> nb = stackalloc int[4];
        while (pq.TryDequeue(out int st, out float cost))
        {
            int c = st >> 1;
            bool structural = (st & 1) == 1;
            if (structural ? cost > _sExtra[c] + 1e-4f : cost > _extra[c] + 1e-4f) continue;
            L.Neighbors(c, nb);
            if (!structural)
            {
                foreach (int j in nb)
                {
                    if (j < 0 || L.Kind[j] == CellKind.Outside) continue;
                    float occl = 0, loss = 0.6f;
                    var k = L.Kind[j];
                    if (k == CellKind.Wall)
                    {
                        occl = F.Hole[j] >= 2 ? 0 : F.Hole[j] > 0 ? 15 : 45;
                        TryStruct(pq, j, _extra[c] + Couple);          // 공기가 벽을 흔든다
                    }
                    else if (k == CellKind.Door && !F.DoorPassable(L.DoorAt[j])) occl = F.DoorOpen[L.DoorAt[j]] > 0 ? 8 : 25;
                    float th = 1;
                    if (L.IsSpace(j))
                    {
                        th = Thin(j);
                        if (th <= 0) continue;                        // 진공은 소리를 건네지 못한다
                        loss += -10 * MathF.Log10(Math.Min(1f, th));
                    }
                    float next = _extra[c] + loss + occl;
                    if (next > 80) continue;
                    if (_seen[j] == _stamp && _extra[j] <= next) continue;
                    _seen[j] = _stamp; _extra[j] = next; _occl[j] = _occl[c] + occl; _thin[j] = Math.Min(_thin[c], th); _path[j] = _path[c] + CellM;
                    pq.Enqueue(j * 2, next);
                }
                TryStruct(pq, c, _extra[c] + Couple + 3);                // 바닥 판
            }
            else
            {
                foreach (int j in nb)
                {
                    if (j < 0 || L.Kind[j] == CellKind.Outside) continue;
                    TryStruct(pq, j, _sExtra[c] + (L.Kind[j] == CellKind.Wall ? StructWall : StructDeck));
                    // 판이 공기를 흔든다 (공기 칸으로)
                    if (L.IsSpace(j) && Thin(j) > 0)
                    {
                        float next = _sExtra[c] + Couple;
                        if (next <= 80 && !(_seen[j] == _stamp && _extra[j] <= next))
                        {
                            _seen[j] = _stamp; _extra[j] = next; _occl[j] = 45; _thin[j] = Thin(j); _path[j] = 0; // 구조로 온 소리는 먹먹하다 (뼈대는 빨라 길이를 0으로)
                            pq.Enqueue(j * 2, next);
                        }
                    }
                }
            }
        }
    }

    private void TryStruct(PriorityQueue<int, float> pq, int j, float next)
    {
        if (next > 95) return;
        if (_sSeen[j] == _stamp && _sExtra[j] <= next) return;
        _sSeen[j] = _stamp; _sExtra[j] = next;
        pq.Enqueue(j * 2 + 1, next);
    }

    private float Thin(int c)
    {
        if (L.Kind[c] == CellKind.Wall) return 1;
        if (!L.IsSpace(c)) return 0;
        double p = F.Pressure(c);
        return p < 1 ? 0 : (float)Math.Min(1.5, p / 101.3);
    }

    /// <summary>귀 칸: 고른 사람의 칸 · 아니면 카메라 가운데 (벽 · 바깥이면 가까운 바닥).</summary>
    private int EarCellFor(LiveShow show, out int person)
    {
        person = -1;
        if (ForceEar >= 0) return ForceEar;
        var mind = show.World.Find<Mind>();
        if (show.Selected >= 0 && mind != null && show.Selected < mind.Agents.Count)
        {
            var a = mind.Agents[show.Selected];
            if (a.IsPerson) person = a.Id;
            return a.C.Cell;
        }
        var (l, t, w, h) = show.Camera();
        int cx = (int)Math.Floor(l + w / 2), cy = (int)Math.Floor(t + h / 2);
        for (int r = 0; r <= 5; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = cx + dx, y = cy + dy;
                    if (!L.In(x, y)) continue;
                    int c = L.Index(x, y);
                    if (L.IsSpace(c)) return c;
                }
        return -1;
    }

    private int WallNear(int c)
    {
        if (L.Kind[c] == CellKind.Wall) return c;
        Span<int> nb = stackalloc int[4];
        L.Neighbors(c, nb);
        foreach (int j in nb) if (j >= 0 && L.Kind[j] == CellKind.Wall) return j;
        return c;
    }

    private string MaterialAt(int c) => _world!.Tables.Materials[L.Material[c]].Id;
}
