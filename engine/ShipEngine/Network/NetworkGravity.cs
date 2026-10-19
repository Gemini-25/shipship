namespace ShipEngine;

/// <summary>
/// 인공 중력 (바퀴 3 (가) — docs/ENGINE.md '인공 중력 · 무중력'): 발생기(role gravity)가 제 몸(④ 칸 강체 — 배 · 떨어져 나간 조각)의 바닥 중력을 만든다.
/// <para>돌면(켜짐 · 전기 · 전기 · 기계 부품 멀쩡) 장이 30초에 걸쳐 1g로 서고 · 멈추면 10초에 걸쳐 0으로 무너진다 — 몸마다 그 안의 가장 센 발생기.
/// 칸마다 ③ 장의 <see cref="Field.Grav"/>에 요청으로 쓴다 (아래 엔진이 읽는다). 발생기가 하나도 없는 배(시험 배 · 행성 기지)는 바탕 1g 그대로 둔다.
/// 몸의 중력이 반 아래로 무너지면 · 다시 서면 사실 '중력이 바뀌었다' (원인: 발생기가 멈춘 까닭 — 고장 · 꺼짐 · 전기).</para>
/// </summary>
public sealed partial class Network
{
    /// <summary>[장치] 중력 발생기 장의 세기 (0~1).</summary>
    public float[] GravField = Array.Empty<float>();
    /// <summary>[몸] 지난 틱 바닥 중력 (사실을 낼 때 견준다 — 몸 0이 배).</summary>
    public float[] BodyG = Array.Empty<float>();
    /// <summary>[몸] 중력이 무너진 원인 마디 (서면 0).</summary>
    public int[] BodyGCause = Array.Empty<int>();
    public const double GravUpSec = 30, GravDownSec = 10;
    private bool? _hasGrav;

    public bool HasGravity => _hasGrav ??= Devices.Any(d => d.Def.Role == "gravity");

    private void GravityStep(Sim sim, double dt)
    {
        if (!HasGravity) return;
        if (GravField.Length != Devices.Count) GravField = new float[Devices.Count];
        int bodies = Math.Max(1, _stc?.Bodies.Count ?? 0);
        if (BodyG.Length < bodies)
        {
            // 새로 떨어져 나간 조각은 배의 중력을 지닌 채 시작한다 (장이 무너지는 데 10초)
            int old = BodyG.Length;
            Array.Resize(ref BodyG, bodies); Array.Resize(ref BodyGCause, bodies);
            for (int b = Math.Max(1, old); b < bodies; b++) BodyG[b] = BodyG[0];
            _gravDirty = true;
        }
        Span<float> level = stackalloc float[bodies];
        Span<int> src = stackalloc int[bodies];
        level.Clear(); src.Fill(-1);
        foreach (var d in Devices)
        {
            if (d.Def.Role != "gravity") continue;
            float g = GravField[d.Id];
            g = Working(d.Id) ? (float)Math.Min(1, g + dt / GravUpSec) : (float)Math.Max(0, g - dt / GravDownSec);
            GravField[d.Id] = g;
            int b = Math.Max(0, _stc?.BodyOf(d.Cell) ?? 0);
            if (b < bodies && (src[b] < 0 || g > level[b])) { level[b] = g; src[b] = d.Id; }
        }
        for (int b = 0; b < bodies; b++)
            if (src[b] < 0) level[b] = (float)Math.Max(0, BodyG[b] - dt / GravDownSec);   // 발생기가 없는 몸: 남은 장이 무너진다
        bool changed = false;
        for (int b = 0; b < bodies; b++)
        {
            if (level[b] == BodyG[b]) continue;
            changed = true;
            int dev = src[b] >= 0 ? src[b] : Devices.First(d => d.Def.Role == "gravity").Id;
            if (BodyG[b] >= 0.5f && level[b] < 0.5f)
            {
                string text = src[b] < 0 ? $"떨어져 나간 조각에는 {Devices[dev].Name}가 없어 중력이 사라진다" : $"{Devices[dev].Name}가 멈춰 중력이 사라진다 — {WhyGravityStopped(dev)}";
                BodyGCause[b] = sim.Causes.Add(CauseKind.Machine, text, sim.Tick, Devices[dev].Cell, src[b] < 0 && _stc != null && b < _stc.Bodies.Count ? _stc.Bodies[b].PushCause : WhyGravityParent(dev));
                sim.Facts.Emit(sim.Tick, FactKind.Gravity, Devices[dev].Cell, 0, BodyGCause[b], other: b);
            }
            else if (BodyG[b] < 0.5f && level[b] >= 0.5f)
            {
                int back = sim.Causes.Add(CauseKind.Machine, $"{Devices[dev].Name}가 다시 돌아 중력이 돌아온다", sim.Tick, Devices[dev].Cell, BodyGCause[b]);
                sim.Facts.Emit(sim.Tick, FactKind.Gravity, Devices[dev].Cell, 1, back, other: b);
                BodyGCause[b] = 0;
            }
            BodyG[b] = level[b];
        }
        if (!changed && !_gravDirty) return;
        _gravDirty = false;
        for (int c = 0; c < L.N; c++) F.Grav[c] = BodyG[Math.Min(bodies - 1, Math.Max(0, _stc?.BodyOf(c) ?? 0))];
    }

    private bool _gravDirty = true;

    private string WhyGravityStopped(int d)
    {
        if (Broken[d * 4 + (int)Part.Electric] != 0 || Broken[d * 4 + (int)Part.Mechanical] != 0) return "망가졌다";
        if (On[d] == 0) return "꺼졌다";
        return "전기가 끊겼다";
    }

    private int WhyGravityParent(int d)
    {
        for (int p = 0; p < 2; p++) if (Broken[d * 4 + p] != 0 && _brokeCause.TryGetValue(d * 4 + p, out int bc)) return bc;
        if (On[d] == 0) return OnCause[d];
        return ReactorScram ? ScramCause : 0;
    }

    private void SaveGravity(StateWriter w) { w.Tag("중력"); w.Arr(GravField); w.Arr(BodyG); w.Arr(BodyGCause); }
    private void LoadGravity(StateReader r) { r.Tag("중력"); GravField = r.Arr<float>(); BodyG = r.Arr<float>(); BodyGCause = r.Arr<int>(); _gravDirty = true; }
}
