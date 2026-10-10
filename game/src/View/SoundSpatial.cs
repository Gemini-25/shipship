using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v11.3 자리가 있는 소리: 화면 가운데에서 먼 소리는 작게, 왼쪽·오른쪽은 그쪽 귀로.
///   운석 충돌은 맞은 자리에서 (화면 밖이어도 작게는 들린다) · 공기 새는 소리와 불 소리는 가장 가까운 곳에서.
///   문: 열릴 때 "칙" 하는 공압음, 닫힐 때 낮게 (보이는 문만, 한꺼번에 너무 많이 울리지 않게).
///   발소리: 가까이 볼 때(확대 0.7 이상 · 1배속) 걷는 사람의 걸음마다 — 화면 가운데에 가까운 넷까지. 로봇은 작은 구동음.
/// 거리는 화면 기준이다 (확대하면 가까운 것만 들린다). 시뮬레이션은 여전히 소리를 모른다.
/// </summary>
public partial class SoundSystem
{
    private readonly List<AudioStreamPlayer2D> _pool2D = new();
    private AudioStreamWav _door = null!, _step = null!, _servo = null!;
    private float[] _doorOpen = Array.Empty<float>();
    private readonly Dictionary<int, (System.Numerics.Vector2 last, float acc, bool left)> _walkers = new();
    private readonly Dictionary<int, (System.Numerics.Vector2 last, float acc)> _rovers = new();
    private int _doorBudget;
    private double _doorWindow;

    private void InitSpatial()
    {
        _door = Make(DoorSound());
        _step = Make(StepSound());
        _servo = Make(ServoSound());
        for (int i = 0; i < 10; i++)
        {
            // 거리 감쇠는 직접 한다 (화면 기준) — 여기서는 좌우만 맡긴다
            var p = new AudioStreamPlayer2D { MaxDistance = 1e7f, Attenuation = 0f, PanningStrength = 1f };
            AddChild(p);
            _pool2D.Add(p);
        }
    }

    /// <summary>배 위의 자리(칸 단위) → 화면 픽셀.</summary>
    private Vector2 ScreenOf(System.Numerics.Vector2 cellPos) =>
        GetViewport().GetCanvasTransform() * _main.ShipView.ToGlobal(ShipView.ToPx(cellPos));

    private Vector2 GlobalOf(System.Numerics.Vector2 cellPos) => _main.ShipView.ToGlobal(ShipView.ToPx(cellPos));

    /// <summary>화면 가운데에서의 거리로 정한 크기 (0~1). 화면 폭만큼 떨어지면 1/5쯤.</summary>
    private float Gain(System.Numerics.Vector2 cellPos)
    {
        var size = GetViewport().GetVisibleRect().Size;
        float d = (ScreenOf(cellPos) - size * 0.5f).Length() / Mathf.Max(1f, size.X);
        return 1f / (1f + 16f * d * d);
    }

    private bool OnScreen(System.Numerics.Vector2 cellPos, float margin = 40f)
    {
        var size = GetViewport().GetVisibleRect().Size;
        var s = ScreenOf(cellPos);
        return s.X > -margin && s.Y > -margin && s.X < size.X + margin && s.Y < size.Y + margin;
    }

    private void Play2D(AudioStreamWav stream, System.Numerics.Vector2 cellPos, float db, float gain, float pitch = 1f)
    {
        if (gain < 0.02f) return;
        var p = _pool2D.FirstOrDefault(x => !x.Playing) ?? _pool2D.OrderByDescending(x => x.GetPlaybackPosition()).First();
        p.Stream = stream;
        p.GlobalPosition = GlobalOf(cellPos);
        p.VolumeDb = db + Mathf.LinearToDb(gain);
        p.PitchScale = pitch;
        p.Play();
    }

    /// <summary>문·발소리·로봇 (매 프레임).</summary>
    private void ProcessSpatial(World w, double delta, bool quiet)
    {
        bool fx = Settings.Effects && !quiet && !_main.Paused;
        float zoom = _main.Camera.Zoom.X;

        // ── 문 ──
        var doors = w.Ship.Doors;
        if (_doorOpen.Length != doors.Count)
        {
            _doorOpen = doors.Select(d => d.Openness).ToArray();
            return;
        }
        _doorWindow += delta;
        if (_doorWindow > 0.6) { _doorWindow = 0; _doorBudget = 2; }
        for (int i = 0; i < doors.Count; i++)
        {
            var d = doors[i];
            float before = _doorOpen[i], now = d.Openness;
            _doorOpen[i] = now;
            if (!fx || d.Removed || zoom < 0.5f || _doorBudget <= 0) continue;
            bool opening = before < 0.1f && now >= 0.1f, closing = before > 0.9f && now <= 0.9f;
            if (!opening && !closing) continue;
            if (!OnScreen(d.Cell.Center) || Gain(d.Cell.Center) < 0.25f) continue; // 화면 가운데 쪽 문만
            _doorBudget--;
            Play2D(_door, d.Cell.Center, d.IsExternal ? -9f : -15f, Gain(d.Cell.Center), opening ? 1.05f : 0.82f);
        }

        // ── 발소리 (가까이서 1배속으로 볼 때만) ──
        bool close = fx && zoom >= 0.7f && _main.SpeedIndex == 0;
        var near = close
            ? w.Crew.Where(c => !c.Dead && !c.Down && !c.Outside && c.CarriedBy == null && OnScreen(c.Position, 0f))
                .OrderBy(c => (ScreenOf(c.Position) - GetViewport().GetVisibleRect().Size * 0.5f).LengthSquared()).Take(4).Select(c => c.Id).ToHashSet()
            : new HashSet<int>();
        foreach (var c in w.Crew)
        {
            var pos = c.Position;
            if (!_walkers.TryGetValue(c.Id, out var st)) { _walkers[c.Id] = (pos, 0f, false); continue; }
            float moved = (pos - st.last).Length();
            st.last = pos;
            if (moved > 3f) { st.acc = 0f; _walkers[c.Id] = st; continue; } // 순간 이동(불러오기·되감기)
            st.acc += moved;
            if (st.acc >= 0.72f)
            {
                st.acc -= 0.72f;
                st.left = !st.left;
                // 우주복을 입으면 무겁게, 진공에선 발소리가 없다
                bool vacuum = c.Room == null || c.Room.Air.Pressure < 20f;
                if (near.Contains(c.Id) && !vacuum)
                    Play2D(_step, pos, c.Suit != null ? -17f : -21f, Gain(pos), (st.left ? 0.94f : 1.06f) * (c.Suit != null ? 0.8f : 1f));
            }
            _walkers[c.Id] = st;
        }
        foreach (var r in w.Robots.Robots)
        {
            var pos = r.Position;
            if (!_rovers.TryGetValue(r.Id, out var st)) { _rovers[r.Id] = (pos, 0f); continue; }
            float moved = (pos - st.last).Length();
            st.last = pos;
            if (moved > 3f) { _rovers[r.Id] = (pos, 0f); continue; }
            st.acc += moved;
            if (st.acc >= 1.1f)
            {
                st.acc = 0f;
                if (close && OnScreen(pos, 0f)) Play2D(_servo, pos, -24f, Gain(pos), 1f + 0.1f * (r.Id % 3));
            }
            _rovers[r.Id] = st;
        }
    }

    /// <summary>계속 나는 소리의 자리: 가장 가까운 새는 방 · 불.</summary>
    private void PlaceLoops(World w)
    {
        var center = GetViewport().GetVisibleRect().Size * 0.5f;
        var leak = w.Ship.LiveRooms.Where(r => r.Leaking).OrderBy(r => (ScreenOf(r.Center) - center).LengthSquared()).FirstOrDefault();
        if (leak != null) _hiss.GlobalPosition = GlobalOf(leak.Center);
        var fire = w.Fire.Fires.Keys.OrderBy(c => (ScreenOf(c.Center) - center).LengthSquared()).Cast<Cell?>().FirstOrDefault();
        if (fire is Cell f) _fire.GlobalPosition = GlobalOf(f.Center);
        _hissGain = leak != null ? MathF.Max(0.3f, Gain(leak.Center)) : 0f;
        _fireGain = fire is Cell f2 ? MathF.Max(0.25f, Gain(f2.Center)) : 0f;
    }

    private float _hissGain = 1f, _fireGain = 1f;

    // ── 파형 ──

    /// <summary>공압 문: 띠를 거른 바람 소리가 부풀었다 가라앉고, 끝에 낮게 "턱".</summary>
    private static float[] DoorSound()
    {
        int n = (int)(Rate * 0.42f);
        var s = new float[n];
        var rng = new Random(21);
        float lp = 0f, hp = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float x = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.25f * (x - lp);
            hp = lp - prev;
            prev = lp;
            float env = MathF.Sin(MathF.PI * MathF.Min(1f, t / 0.32f)) * (t < 0.32f ? 1f : 0f);
            float thunk = t > 0.3f ? MathF.Sin((t - 0.3f) * MathF.Tau * 70f) * MathF.Exp(-(t - 0.3f) * 30f) : 0f;
            s[i] = 0.9f * env * (lp * 0.6f + hp * 1.2f) + 0.5f * thunk;
        }
        return s;
    }

    /// <summary>발소리: 낮은 "툭" + 짧은 마찰음.</summary>
    private static float[] StepSound()
    {
        int n = (int)(Rate * 0.09f);
        var s = new float[n];
        var rng = new Random(33);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float thud = MathF.Sin(t * MathF.Tau * (110f - 300f * t)) * MathF.Exp(-t * 45f);
            float scuff = (float)(rng.NextDouble() * 2 - 1) * MathF.Exp(-t * 90f) * 0.35f;
            s[i] = 0.8f * (thud + scuff);
        }
        return s;
    }

    /// <summary>로봇 구동음: 짧게 올라가는 모터 소리.</summary>
    private static float[] ServoSound()
    {
        int n = (int)(Rate * 0.16f);
        var s = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float f = 380f + 900f * t;
            phase += f / Rate;
            float env = MathF.Min(1f, t / 0.02f) * MathF.Min(1f, (0.16f - t) / 0.04f);
            float x = MathF.Sin((float)(phase * MathF.Tau));
            s[i] = 0.3f * env * (x + 0.3f * MathF.Sign(x));
        }
        return s;
    }
}
