using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// 물건 종류. 물건은 "우주선 전체 재고"가 아니라 어딘가(선반, 냉장고, 승무원 손)에 실제로 있다.
/// 그래야 "부품은 있는데 창고가 감압돼서 못 가져옴" 같은 일이 생긴다.
/// </summary>
public enum ItemKind
{
    Produce,    // 채소 (수경재배 수확물)
    Meal,       // 조리된 식사
    Ration,     // 비상식량
    Lubricant,  // 윤활유 (펌프·엔진 정비) — 채소로도 만든다
    Filter,     // 필터 (산소 발생기·정수기 정비) — 채소로도 만든다
    Cable,      // 케이블 (전기 수리)
    Fuse,       // 퓨즈 (배전반)
    Sealant,    // 실링폼 (임시 밀폐)
    MedKit,     // 구급 키트
    Suit,       // 우주복
    Extinguisher, // 소화기 (쓰고 나면 다시 걸어 둔다)
    Fuel,       // 보조 발전기 연료통 (채소로 만든 바이오 연료도 된다)

    // ── 원료: 채집 장치가 주변 우주 공간에서 모은다 ──
    MetalOre,   // 금속 원료 (금속성 먼지, 잔해)
    Silicate,   // 규산 원료
    Carbon,     // 탄소 원료
    Ice,        // 얼음·휘발성 물질 → 공기 탱크, 물
    Rare,       // 희귀 소재 (아주 드물게)

    // ── 기본 수리재: 정제기에서 원료로 만든다 ──
    Plate,      // 금속판 (외벽 용접, 부품 몸체)
    Structure,  // 구조재 (외벽 패널 교체)
    Electronics,// 단순 전자재

    // ── 일반 부품: 작업대에서 기본 수리재로 만든다 (시간이 걸린다) ──
    Motor,
    Pump,
    Bearing,
    PowerController,
    Sensor,

    // ── 고급 부품: 희귀 소재와 오랜 정밀 작업이 필요하다 ──
    ReactorControl,

    // ── v15 물자 70 (CatalogV15.cs): 부품 24 · 소모품 20 ──
    Gasket, Seal, Valve, Fan, Belt, Relay, Capacitor, Insulator, Coupling, Impeller, Brush, Nozzle,
    Membrane, Thermostat, Diode, Spring, Hose, Clamp, Gear, Bushing, Fiber, Lamp, Thermocouple, Solenoid,
    Solvent, Tape, Glue, Nutrient, Seed, Soap, Detergent, Disinfectant, Bandage, Coffee, TeaLeaf, Spice,
    Vitamin, Gloves, Rag, CellPack, Thread, Paint, Desiccant, Mesh,
    // 의료 2차 — 면역억제제 · 투석액 · 세포 잉크
    Immunosuppressant, Dialysate, BioInk,
}

/// <summary>재료 등급 (리뷰어 안: 원료 → 기본 수리재 → 일반 부품 → 고급 부품).</summary>
public enum ItemTier { Supply, Raw, Basic, General, Advanced }

public static class ItemKinds
{
    public static string Name(ItemKind k) => k switch
    {
        ItemKind.Produce => "채소",
        ItemKind.Meal => "식사",
        ItemKind.Ration => "비상식량",
        ItemKind.Lubricant => "윤활유",
        ItemKind.Filter => "필터",
        ItemKind.Cable => "케이블",
        ItemKind.Fuse => "퓨즈",
        ItemKind.Sealant => "실링폼",
        ItemKind.MedKit => "구급 키트",
        ItemKind.Suit => "우주복",
        ItemKind.Extinguisher => "소화기",
        ItemKind.Fuel => "연료통",
        ItemKind.MetalOre => "금속 원료",
        ItemKind.Silicate => "규산 원료",
        ItemKind.Carbon => "탄소 원료",
        ItemKind.Ice => "얼음",
        ItemKind.Rare => "희귀 소재",
        ItemKind.Plate => "금속판",
        ItemKind.Structure => "구조재",
        ItemKind.Electronics => "전자재",
        ItemKind.Motor => "모터",
        ItemKind.Pump => "펌프 부품",
        ItemKind.Bearing => "베어링",
        ItemKind.PowerController => "전력 제어기",
        ItemKind.Sensor => "센서",
        ItemKind.ReactorControl => "원자로 제어부",
        // 의료 2차
        ItemKind.Immunosuppressant => "면역억제제",
        ItemKind.Dialysate => "투석액",
        ItemKind.BioInk => "세포 잉크",
        _ => ItemsV15.Name(k) ?? k.ToString(), // v15
    };

    public static ItemTier Tier(ItemKind k) => k switch
    {
        ItemKind.MetalOre or ItemKind.Silicate or ItemKind.Carbon or ItemKind.Ice or ItemKind.Rare => ItemTier.Raw,
        ItemKind.Plate or ItemKind.Structure or ItemKind.Cable or ItemKind.Fuse or ItemKind.Electronics or ItemKind.Sealant => ItemTier.Basic,
        ItemKind.Motor or ItemKind.Pump or ItemKind.Bearing or ItemKind.PowerController or ItemKind.Sensor => ItemTier.General,
        ItemKind.ReactorControl => ItemTier.Advanced,
        _ => ItemsV15.Tier(k), // v15
    };

    public static string TierName(ItemTier t) => t switch
    {
        ItemTier.Raw => "원료",
        ItemTier.Basic => "기본 수리재",
        ItemTier.General => "일반 부품",
        ItemTier.Advanced => "고급 부품",
        _ => "물자",
    };

    public static bool IsFood(ItemKind k) => k is ItemKind.Produce or ItemKind.Meal or ItemKind.Ration;

    public static readonly ItemKind[] All = (ItemKind[])Enum.GetValues(typeof(ItemKind));

    /// <summary>원료 (채집 장치 호퍼에 담긴다).</summary>
    public static readonly ItemKind[] RawKinds = { ItemKind.MetalOre, ItemKind.Silicate, ItemKind.Carbon, ItemKind.Ice, ItemKind.Rare };

    /// <summary>선반에 두는 것 (식량·우주복 빼고 전부).</summary>
    public static readonly ItemKind[] Shelved = All.Where(k => k is not (ItemKind.Produce or ItemKind.Meal or ItemKind.Suit)).ToArray();
}

/// <summary>보관함 하나. 넣을 수 있는 종류와 총량 한도가 있다.</summary>
public sealed class Inventory
{
    private readonly Dictionary<ItemKind, int> _items = new();
    private readonly HashSet<ItemKind>? _accepts;

    public int Capacity { get; }

    public Inventory(int capacity, params ItemKind[] accepts)
    {
        Capacity = capacity;
        _accepts = accepts.Length > 0 ? new HashSet<ItemKind>(accepts) : null;
    }

    public int Total => _items.Values.Sum();
    public int Free => Capacity - Total;
    public int Count(ItemKind k) => _items.TryGetValue(k, out int n) ? n : 0;
    public bool Accepts(ItemKind k) => _accepts == null || _accepts.Contains(k);

    public IEnumerable<(ItemKind kind, int count)> Contents =>
        ItemKinds.All.Where(k => Count(k) > 0).Select(k => (k, Count(k)));

    /// <summary>넣은 개수를 돌려준다 (자리가 모자라면 일부만).</summary>
    public int Add(ItemKind k, int n)
    {
        if (!Accepts(k) || n <= 0) return 0;
        int added = Math.Min(n, Free);
        if (added > 0) _items[k] = Count(k) + added;
        return added;
    }

    /// <summary>꺼낸 개수를 돌려준다.</summary>
    public int Take(ItemKind k, int n)
    {
        int taken = Math.Min(n, Count(k));
        LastTainted = 0;
        if (taken > 0)
        {
            int left = Count(k) - taken;
            if (left == 0) _items.Remove(k);
            else _items[k] = left;
            // v11.2: 균이 든 식사는 먼저 만든 묶음이라 먼저 나간다
            if (k == ItemKind.Meal && Tainted > 0)
            {
                LastTainted = Math.Min(Tainted, taken);
                Tainted -= LastTainted;
                if (Tainted == 0) TaintKnown = false;
            }
        }
        return taken;
    }

    /// <summary>v11.2 균이 든 식사 수 (식사 중 몇 끼).</summary>
    public int Tainted { get; private set; }

    /// <summary>오염을 알아챘는지 (누가 앓아누우면 같은 묶음을 찾아낸다).</summary>
    public bool TaintKnown { get; set; }

    /// <summary>방금 꺼낸 것 중 균이 든 식사 수.</summary>
    public int LastTainted { get; private set; }

    public void Taint(int n) => Tainted = Math.Min(Count(ItemKind.Meal), Tainted + Math.Max(0, n));

    public void ClampTaint()
    {
        if (Tainted > Count(ItemKind.Meal)) Tainted = Count(ItemKind.Meal);
        if (Tainted == 0) TaintKnown = false;
    }
}

/// <summary>승무원이 손에 든 물건 한 묶음.</summary>
public readonly record struct ItemStack(ItemKind Kind, int Count)
{
    public override string ToString() => $"{ItemKinds.Name(Kind)} {Count}";
}
