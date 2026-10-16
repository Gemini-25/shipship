namespace ShipEngine;

/// <summary>
/// ③ 장 — 빛 · 보이는 거리. 5틱마다 칸마다 밝기(럭스)를 다시 잰다:
/// 방 조명(⑥ 망이 켜고 끈다 — <see cref="SetLamp"/>) · 불꽃(구역 안 거리 제곱으로 줄어든다) · 장치 화면 불빛.
/// 연기(그을음)는 빛을 가리고 보이는 거리를 줄인다 (σ = 8.7 m²/g × 그을음 농도 · 보이는 거리 ≈ 3/σ).
/// </summary>
public sealed class Light : IEngine
{
    public string Name => "빛";
    public Phase Phase => Phase.Field;
    public int Every => 5;
    public int Offset => 2;

    private readonly Field F;
    private readonly Layout L;
    /// <summary>칸마다 밝기 (럭스).</summary>
    public float[] Lux;
    /// <summary>방마다 조명 (럭스 · 방 번호) — 망이 정한다. 처음엔 모두 300.</summary>
    public float[] Lamp;
    /// <summary>칸마다 더해지는 빛 (화면 · 비상등 · 손전등 — 장치 · 사람이 요청).</summary>
    public float[] Extra;

    public Light(Field f)
    {
        F = f; L = f.L;
        Lux = new float[L.N];
        Lamp = Enumerable.Repeat(300f, Math.Max(1, L.Rooms.Count)).ToArray();
        Extra = new float[L.N];
    }

    public void SetLamp(int room, float lux) { if (room >= 0 && room < Lamp.Length) Lamp[room] = lux; }

    /// <summary>보이는 거리 (m, 많아야 30): 그을음이 짙을수록 짧다.</summary>
    public double Visibility(int cell)
    {
        if (!L.IsSpace(cell)) return 0;
        double sootKgM3 = F.Gas(F.T.gSoot, cell) * 0.012 / Field.CellVolume;
        double sigma = 8700 * sootKgM3;
        double v = sigma > 1e-9 ? 3 / sigma : 30;
        return Math.Min(30, v);
    }

    public void Step(Sim sim, double dt)
    {
        var chem = sim.Find<Chemistry>();
        for (int i = 0; i < L.N; i++)
        {
            if (!L.IsSpace(i)) { Lux[i] = 0; continue; }
            int room = L.Room[i];
            float lamp = room >= 0 && room < Lamp.Length ? Lamp[room] : 0;
            double vis = Visibility(i);
            float shade = (float)Math.Min(1, vis / 10); // 짙은 연기는 조명도 가린다
            Lux[i] = lamp * (0.3f + 0.7f * shade) + Extra[i];
        }
        if (chem == null) return;
        // 불꽃 빛: 구역 안에서 거리 제곱으로 (반지름 8)
        Span<int> nb = stackalloc int[4];
        for (int i = 0; i < L.N; i++)
        {
            if (chem.Hrr[i] < 5) continue;
            float src = chem.Hrr[i] * 40; // 200kW 불 → 곁 칸 8000럭스쯤
            int region = F.RegionOf.Length > i ? F.RegionOf[i] : -1;
            if (region < 0) continue;
            var dist = new Dictionary<int, int> { [i] = 0 };
            var q = new Queue<int>(); q.Enqueue(i);
            while (q.Count > 0)
            {
                int c = q.Dequeue(); int d = dist[c];
                Lux[c] += src / (1 + d * d);
                if (d >= 8) continue;
                L.Neighbors(c, nb);
                foreach (int j in nb)
                    if (j >= 0 && F.RegionOf[j] == region && !dist.ContainsKey(j)) { dist[j] = d + 1; q.Enqueue(j); }
            }
        }
    }

    public void Save(StateWriter w) { w.Arr(Lux); w.Arr(Lamp); w.Arr(Extra); }
    public void Load(StateReader r) { r.ArrInto(Lux); Lamp = r.Arr<float>(); r.ArrInto(Extra); }
}
