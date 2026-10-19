using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShipEngine;

public enum MatterPhase : byte { Gas, Aerosol, Liquid, Solid }

public enum ReactionKind : byte { Fire, GasFire, Slow, Electric, Device, Life, Bio }

[Flags]
public enum ReactionNeed : byte { None = 0, Wet = 1, Light = 2, LowO2 = 4, NoO2 = 8 }

// ── 표에서 읽는 모양 ──

public sealed class SmellDef { public string Name = ""; public double Ppm; }

public sealed class SubstanceDef
{
    public string Id = "", Name = "", Formula = "";
    public MatterPhase Phase;
    public double Hf, C;
    public bool Cell;
    public string? Condense;
    public double BoilK;
    public double Harm, Deadly;
    public SmellDef? Smell;
    public string? Color;
}

public sealed class ReactionDef
{
    public string Id = "", Name = "";
    public ReactionKind Kind;
    public Dictionary<string, double> In = new(), Out = new();
    public double MinK, Rate = 1;
    /// <summary>② 사고 화학 (다): 탈 것의 한계 산소 지수 — 25℃에서 불꽃이 스스로 서는 가장 옅은 산소 몰분율 (0 = 없음).</summary>
    public double Loi;
    public string[] Need = Array.Empty<string>();
}

public sealed class StarDef { public string Name = ""; public double LightAt1, FlaresPerDay, FlareDose, FlareHours, WarnMin, BaseDose; }
public sealed class PlanetDef
{
    public string Name = "";
    public double KPa, DayK, NightK, DayHours, G, Wind, StormsPerDay;
    public Dictionary<string, double> Air = new();
}
public sealed class DebrisDef { public string Name = ""; public double MinKg, MaxKg, Alpha, Speed; }
/// <summary>⑤ 별계의 천체 하나: 이름 · 궤도 반지름(AU) · 내려앉을 수 있는 행성 종류.</summary>
public sealed class BodyDef { public string Name = "", Planet = ""; public double Au; }
/// <summary>⑤ 지어낸 별계 (실제 물리): 별 · 천체 궤도 · 은하 우주선(거리와 상관없는 바깥 방사선 mGy/시).</summary>
public sealed class SystemDef { public string Name = "", Star = "yellow"; public double Gcr; public List<BodyDef> Bodies = new(); }

/// <summary>⑤ 배의 운동 상수 (space.json ship).</summary>
public sealed class ShipMotionDef { public double TonsPerCell = 1, EngineKN = 2300, Isp = 900, RcsG = 0.02, RcsIsp = 300, PropellantRatio = 1, RadarKm = 100, RadarMrad = 0.5; }
/// <summary>⑤ 항로의 위험 구간: from~to AU · 파편 종류 · 하루 평균 구름 · 운석.</summary>
public sealed class ZoneDef { public string Name = "", Debris = "rubble"; public double From, To, CloudsPerDay, RocksPerDay; }
/// <summary>⑤ 다른 배가 지나가는 빈도.</summary>
public sealed class TrafficDef { public double PerDay = 0.25, NearAu = 0.04, Boost = 4; }

public sealed class SpaceTable
{
    public ShipMotionDef Ship = new();
    public List<ZoneDef> Zones = new();
    public TrafficDef Traffic = new();
    public SystemDef System = new();
    public Dictionary<string, StarDef> Stars = new();
    public Dictionary<string, PlanetDef> Planets = new();
    public Dictionary<string, DebrisDef> Debris = new();
}

/// <summary>장치 표 (⑥ 망 · 장치): 역할과 숫자.</summary>
public sealed class DeviceDef
{
    public string Role = "";
    public double Kw, Gen, Kwh, HeatKw, Mol;
    /// <summary>돌아갈 때 1m에서의 소리 크기 (dBA) — 바탕 소음 (③ 소리).</summary>
    public double Db;
    /// <summary>재배 선반의 단 수 (재배 넓이 = 칸 × 단).</summary>
    public int Tiers = 1;
    public bool On;
    public Dictionary<string, double> Parts = new();
}

/// <summary>사람 표 (⑧ 마음): 이름 · 맡은 일 · 물건 · 빠르기 · 대사량 · 욕구 · 믿음 반감기.</summary>
public sealed class PeopleTable
{
    public List<string> Names = new();
    public Dictionary<string, RoleDef> Roles = new();
    public Dictionary<string, Dictionary<string, int>> Stock = new();
    public Dictionary<string, ItemDef> Items = new();
    public Dictionary<string, RecipeDef> Recipes = new();
    public Dictionary<string, MakerDef> Makers = new();
    public Dictionary<string, double> Speed = new(), Watts = new(), Needs = new(), HalfLife = new();
}

public sealed class RoleDef
{
    public List<string> Rooms = new(), Devices = new();
    public Dictionary<string, double> Skills = new();
    /// <summary>⑧ 판단 엔진: 분야를 아는 정도 (0~1, 처음 값 — knowledge.json 분야 이름).</summary>
    public Dictionary<string, double> Knows = new();
}

public sealed class ItemDef
{
    public double Water, O2, Minutes, MaxArea;
    /// <summary>이산화탄소 소화기에 든 이산화탄소 (몰 — ② (바)).</summary>
    public double Co2;
    /// <summary>무게 (kg) — 감압 바람에 밀리나 · 문틀에 걸리나 · 맞으면 얼마나 아프나.</summary>
    public double Kg = 2;
}

/// <summary>⑪ 만들기: 어느 설비에서 · 몇 분 · 무엇이 드나 · 배에 몇 개를 두려 하나.</summary>
public sealed class RecipeDef
{
    public string At = "";
    public double Minutes;
    public Dictionary<string, int> Needs = new();
    public int Keep;
}

/// <summary>⑪ 저절로 만드는 설비 (전기를 먹고): 무엇을 · 몇 시간에 하나 · 많아야 몇 개.</summary>
public sealed class MakerDef
{
    public string Item = "";
    public double Hours;
    public int Max;
}

public sealed class EquipmentDef
{
    public string Name = "";
    public Dictionary<string, double> Parts = new();
    public double Heat;
    /// <summary>포장되어 상하지 않는다 (식량 보관함).</summary>
    public bool Sealed;
}

public sealed class MaterialDef
{
    public string Id = "", Name = "";
    public Dictionary<string, double> Made = new();
    public double Density, C, K, Strength, Fatigue, MeltK, IgnK, Emiss, Absorb, Penetrate;
    // ── 파괴 역학 · 부식 (깊이 바퀴 2 — ④ 금 · ② 부식) ──
    /// <summary>탄성 계수 (GPa).</summary>
    public double E = 200;
    /// <summary>파괴 인성 K_c (MPa√m — 얇은 판 · 실온) · 차가울 때 (연성-취성 전이 아래) · 전이 온도 (K · 0이면 전이 없음).</summary>
    public double Kc = 100, KcLow, DbttK;
    /// <summary>파리스 법칙 da/dN = C·ΔK^m (m/주기 · MPa√m) · 자라지 않는 문턱 ΔKth.</summary>
    public double ParisC, ParisM = 3, DKth = 5;
    /// <summary>응력 부식 균열: 문턱 K_ISCC (MPa√m) · 젖었을 때 자라는 빠르기 (m/s).</summary>
    public double Kiscc = 1e9, Scc;
    /// <summary>열팽창 (1/K).</summary>
    public double Alpha = 12e-6;
    /// <summary>젖은 곳의 고른 부식 (µm/년 — ISO 9223 C3~C4) · 공식(구멍 부식) 배율 · 전위 (V — 바닷물 속 갈바닉 서열 · 낮을수록 먼저 녹는다).</summary>
    public double Corr, Pit = 1, Potential;
}

// ── 실행 중의 모양 ──

/// <summary>물질 하나 (표 + 번호 + 원소 + 몰 질량).</summary>
public sealed class Substance
{
    public int Index;
    public SubstanceDef Def = null!;
    public string Id => Def.Id;
    public string Name => Def.Name;
    public MatterPhase Phase => Def.Phase;
    public bool Airborne => Def.Phase is MatterPhase.Gas or MatterPhase.Aerosol;
    public double Hf => Def.Hf;
    public double C => Def.C;
    /// <summary>원소 번호 → 개수.</summary>
    public (int el, int n)[] Elements = Array.Empty<(int, int)>();
    /// <summary>kg/몰.</summary>
    public double MolarMass;
    /// <summary>기체 칸 번호 (기체 · 연무만, 아니면 -1).</summary>
    public int Gas = -1;
    /// <summary>칸 바닥 저장 번호 (cell 물질만, 아니면 -1).</summary>
    public int Store = -1;
    /// <summary>응결하면 되는 물질 (없으면 null).</summary>
    public Substance? CondensesTo;

    /// <summary>
    /// 에너지 장부에서 이 물질 1몰의 값 (J): 생성 엔탈피 + 기체 · 연무는 현열 c(T-298.15).
    /// 응축상은 현열을 따로(바닥 · 몸의 열용량) 다루므로 생성 엔탈피만 — 반응열이 이 장부로 맞아떨어진다.
    /// </summary>
    public double Energy(double T) => Hf * 1000.0 + (Airborne ? C * (T - 298.15) : 0.0);
}

public readonly record struct Term(int S, double Nu);

/// <summary>반응 하나.</summary>
public sealed class Reaction
{
    public int Index;
    public ReactionDef Def = null!;
    public string Id => Def.Id;
    public string Name => Def.Name;
    public ReactionKind Kind => Def.Kind;
    public ReactionNeed Need;
    public Term[] In = Array.Empty<Term>(), Out = Array.Empty<Term>();
    /// <summary>표준 반응 엔탈피 (J · 반응 1몰).</summary>
    public double DH298;

    /// <summary>반응 1몰이 칸 기체에 내놓는 열 (J) — 온도 T에서 (에너지 장부를 맞추는 키르히호프 보정 포함).</summary>
    public double Heat(Tables t, double T)
    {
        double q = 0;
        foreach (var r in In) q += r.Nu * t.Substances[r.S].Energy(T);
        foreach (var p in Out) q -= p.Nu * t.Substances[p.S].Energy(T);
        return q;
    }
}

public sealed class Material
{
    public int Index;
    public MaterialDef Def = null!;
    public string Id => Def.Id;
    public string Name => Def.Name;
    /// <summary>질량 1kg에 든 물질별 몰 수.</summary>
    public (int s, double molPerKg)[] Made = Array.Empty<(int, double)>();
    public bool Burns => Def.IgnK > 0;
}

/// <summary>
/// 물질 · 반응 · 재질 표 (② 물질). 읽을 때 검사한다: 화학식 · 원소 · 반응식 균형 · 이름 중복.
/// </summary>
public sealed class Tables
{
    private static readonly Lazy<Tables> _default = new(() => Load());
    public static Tables Default => _default.Value;

    public string[] ElementSymbols = Array.Empty<string>();
    public double[] ElementMass = Array.Empty<double>(); // g/몰
    public List<Substance> Substances = new();
    public List<Reaction> Reactions = new();
    public List<Material> Materials = new();
    private readonly Dictionary<string, Substance> _subById = new();
    private readonly Dictionary<string, Reaction> _rxById = new();
    private readonly Dictionary<string, Material> _matById = new();

    /// <summary>기체 칸 순서대로의 물질 (장 물리의 성분).</summary>
    public Substance[] Gases = Array.Empty<Substance>();
    /// <summary>칸 바닥 저장 순서대로의 물질.</summary>
    public Substance[] Stores = Array.Empty<Substance>();

    /// <summary>설비 · 가구 표 (옛 설비 이름 → 재질 kg · 열).</summary>
    public Dictionary<string, EquipmentDef> Equipment = new();
    /// <summary>장치 표 (옛 설비 이름 → 역할 · 숫자). 없는 설비는 장치가 아니다 (의자 · 침대 …).</summary>
    public Dictionary<string, DeviceDef> Devices = new();
    public PeopleTable People = new();
    public DeviceDef? Dev(string type) => Devices.TryGetValue(type, out var d) ? d : null;

    /// <summary>별 · 행성 · 파편 표 (⑤ 바깥 세계).</summary>
    public SpaceTable Space = new();

    public EquipmentDef Equip(string type) => Equipment.TryGetValue(type, out var e) ? e : Equipment["_default"];

    public Substance S(string id) => _subById.TryGetValue(id, out var s) ? s : throw new KeyNotFoundException($"물질 표에 없다: {id}");
    public Reaction R(string id) => _rxById.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException($"반응식 표에 없다: {id}");
    public Material M(string id) => _matById.TryGetValue(id, out var m) ? m : throw new KeyNotFoundException($"재질 표에 없다: {id}");
    public bool HasMaterial(string id) => _matById.ContainsKey(id);

    public int GasCount => Gases.Length;
    public int StoreCount => Stores.Length;

    // 자주 쓰는 기체 칸 번호
    public int gN2, gO2, gAr, gCO2, gH2O, gCO, gH2, gCH4, gNH3, gO3, gSoot, gAroma, gHCl, gHCN, gHF, gPF5, gPOF3;
    // 자주 쓰는 바닥 저장 번호
    public int sWater, sNH3l, sSoot, sPoly, sCell, sFood, sPvc, sPu, sEC, sLiPF6, sLiF;
    /// <summary>바닥 저장 중 염 (말라붙은 소금 · 소금물의 염 — 없으면 -1).</summary>
    public int sSalt = -1;

    public static Tables Load()
    {
        var t = new Tables();
        var el = JsonSerializer.Deserialize<Dictionary<string, double>>(DataFiles.Text("elements.json"), DataFiles.Json)!;
        t.ElementSymbols = el.Keys.ToArray();
        t.ElementMass = el.Values.ToArray();

        foreach (var d in DataFiles.Load<List<SubstanceDef>>("substances.json"))
        {
            if (t._subById.ContainsKey(d.Id)) throw new InvalidDataException($"물질 표: '{d.Id}'가 둘이다");
            var s = new Substance { Index = t.Substances.Count, Def = d, Elements = t.ParseFormula(d.Formula, d.Id) };
            s.MolarMass = s.Elements.Sum(e => e.n * t.ElementMass[e.el]) / 1000.0;
            if (d.C <= 0) throw new InvalidDataException($"물질 표: '{d.Id}'의 열용량이 없다");
            t.Substances.Add(s);
            t._subById[d.Id] = s;
        }
        foreach (var s in t.Substances)
            if (s.Def.Condense is string to)
            {
                s.CondensesTo = t.S(to);
                if (!SameElements(s, s.CondensesTo)) throw new InvalidDataException($"물질 표: '{s.Id}' → '{to}' 응결은 화학식이 같아야 한다");
                if (s.Def.BoilK <= 0) throw new InvalidDataException($"물질 표: '{s.Id}'의 끓는점이 없다");
            }
        t.Gases = t.Substances.Where(s => s.Airborne).ToArray();
        for (int i = 0; i < t.Gases.Length; i++) t.Gases[i].Gas = i;
        t.Stores = t.Substances.Where(s => s.Def.Cell).ToArray();
        for (int i = 0; i < t.Stores.Length; i++) t.Stores[i].Store = i;

        foreach (var d in DataFiles.Load<List<ReactionDef>>("reactions.json"))
        {
            if (t._rxById.ContainsKey(d.Id)) throw new InvalidDataException($"반응식 표: '{d.Id}'가 둘이다");
            var r = new Reaction
            {
                Index = t.Reactions.Count, Def = d,
                In = d.In.Select(kv => new Term(t.S(kv.Key).Index, kv.Value)).ToArray(),
                Out = d.Out.Select(kv => new Term(t.S(kv.Key).Index, kv.Value)).ToArray(),
            };
            foreach (var n in d.Need)
                r.Need |= n switch
                {
                    "wet" => ReactionNeed.Wet, "light" => ReactionNeed.Light, "lowO2" => ReactionNeed.LowO2, "noO2" => ReactionNeed.NoO2,
                    _ => throw new InvalidDataException($"반응식 표: '{d.Id}'의 조건 '{n}'을 모른다"),
                };
            var bal = t.Imbalance(r);
            if (bal != null) throw new InvalidDataException($"반응식 표: '{d.Id}' 원소가 맞지 않는다 — {bal}");
            r.DH298 = r.Out.Sum(p => p.Nu * t.Substances[p.S].Hf * 1000) - r.In.Sum(p => p.Nu * t.Substances[p.S].Hf * 1000);
            t.Reactions.Add(r);
            t._rxById[d.Id] = r;
        }

        foreach (var d in DataFiles.Load<List<MaterialDef>>("materials.json"))
        {
            if (t._matById.ContainsKey(d.Id)) throw new InvalidDataException($"재질 표: '{d.Id}'가 둘이다");
            double sum = d.Made.Values.Sum();
            if (Math.Abs(sum - 1) > 1e-6) throw new InvalidDataException($"재질 표: '{d.Id}' 비율 합이 1이 아니다 ({sum})");
            var m = new Material
            {
                Index = t.Materials.Count, Def = d,
                Made = d.Made.Select(kv => { var s = t.S(kv.Key); return (s.Index, kv.Value / s.MolarMass); }).ToArray(),
            };
            t.Materials.Add(m);
            t._matById[d.Id] = m;
        }

        t.Equipment = DataFiles.Load<Dictionary<string, EquipmentDef>>("equipment.json");
        foreach (var (k, e) in t.Equipment)
            foreach (var m in e.Parts.Keys)
                if (!t._matById.ContainsKey(m)) throw new InvalidDataException($"설비 표: '{k}'의 재질 '{m}'이 재질 표에 없다");
        if (!t.Equipment.ContainsKey("_default")) throw new InvalidDataException("설비 표에 _default가 없다");
        t.Space = DataFiles.Load<SpaceTable>("space.json");
        t.Devices = DataFiles.Load<Dictionary<string, DeviceDef>>("devices.json");
        var roles = new HashSet<string> { "panel", "reactor", "aux", "battery", "pump", "hvac", "o2gen", "scrubber", "dehumid", "recycler",
                                          "computer", "console", "sensor", "stove", "fridge", "grow", "charger", "industry", "appliance", "lamp", "engine" };
        var parts = new HashSet<string> { "전기", "기계", "봉함", "거르개" };
        foreach (var (k, d) in t.Devices)
        {
            if (!roles.Contains(d.Role)) throw new InvalidDataException($"장치 표: '{k}'의 역할 '{d.Role}'을 모른다");
            foreach (var p in d.Parts.Keys) if (!parts.Contains(p)) throw new InvalidDataException($"장치 표: '{k}'의 부품 '{p}'를 모른다");
        }
        t.People = DataFiles.Load<PeopleTable>("people.json");
        foreach (var (k, st) in t.People.Stock)
            foreach (var it in st.Keys)
                if (!t.People.Items.ContainsKey(it)) throw new InvalidDataException($"사람 표: '{k}'에 든 '{it}'이 물건 표에 없다");
        foreach (var key in new[] { "walk", "run", "crawl", "carry", "robot", "drone" })
            if (!t.People.Speed.ContainsKey(key)) throw new InvalidDataException($"사람 표: 빠르기 '{key}'가 없다");
        foreach (var (k, p) in t.Space.Planets)
        {
            foreach (var g in p.Air.Keys)
                if (!t._subById.TryGetValue(g, out var sub) || !sub.Airborne) throw new InvalidDataException($"행성 표: '{k}' 대기의 '{g}'가 기체가 아니다");
            double sum = p.Air.Values.Sum();
            if (p.KPa > 0 && Math.Abs(sum - 1) > 1e-6) throw new InvalidDataException($"행성 표: '{k}' 대기 비율 합이 1이 아니다 ({sum})");
        }

        t.gN2 = t.S("N2").Gas; t.gO2 = t.S("O2").Gas; t.gAr = t.S("Ar").Gas; t.gCO2 = t.S("CO2").Gas; t.gH2O = t.S("H2O").Gas;
        t.gCO = t.S("CO").Gas; t.gH2 = t.S("H2").Gas; t.gCH4 = t.S("CH4").Gas; t.gNH3 = t.S("NH3").Gas; t.gO3 = t.S("O3").Gas;
        t.gSoot = t.S("Soot").Gas; t.gAroma = t.S("Aroma").Gas; t.gHCl = t.S("HCl").Gas; t.gHCN = t.S("HCN").Gas; t.gHF = t.S("HF").Gas; t.gPF5 = t.S("PF5").Gas; t.gPOF3 = t.S("POF3").Gas;
        t.sWater = t.S("H2O_l").Store; t.sNH3l = t.S("NH3_l").Store; t.sSoot = t.S("C").Store; t.sPoly = t.S("CH2").Store;
        t.sCell = t.S("Cellulose").Store; t.sFood = t.S("Glucose").Store; t.sPvc = t.S("PVC").Store; t.sPu = t.S("PU").Store; t.sEC = t.S("EC").Store; t.sLiPF6 = t.S("LiPF6").Store; t.sLiF = t.S("LiF").Store;
        t.sSalt = t._subById.TryGetValue("NaCl", out var salt) ? salt.Store : -1;
        return t;
    }

    private static readonly Regex FormulaPart = new("([A-Z][a-z]?)([0-9]*)", RegexOptions.Compiled);

    private (int el, int n)[] ParseFormula(string f, string id)
    {
        var counts = new SortedDictionary<int, int>();
        int pos = 0;
        foreach (Match m in FormulaPart.Matches(f))
        {
            if (m.Index != pos) throw new InvalidDataException($"물질 표: '{id}'의 화학식 '{f}'을 읽지 못했다");
            pos = m.Index + m.Length;
            int el = Array.IndexOf(ElementSymbols, m.Groups[1].Value);
            if (el < 0) throw new InvalidDataException($"물질 표: '{id}'의 원소 '{m.Groups[1].Value}'가 원소 표에 없다");
            int n = m.Groups[2].Value.Length > 0 ? int.Parse(m.Groups[2].Value) : 1;
            counts[el] = counts.GetValueOrDefault(el) + n;
        }
        if (pos != f.Length || counts.Count == 0) throw new InvalidDataException($"물질 표: '{id}'의 화학식 '{f}'을 읽지 못했다");
        return counts.Select(kv => (kv.Key, kv.Value)).ToArray();
    }

    private static bool SameElements(Substance a, Substance b) => a.Elements.SequenceEqual(b.Elements);

    /// <summary>반응식의 원소가 안 맞으면 그 내용, 맞으면 null.</summary>
    public string? Imbalance(Reaction r)
    {
        var d = new double[ElementSymbols.Length];
        foreach (var x in r.In) foreach (var (el, n) in Substances[x.S].Elements) d[el] += x.Nu * n;
        foreach (var x in r.Out) foreach (var (el, n) in Substances[x.S].Elements) d[el] -= x.Nu * n;
        var bad = d.Select((v, i) => (v, i)).Where(p => Math.Abs(p.v) > 1e-9).Select(p => $"{ElementSymbols[p.i]} {p.v:+0.##;-0.##}").ToList();
        return bad.Count == 0 ? null : string.Join(", ", bad);
    }

    /// <summary>물질별 몰 수 → 원소별 몰 수.</summary>
    public double[] ElementsOf(ReadOnlySpan<double> moles)
    {
        var e = new double[ElementSymbols.Length];
        for (int s = 0; s < moles.Length; s++)
        {
            if (moles[s] == 0) continue;
            foreach (var (el, n) in Substances[s].Elements) e[el] += moles[s] * n;
        }
        return e;
    }
}
