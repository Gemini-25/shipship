namespace ShipEngine;

/// <summary>
/// 도면의 설비 · 가구를 장에 앉힌다: 재질의 열용량은 그 칸 고체에, 타는 재질(고분자 · 천 · 음식 …)은 그 칸 바닥 저장(연료)에.
/// 설비 표(engine/data/equipment.json)를 읽는다.
/// </summary>
public static class Furnish
{
    public static void Apply(Field f)
    {
        var t = f.T;
        foreach (var fu in f.L.Furniture)
        {
            if (fu.Cells.Length == 0) continue;
            var e = t.Equip(fu.Type);
            double per = 1.0 / fu.Cells.Length;
            foreach (var (matId, kg) in e.Parts)
            {
                var m = t.M(matId);
                foreach (int c in fu.Cells)
                {
                    f.Cs[c] += kg * per * m.Def.C;
                    foreach (var (s, molPerKg) in m.Made)
                    {
                        var sub = t.Substances[s];
                        if (sub.Store >= 0 && sub.Phase == MatterPhase.Solid) f.AddStore(c, sub.Store, kg * per * molPerKg); // 음식 속 물은 바닥에 고이지 않는다
                    }
                }
            }
        }
    }
}
