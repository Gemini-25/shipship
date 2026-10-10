using System;
using System.Linq;

namespace ShipSim.Core;

// v12.3 핵심 설비 속 부품 (분해도): 부품마다 고장 종류 · 이상 징후 원인 · 설비 인입선·배관·열·감지기 교정과 이어진다.
// 표(데이터)로 둔다 — 설비가 늘면 한 줄씩.

public enum PartLink { None, Feed, Line, Heat, Vapor, Sensor }

public sealed record PartSpec(string Name, FaultKind[] Faults, OmenCause[] Causes, PartLink Link = PartLink.None, int Circuit = -1);

public sealed record PartState(PartSpec Spec, float Health, string Text, bool Broken, bool Hidden);

public static class MachineParts
{
    private static PartSpec P(string n, FaultKind[] f, OmenCause[] c, PartLink l = PartLink.None, int circuit = -1) => new(n, f, c, l, circuit);
    private static FaultKind[] F(params FaultKind[] k) => k;
    private static OmenCause[] C(params OmenCause[] k) => k;

    public static readonly System.Collections.Generic.IReadOnlyDictionary<FurnitureType, PartSpec[]> Table = new System.Collections.Generic.Dictionary<FurnitureType, PartSpec[]>
    {
        [FurnitureType.CoolantPump] = new[]
        {
            P("모터", F(), C(OmenCause.Overload, OmenCause.Connector), PartLink.Feed),
            P("베어링", F(FaultKind.BearingWear), C(OmenCause.BearingWear, OmenCause.LooseMount)),
            P("임펠러", F(FaultKind.PumpSeized), C(OmenCause.ImpellerDamage)),
            P("축 씰", F(), C(OmenCause.MicroLeak, OmenCause.StickyValve), PartLink.Line),
            P("압력 감지기", F(FaultKind.SensorDrift), C(OmenCause.SensorOffset), PartLink.Sensor),
        },
        [FurnitureType.OxygenGenerator] = new[]
        {
            P("전해조", F(FaultKind.ElectrolyzerFault), C(OmenCause.Overload, OmenCause.InsulationDamage), PartLink.Heat),
            P("필터", F(FaultKind.FilterClogged), C(OmenCause.FilterClog)),
            P("수소 배출 밸브", F(), C(OmenCause.StickyValve, OmenCause.MicroLeak), PartLink.Vapor),
            P("전원부", F(FaultKind.WiringFault), C(OmenCause.Connector, OmenCause.LooseTerminal), PartLink.Feed),
            P("급수 입구", F(), C(), PartLink.Line),
        },
        [FurnitureType.WaterRecycler] = new[]
        {
            P("막 여과기", F(FaultKind.MembraneFouling), C(OmenCause.FilterClog)),
            P("순환 펌프", F(FaultKind.PumpSeized), C(OmenCause.ImpellerDamage, OmenCause.BearingWear)),
            P("증류기", F(), C(OmenCause.Overload), PartLink.Heat),
            P("관 이음", F(), C(OmenCause.MicroLeak), PartLink.Line),
            P("수질 감지기", F(FaultKind.SensorDrift), C(OmenCause.SensorOffset), PartLink.Sensor),
        },
        [FurnitureType.PowerPanel] = new[]
        {
            P("A 회로 차단기", F(FaultKind.BreakerTrip, FaultKind.ShortCircuit), C(), PartLink.None, 0),
            P("B 회로 차단기", F(FaultKind.BreakerTrip, FaultKind.ShortCircuit), C(), PartLink.None, 1),
            P("C 회로 차단기", F(FaultKind.BreakerTrip, FaultKind.ShortCircuit), C(), PartLink.None, 2),
            P("D 회로 차단기", F(FaultKind.BreakerTrip, FaultKind.ShortCircuit), C(), PartLink.None, 3),
            P("모선·단자", F(), C(OmenCause.LooseTerminal, OmenCause.Overload, OmenCause.InsulationDamage), PartLink.Heat),
        },
        [FurnitureType.MainComputer] = new[]
        {
            P("연산 기판", F(FaultKind.ControlFault), C(OmenCause.BoardFault)),
            P("저장 장치", F(FaultKind.StorageFault), C(OmenCause.Connector)),
            P("냉각 팬", F(FaultKind.Overheat), C(OmenCause.BearingWear), PartLink.Heat),
            P("전원부", F(FaultKind.WiringFault), C(OmenCause.LooseTerminal), PartLink.Feed),
        },
        [FurnitureType.ReactorCore] = new[]
        {
            P("제어봉 구동부", F(FaultKind.ControlFault), C(OmenCause.StickyValve, OmenCause.BoardFault)),
            P("노심 계측기", F(FaultKind.SensorDrift), C(OmenCause.SensorOffset), PartLink.Sensor),
            P("냉각 재킷", F(), C(OmenCause.MicroLeak), PartLink.Heat),
            P("차폐", F(FaultKind.Wrecked), C()),
        },
        [FurnitureType.Battery] = new[]
        {
            P("셀 묶음", F(FaultKind.CellDegradation), C(OmenCause.Overload)),
            P("셀 온도", F(), C(), PartLink.Heat),
            P("관리 회로", F(FaultKind.WiringFault), C(OmenCause.BoardFault, OmenCause.Connector), PartLink.Feed),
        },
        [FurnitureType.AuxGenerator] = new[]
        {
            P("연료 분사기", F(FaultKind.InjectorClog), C(OmenCause.FilterClog)),
            P("베어링", F(FaultKind.BearingWear), C(OmenCause.BearingWear, OmenCause.LooseMount)),
            P("발전기 권선", F(FaultKind.WiringFault), C(OmenCause.InsulationDamage), PartLink.Heat),
            P("배기", F(), C(), PartLink.Vapor),
        },
    };

    public static PartSpec[]? For(FurnitureType t) => Table.TryGetValue(t, out var p) ? p : null;

    public static PartState State(Machine m, PartSpec p, long now)
    {
        var fault = m.Faults.FirstOrDefault(f => p.Faults.Contains(f.Kind) && (p.Circuit < 0 || f.Circuit == p.Circuit));
        if (fault != null) return new PartState(p, 0.05f, $"고장 — {fault.Spec.Name}", true, false);
        if (m.Faults.Any(f => f.Kind == FaultKind.Wrecked)) return new PartState(p, 0.05f, "파손", true, false);
        float h = Math.Clamp(m.Condition * (1f - 0.6f * m.Wear), 0f, 1f);
        string text = "";
        switch (p.Link)
        {
            case PartLink.Feed when m.Feed < 0.95f: h = MathF.Min(h, m.Feed); text = m.Spliced ? "임시 접속" : "전선 상함"; break;
            case PartLink.Line when m.Line < 0.95f: h = MathF.Min(h, m.Line); text = m.Line < 0.3f ? "이음 빠짐 — 샌다" : "이음 샘"; break;
            case PartLink.Heat when m.Heat > 0.35f: h = MathF.Min(h, 1f - m.Heat); text = $"뜨겁다 {m.Heat * 100:0}%"; break;
            case PartLink.Vapor when m.Vapor > 0.2f: h = MathF.Min(h, 1f - m.Vapor); text = $"가스 {m.Vapor * 100:0}%"; break;
            case PartLink.Sensor when m.SensorCal < 0.85f: h = MathF.Min(h, m.SensorCal); text = $"교정 {m.SensorCal * 100:0}%"; break;
        }
        if (m.Fouled > 0.3f && p.Link == PartLink.Heat) { h = MathF.Min(h, 1f - m.Fouled * 0.6f); text = text.Length > 0 ? text : "그을음·때"; }
        // 이상 징후: 그 부품이 원인이면 (아무도 모르면 옅게 — 보는 사람에게는 보인다)
        if (m.Omen is Omen o && p.Causes.Contains(o.Cause))
        {
            h = MathF.Min(h, 0.4f - 0.3f * o.Level(now));
            return new PartState(p, h, $"{Causes.Name(o.Cause)}" + (o.Known ? "" : " (아직 아무도 모름)"), false, !o.Known);
        }
        if (text.Length == 0) text = h > 0.8f ? "좋음" : h > 0.5f ? "닳음" : "많이 닳음";
        return new PartState(p, h, text, false, false);
    }
}
