using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>몸 하나 — 사람 · 동물. 위치(칸)와 하는 일의 세기(W)는 ⑧ 마음이 정하고, 나머지는 칸에서 받는 것으로 바뀐다.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Body
{
    public int Cell;
    public byte Alive, Conscious, Asleep, Suit;   // 우주복: 바깥 공기 · 열 · 독을 막는다 (산소는 우주복 탱크)
    public float ActivityW;    // 대사량 W (쉬기 80 · 일 250 · 힘든 일 500)
    public double Reserve;     // 몸의 탄수화물 (글리코겐 · 핏속 포도당 — 양분 몰 · 1몰 180g · 2.8MJ) — 힘든 일에 주로 태운다
    public double Water;       // 몸의 물 (몰)
    public float SpO2;         // 산소 포화도 0~1 (일산화탄소 · 폐 손상 반영)
    public float Hypoxia;      // 산소 부족이 쌓인 것 (0.5 넘으면 쓰러짐 · 3이면 죽음)
    public float COHb;         // 일산화탄소 헤모글로빈 비율
    public float LungDamage;   // 폐 손상 (암모니아 · 오존 · 연기 · 곰팡이 포자)
    public double CoreK;       // 체온 (K)
    public float Burn, Blunt, Barotrauma, Frost;
    public float Bleed;        // 출혈 (L/시)
    public float BloodL;       // 피 (L)
    public float RadGy, RadRecent;
    public float Pain;
    public float SuitO2;       // 우주복 산소 (몰)
    public float LastKPa;
    public int DeathCause;
    public long DiedAt;
    public int HurtCause;
    public double Fat;         // 몸의 지방 (팔미트산 몰 — 1몰 256g · 10MJ) — 쉴 때 주로 태운다
    public double Bladder;     // 방광의 물 (몰) — 0.4L 넘으면 비운다 (오수로)
}

/// <summary>재배대의 작물.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Crop
{
    public int Device;     // 망의 장치 번호
    public int Cell;
    public float Biomass;  // 거둘 수 있는 양분 (몰)
    public float Health;   // 0~1
    public float DarkHours;
}

/// <summary>
/// ⑦ 생명 — 몸 · 작물 · 곰팡이 · 음식 상함 (docs/ENGINE.md §4 ⑦). 5틱(12초)마다.
/// <para>
/// <b>숨</b>: 그 칸 공기에서 산소를 빨고(대사량 ÷ 467kJ/몰) 이산화탄소 · 수증기를 내놓는다 (반응식 '숨' — 몸의 양분이 탄다).
/// 산소 포화도는 산소 분압의 힐 곡선(P50=7kPa) × (1−일산화탄소 헤모글로빈) × (1−폐 손상). 낮으면 산소 부족이 쌓여 쓰러지고 죽는다.
/// 진공(6.3kPa 아래)에서는 체액이 끓어 15초면 쓰러지고 1분 반이면 죽는다. 우주복을 입으면 우주복 산소로.
/// </para>
/// <para>
/// <b>체온</b>: 대사열 · 떨림 · 땀 ↔ 칸 공기(대류 · 바람 · 엷은 공기) · 겉면(복사). 뜨거운 공기 · 불꽃 복사는 화상, 추위는 동상 · 저체온.
/// <b>상처는 받은 에너지에서</b>: 충격(④가 칸에 남긴 에너지) · 급감압(폐 손상) · 열 · 방사선(⑤의 칸 선량률) · 독(칸의 ppm).
/// </para>
/// <para>
/// <b>작물</b>: 빛(망의 재배등 · 칸 밝기) · 이산화탄소 · 온도 · 물탱크로 광합성 (반응식 그대로 — 칸에서 CO₂를 먹고 O₂를 낸다).
/// <b>곰팡이</b>: 습하고(상대 습도 80%↑) 따뜻한 칸에서 자라 포자가 폐를 상하게 한다. <b>음식 상함</b>: 따뜻하고 습한 칸의 양분이 썩는다 — 산소가 없으면 메탄이 된다.
/// </para>
/// 에너지 장부: 몸의 양분(생성 엔탈피) + 몸의 열(245kJ/K × 체온) + 몸의 물. 숨 · 땀 · 열 주고받기로 장의 장부와 합쳐 보존된다.
/// </summary>
public sealed class Life : IEngine
{
    public string Name => "생명";
    public Phase Phase => Phase.Life;
    public int Every => 5;
    public int Offset => 3;

    public const double BodyC = 245_000;     // J/K (70kg × 3.5kJ/kg·K)
    public const double SkinArea = 1.8;      // m²
    public const double BodyTemp = 310.15;

    private readonly Field F;
    private readonly Layout L;
    private readonly Tables T;
    private readonly Reaction _breathe, _photo, _rot, _ferment;
    private readonly Substance _glucose, _waterL, _fat;
    private Network? _net;

    // ── 대사 (NASA BVAD 2018 · Compendium of Physical Activities 2011) ──
    public const double GlycogenMax = 3.5;        // 글리코겐 한도 (몰 — 약 630g: 근육 500 · 간 100 + 핏속) — 넘치면 지방으로
    public const double Fat0 = 39;                // 처음 지방 (몰 — 약 10kg · 체지방 14%)
    public const double Urine0 = 83.3 / 86400;    // 오줌 (몰/초 — 하루 1.5L, 물이 넉넉하면 많이 · 모자라면 적게)
    public const double UrineRefWater = Mind.BodyWater0 * 0.995; // 이 물에서 하루 1.5L (목마름은 몸의 물이 0.5% 줄 때쯤 마시게 한다 — 그 언저리가 보통)
    public const double NightUrine = 0.5;          // 자는 동안 오줌 빠르기 (낮의 절반)
    public const double Insensible = 83.3 / 86400; // 숨 · 피부로 늘 잃는 물 (몰/초 — 하루 1.5L · 대사로 생긴 물 0.35L와 합쳐 BVAD 1.85kg)
    /// <summary>산소 1몰에서 나오는 에너지 (J · 물이 액체인 열량 — 식품 열량과 같은 기준): 포도당 467kJ · 지방 434kJ.</summary>
    public const double CropMolPerM2Day = 0.1;     // 작물 1m²가 하루에 내는 양분 (몰 — 밝은 재배등 · 알맞은 온도 · 이산화탄소)
    public const double GlucoseJPerO2 = 2803e3 / 6, FatJPerO2 = 9978e3 / 23;
    /// <summary>탄수화물로 태우는 몫 (산소 기준): 쉴 때 0.3(호흡 몫 0.79) → 힘들수록 → 813W에서 1(호흡 몫 1.0).</summary>
    public static double CarbShare(double w) => Math.Clamp(0.3 + 0.7 * (w - 80) / 730, 0.3, 1.0);
    // 장부 (재미 측정 · 시험): 지금까지 숨으로 쓴 산소 · 낸 이산화탄소 (몰) · 대사 에너지 (J) · 오줌 (몰)
    public double O2Used, CO2Made, MetabolicJ, UrineMade;

    public List<Body> Bodies = new();
    public List<Crop> Crops = new();
    public float[] Mold;
    public long Deaths;
    public double FoodRotted;   // 썩은 양분 (몰 — 장부)

    public Life(Field f)
    {
        F = f; L = f.L; T = f.T;
        _breathe = T.R("breathe"); _photo = T.R("photosynth"); _rot = T.R("rot"); _ferment = T.R("ferment");
        _glucose = T.S("Glucose"); _waterL = T.S("H2O_l"); _fat = T.S("Fat");
        Mold = new float[L.N];
    }

    // ───────────────────────── 요청 (⑧ 마음 · 시험) ─────────────────────────

    public int AddBody(int cell)
    {
        Bodies.Add(new Body
        {
            Cell = cell, Alive = 1, Conscious = 1, ActivityW = 90, Reserve = GlycogenMax * 0.8, Fat = Fat0, Water = 42 * 55.5, SpO2 = 0.97f,
            CoreK = BodyTemp, BloodL = 5, LastKPa = (float)F.Pressure(cell),
        });
        return Bodies.Count - 1;
    }

    public ref Body this[int i] => ref CollectionsMarshal.AsSpan(Bodies)[i];

    public void Move(int body, int cell) => this[body].Cell = cell;
    public void Eat(int body, double glucoseMol) => this[body].Reserve += glucoseMol;
    public void Drink(int body, double waterMol) => this[body].Water += waterMol;

    /// <summary>재배대마다 작물을 심는다 (망의 장치에서).</summary>
    public void Plant(Network net)
    {
        Crops.Clear();
        foreach (var d in net.Devices)
            if (d.Def.Role == "grow") Crops.Add(new Crop { Device = d.Id, Cell = d.Cell, Biomass = 0, Health = 1 });
    }

    // ───────────────────────── 한 걸음 ─────────────────────────

    public void Step(Sim sim, double dt)
    {
        var st = sim.Find<Structure>();
        var outside = sim.Find<Outside>();
        var chem = sim.Find<Chemistry>();
        _net ??= sim.Find<Network>();
        var span = CollectionsMarshal.AsSpan(Bodies);
        for (int b = 0; b < span.Length; b++)
        {
            ref var body = ref span[b];
            if (body.Alive == 0) continue;
            StepBody(sim, b, ref body, dt, st, outside, chem);
        }
        if (st != null) Array.Clear(st.ImpactJ);
        var net = sim.Find<Network>();
        if (net != null) Grow(sim, net, dt);
        if (sim.Tick % 125 == 3) { MoldStep(dt * 25); RotStep(sim, dt * 25); }
    }

    private HashSet<int>? _medBedCells; // 치료 침대 칸 (산소통 · 마스크 — 전원이 끊겨도 산소는 나온다)

    /// <summary>그 칸이 치료 침대인가.</summary>
    private bool OnTherapy(Sim sim, int cell)
    {
        _medBedCells ??= L.Furniture.Where(fu => fu.Type == "MedBed").SelectMany(fu => fu.Cells).ToHashSet();
        return _medBedCells.Contains(cell);
    }

    private void StepBody(Sim sim, int id, ref Body b, double dt, Structure? st, Outside? outside, Chemistry? chem)
    {
        int c = b.Cell;
        if (c < 0 || c >= L.N) return;
        double p = F.Pressure(c), tair = F.Temp(c);
        bool suit = b.Suit != 0;
        double pO2 = suit ? 21 : F.Partial(T.gO2, c);
        double pCO2 = suit ? 0.04 : F.Partial(T.gCO2, c);
        double co = suit ? 0 : F.Ppm(T.gCO, c), nh3 = suit ? 0 : F.Ppm(T.gNH3, c), o3 = suit ? 0 : F.Ppm(T.gO3, c), soot = suit ? 0 : F.Ppm(T.gSoot, c);
        int env = 0; // 이번에 몸을 해친 것의 원인
        string? harm = null;
        if (b.Conscious == 0) b.ActivityW = 80; // 의식 없는 몸은 기초 대사로 (쓰러지기 전 달리던 대사량이 남아 스스로 익지 않게)

        // ── 숨 ── (추우면 떨어서 대사가 는다)
        double shiver = b.Conscious != 0 ? Math.Clamp((309.75 - b.CoreK) * 300, 0, 400) : 0; // 36.6℃ 아래로 내려간 만큼 떤다 (0.1℃에 30W · 많아야 400W)
        double watts = b.ActivityW + shiver; // 잠의 대사량은 ⑧ 마음이 잠 77W로 준다 (여기서 또 줄이지 않는다)
        double fc = b.Reserve <= 0.01 ? 0 : b.Fat <= 0.01 ? 1 : CarbShare(watts); // 탄수화물 몫 (글리코겐이 바닥나면 지방만)
        double vo2 = watts / (fc * GlucoseJPerO2 + (1 - fc) * FatJPerO2) * dt; // 필요한 산소 (몰)
        double hill(double x) => Math.Pow(x, 2.7) / (Math.Pow(x, 2.7) + Math.Pow(7, 2.7));
        // 산소 요법: 치료 침대에 누우면 마스크로 거의 순수한 산소를 마신다 — 상한 폐로도 피에 산소가 들어간다
        bool therapy = !suit && p > 30 && OnTherapy(sim, c);
        double pO2e = therapy ? Math.Max(pO2, 0.9 * Math.Min(p, 101.3)) : pO2;
        double sat = hill(pO2e) * (1 - b.COHb) * (1 - Math.Min(0.9, b.LungDamage * (therapy ? 0.5 : 1)));
        b.SpO2 = (float)sat;
        double o2Got;
        if (suit) { o2Got = Math.Min(vo2, b.SuitO2); b.SuitO2 -= (float)o2Got; if (b.SuitO2 <= 0) b.SpO2 = 0; }
        else o2Got = p > 1 ? Math.Min(vo2 * Math.Min(1, sat / 0.95), F.Gas(T.gO2, c) * 0.5) : 0;
        // 숨: 포도당 + 6O₂ → 6CO₂ + 6H₂O · 지방(팔미트산) + 23O₂ → 16CO₂ + 16H₂O — 섞어 태운다 (호흡 몫 0.79 ~ 1.0)
        double xg = Math.Min(o2Got * fc / 6, b.Reserve), xf = Math.Min(o2Got * (1 - fc) / 23, b.Fat);
        if (xg + xf > 0)
        {
            double o2 = 6 * xg + 23 * xf, co2 = 6 * xg + 16 * xf, h2o = 6 * xg + 16 * xf;
            // 반응열은 몸으로 (장부: E(기체, T) = hf + cv(T−298))
            double q = xg * _glucose.Hf * 1000 + xf * _fat.Hf * 1000;
            if (!suit) { q += o2 * T.Gases[T.gO2].Energy(tair); F.RemoveGas(c, T.gO2, o2); sim.Book.Add(Res.O2, Flow.Breathed, -o2); }
            else q += o2 * T.Gases[T.gO2].Energy(BodyTemp);
            q -= co2 * T.Gases[T.gCO2].Energy(BodyTemp) + h2o * T.Gases[T.gH2O].Energy(BodyTemp);
            b.Reserve -= xg; b.Fat -= xf;
            if (!suit && p > 1) { F.AddGas(c, T.gCO2, co2, BodyTemp); F.AddGas(c, T.gH2O, h2o, BodyTemp); }
            b.CoreK += q / BodyC;
            O2Used += o2; CO2Made += co2; MetabolicJ += xg * GlucoseJPerO2 * 6 + xf * FatJPerO2 * 23;
        }
        // 남는 탄수화물은 지방으로 (4 포도당 + O₂ → 팔미트산 + 8CO₂ + 8H₂O · 3시간에 걸쳐 — 열이 조금 난다)
        if (b.Reserve > GlycogenMax && !suit && p > 1)
        {
            double k = Math.Min((b.Reserve - GlycogenMax) / 4 * dt / (3 * 3600), F.Gas(T.gO2, c) * 0.1);
            if (k > 0)
            {
                double q = 4 * k * _glucose.Hf * 1000 + k * T.Gases[T.gO2].Energy(tair) - k * _fat.Hf * 1000 - 8 * k * T.Gases[T.gCO2].Energy(BodyTemp) - 8 * k * T.Gases[T.gH2O].Energy(BodyTemp);
                F.RemoveGas(c, T.gO2, k); F.AddGas(c, T.gCO2, 8 * k, BodyTemp); F.AddGas(c, T.gH2O, 8 * k, BodyTemp); sim.Book.Add(Res.O2, Flow.Breathed, -k);
                b.Reserve -= 4 * k; b.Fat += k;
                b.CoreK += q / BodyC;
                O2Used += k; CO2Made += 8 * k;
            }
        }
        // 산소 부족
        bool vacuum = !suit && p < 6.3;
        if (vacuum) { b.Hypoxia += (float)(dt / 30); env = EnvCause(sim, c, st, chem); harm = "진공에서 숨을 못 쉬었다"; }
        else if (b.SpO2 < 0.75) { b.Hypoxia += (float)((0.75 - b.SpO2) / 0.75 * dt / 60); env = EnvCause(sim, c, st, chem); harm = b.COHb > 0.3 ? "일산화탄소에 중독됐다" : "산소가 모자랐다"; }
        else b.Hypoxia = (float)Math.Max(0, b.Hypoxia - dt / 600);
        // 일산화탄소 헤모글로빈 → 평형 1−e^(−ppm/1000), 시간 상수 4시간 × (80W/대사량)
        double eq = 1 - Math.Exp(-co / 1000);
        double tau = 4 * 3600 * 80 / Math.Max(60, b.ActivityW) / (therapy ? 4 : 1); // 순수한 산소는 일산화탄소를 네 배 빨리 밀어낸다
        b.COHb += (float)((eq - b.COHb) * (1 - Math.Exp(-dt / tau)));
        // 이산화탄소 · 독
        if (pCO2 > 7) { b.Hypoxia += (float)((pCO2 - 7) / 5 * dt / 60); harm ??= "이산화탄소가 너무 짙었다"; env = env == 0 ? EnvCause(sim, c, st, chem) : env; }
        if (pCO2 > 3) b.Pain = (float)Math.Min(1, b.Pain + (pCO2 - 3) / 40 * dt / 60);
        double lung = 0;
        // 암모니아: 300ppm은 한 시간 넘게 버티고 · 3000ppm은 30분이면 위태롭고 · 1만ppm은 몇 분 (분마다 폐 손상 0.0167 × (ppm/3000)^1.3)
        if (nh3 > 100) lung += 0.0167 * Math.Pow(nh3 / 3000, 1.3);
        if (o3 > 5) lung += o3 / 200;
        if (soot > 2000) lung += soot / 200_000;
        if (Mold[c] > 0.3f) lung += 0.002;
        if (lung > 0) { b.LungDamage += (float)(lung * dt / 60); b.Pain = (float)Math.Min(1, b.Pain + lung * dt / 120); harm ??= nh3 > 100 ? "암모니아를 마셨다" : o3 > 5 ? "오존을 마셨다" : "연기를 마셨다"; env = env == 0 ? EnvCause(sim, c, st, chem) : env; }
        else b.LungDamage = (float)Math.Max(0, b.LungDamage - dt / (14 * 86400));

        // ── 체온 ──
        double tsurf = F.HasSurface(c) ? F.Tf[c] : F.Ts[c];
        double wind = Math.Sqrt(F.WindX[c] * F.WindX[c] + F.WindY[c] * F.WindY[c]);
        double thin = Math.Sqrt(Math.Clamp(p / 101.3, 0, 2));
        // 체온 조절 (ISO 7730 · Gagge 두 마디를 가볍게): 피부 온도는 혈관이 조였다 풀었다 (추우면 낮게 · 더우면 높게) ·
        // 마른 열은 '옷의 저항 + 겉 공기층의 저항'을 지나 공기(대류)와 둘레 겉면(복사)으로 — 옷 1clo · 침대 이불 2.5clo (1clo = 0.155 m²K/W)
        double coreC = b.CoreK - 273.15;
        double skin = 273.15 + Math.Clamp(33.7 - 4 * Math.Max(0, 36.8 - coreC) + 2 * Math.Max(0, coreC - 37.0), 28, 36);
        double hc = (3 + 4 * Math.Pow(wind, 0.6)) * thin;
        double hr = 4 * 0.95 * Field.Sigma * 0.7 * Math.Pow((skin + tsurf) / 2, 3);   // 복사를 곧게 편 것 (W/m²K)
        bool inBed = b.Asleep != 0 && L.FurnitureAt[c] >= 0 && L.Furniture[L.FurnitureAt[c]].Type == "Bed";
        double icl = suit ? 1.2 : inBed ? 2.5 * 0.155 : 0.155;                            // 우주복은 열을 거의 막는다 (냉각 속옷이 뺀다)
        double h = hc + hr;
        double top = h > 0 ? (hc * tair + hr * tsurf) / h : tair;                           // 느끼는 온도 (공기 · 겉면 평균)
        double dry = (skin - top) * SkinArea / (icl + 1 / Math.Max(0.5, h));                // W (+ 몸이 잃음)
        double conv = h > 0 ? dry * hc / h : 0, rad = h > 0 ? dry * hr / h : 0;
        double sweat = 0;
        if (b.CoreK > 310.4 && !suit && p > 6.3)
        {
            // 땀: 몸이 내려는 만큼(심부 37.25℃ 넘은 만큼) — 마를 수 있는 한도까지 (ISO 7933: 피부 포화 수증기압 − 공기 수증기압 ÷ 증발 저항(옷 + 공기층 · 루이스 16.5))
            double demand = Math.Min(600, (b.CoreK - 310.4) * 400);
            double re = icl / (16.5 * 0.38) + 1 / (16.5 * Math.Max(0.5, hc));
            double emax = Math.Max(0, Water.SatKPa(skin) - F.Partial(T.gH2O, c)) * SkinArea / re;
            sweat = Math.Min(demand, emax);
        }
        // 우주복 냉각 속옷: 우주복이 돌면(산소가 남아 있으면) 몸의 남는 열을 물 순환으로 빼 겉으로 버린다 — 땀이 마르지 않는 우주복 속에서 익지 않게
        double suitCool = suit && b.SuitO2 > 0 && b.CoreK > 310.2 ? Math.Min(400, (b.CoreK - 310.2) * 400) : 0;
        double outW = conv + rad + sweat + suitCool;
        b.CoreK -= outW * dt / BodyC;
        F.AddHeat(c, (conv + suitCool) * dt);          // 공기가 거의 없으면 장이 바닥으로 돌린다
        F.AddSurfaceHeat(c, rad * dt);
        if (!suit && p > 1)
        {
            if (sweat > 0)
            {
                // 땀: 몸의 물 → 칸의 수증기 (증발열은 몸이 냄 — 장부: 액체 hf → 기체 hf + cv(T−298))
                double n = sweat * dt / ((T.Gases[T.gH2O].Hf - _waterL.Hf) * 1000);
                n = Math.Min(n, b.Water * 0.01);
                double need = n * ((T.Gases[T.gH2O].Hf - _waterL.Hf) * 1000 + T.Gases[T.gH2O].C * (BodyTemp - 298.15));
                b.Water -= n;
                F.AddGas(c, T.gH2O, n, BodyTemp);
                // sweat W는 이미 체온에서 뺐다 — 장부를 맞추는 차이만 몸에서
                b.CoreK -= (need - sweat * dt) / BodyC;
            }
        }
        // 콩팥: 물이 넉넉하면 오줌을 많이 · 모자라면 적게 (하루 0.5 ~ 4.5L) — 방광이 차면 비운다 (오수로 — 망이 있으면)
        // 자는 동안은 절반 (밤의 항이뇨 호르몬 — 건강한 어른의 밤 오줌은 하루의 20% 아래: ICS 2002)
        double urine = Math.Min(b.Water * 0.001, Urine0 * Math.Clamp(1 + (b.Water - UrineRefWater) / (0.01 * Mind.BodyWater0), 0.33, 3) * (b.Asleep != 0 ? NightUrine : 1) * dt);
        b.Water -= urine; b.Bladder += urine; UrineMade += urine;
        if (b.Bladder > 22 && _net != null && b.Conscious != 0) { _net.WasteWater += b.Bladder; b.Bladder = 0; }
        // 늘 조금씩 물을 잃는다 (숨 · 피부 — 하루 1.5L)
        double insens = Insensible * dt;
        b.Water -= insens;
        if (!suit && p > 1) { F.AddGas(c, T.gH2O, insens, BodyTemp); b.CoreK -= insens * ((T.Gases[T.gH2O].Hf - _waterL.Hf) * 1000 + T.Gases[T.gH2O].C * (BodyTemp - 298.15)) / BodyC; }

        // ── 화상 · 동상 ──
        if (!suit && tair > 330) { b.Burn += (float)((tair - 330) / 100 * dt / 60); harm ??= "뜨거운 공기에 데었다"; env = env == 0 ? EnvCause(sim, c, st, chem) : env; }
        if (chem != null)
        {
            double flame = chem.Hrr[c];
            Span<int> nb = stackalloc int[4];
            L.Neighbors(c, nb);
            foreach (int j in nb) if (j >= 0) flame += chem.Hrr[j] * 0.4;
            if (flame > 20) { b.Burn += (float)(flame / 200 * dt / 30 * (suit ? 0.2 : 1)); harm ??= "불길에 데었다"; env = env == 0 ? (chem.FireCause[c] != 0 ? chem.FireCause[c] : EnvCause(sim, c, st, chem)) : env; }
        }
        if (!suit && tair < 250 && p > 1) b.Frost += (float)((250 - tair) / 100 * dt / 600);

        // ── 압력 · 충격 · 방사선 ──
        double dp = b.LastKPa - p;
        if (!suit && dp > 40) { b.Barotrauma += (float)(dp / 150); b.LungDamage += (float)(dp / 400); harm ??= "급감압에 폐를 다쳤다"; env = env == 0 ? EnvCause(sim, c, st, chem) : env; }
        b.LastKPa = (float)p;
        if (st != null && st.ImpactJ[c] > 0)
        {
            double e = st.ImpactJ[c];
            b.Blunt += (float)(e / 8000);
            b.Bleed += (float)(e / 4000);
            harm ??= "파편 · 충격에 맞았다";
            env = env == 0 ? st.DamageCause[c] : env;
        }
        if (outside != null)
        {
            double gy = outside.Dose[c] / 1000.0 * dt / 3600;
            b.RadGy += (float)gy; b.RadRecent = (float)(b.RadRecent * Math.Exp(-dt / (2 * 86400)) + gy);
        }

        // ── 피 · 물 · 양분 ──
        b.BloodL -= (float)(b.Bleed * dt / 3600);
        b.Bleed = (float)(b.Bleed * Math.Exp(-dt / 1800)); // 굳는다
        if (b.Burn > 0.2) b.Water -= (b.Burn * 55.5 * dt / 3600); // 넓은 화상은 물을 잃는다

        // ── 의식 · 죽음 ──
        double pain = Math.Min(1, b.Pain + b.Burn * 0.6 + b.Blunt * 0.5 + b.Barotrauma * 0.4);
        b.Pain = (float)Math.Max(0, b.Pain - dt / 1800);
        bool down = b.Hypoxia > 0.5 || b.COHb > 0.5 || pCO2 > 7 || b.CoreK < 305 || b.CoreK > 313 || b.BloodL < 3.5 || pain > 0.95 || b.RadRecent > 6;
        if (down && b.Conscious != 0)
        {
            b.Conscious = 0;
            int cause = sim.Causes.Add(CauseKind.Biology, harm ?? "쓰러졌다", sim.Tick, c, env);
            b.HurtCause = cause;
            sim.Facts.Emit(sim.Tick, FactKind.Collapsed, c, (float)b.Hypoxia, cause, actor: id);
        }
        else if (!down && b.Conscious == 0 && b.Hypoxia < 0.3 && b.CoreK < 312.15) b.Conscious = 1; // 열로 쓰러지면 39℃ 아래로 식어야 깬다
        string? death = b.Hypoxia > 3 ? harm ?? "숨을 못 쉬었다"
            : b.CoreK < 301 ? "얼어 죽었다" : b.CoreK > 316 ? "열사병" : b.BloodL < 2.5 ? "피를 너무 흘렸다"
            : b.Burn > 1.2 ? "화상" : b.Blunt > 1.5 ? "크게 다쳤다" : b.LungDamage > 1.2 ? "폐가 상했다"
            : b.Water < 42 * 55.5 * 0.82 ? "목이 말라" : b.Reserve <= 0.01 && b.Fat <= 0.01 && b.CoreK < 306 ? "굶어서" : b.RadRecent > 9 ? "방사선" : null;
        if (death != null)
        {
            b.Alive = 0; b.Conscious = 0; b.DiedAt = sim.Tick;
            int cause = sim.Causes.Add(CauseKind.Biology, $"죽었다 — {death}", sim.Tick, c, env != 0 ? env : b.HurtCause);
            b.DeathCause = cause;
            Deaths++;
            sim.Facts.Emit(sim.Tick, FactKind.Died, c, 0, cause, actor: id);
        }
        else if (harm != null && b.HurtCause == 0)
        {
            b.HurtCause = sim.Causes.Add(CauseKind.Biology, harm, sim.Tick, c, env);
            sim.Facts.Emit(sim.Tick, FactKind.Hurt, c, 0, b.HurtCause, actor: id);
        }
    }

    /// <summary>이 칸을 해친 것의 원인: 구역이 바깥과 이어진 구멍 → 그 벽의 원인 · 불 → 불의 원인.</summary>
    private int EnvCause(Sim sim, int c, Structure? st, Chemistry? chem)
    {
        if (chem != null && chem.FireCause[c] != 0 && chem.Burning[c] != 0) return chem.FireCause[c];
        int r = F.RegionOf.Length > c ? F.RegionOf[c] : -1;
        if (st != null)
        {
            if (r >= 0) foreach (var k in F.Links) if (k.RA == r && k.RB < 0 && st.DamageCause[k.Via] != 0) return st.DamageCause[k.Via];
            if (F.Hole[c] > 0 && st.DamageCause[c] != 0) return st.DamageCause[c];
        }
        if (chem != null && r >= 0)
            foreach (int cell in F.Regions[r]) if (chem.Burning[cell] != 0 && chem.FireCause[cell] != 0) return chem.FireCause[cell];
        return 0;
    }

    /// <summary>작물: 빛 × 이산화탄소 × 온도 × 물 × 건강 — 광합성 반응식대로 (칸 CO₂ → O₂ · 물탱크의 물).</summary>
    private void Grow(Sim sim, Network net, double dt)
    {
        var light = sim.Find<Light>();
        var span = CollectionsMarshal.AsSpan(Crops);
        for (int k = 0; k < span.Length; k++)
        {
            ref var cr = ref span[k];
            int c = cr.Cell;
            double lamp = net.Supply[cr.Device] > 0.5f && net.On[cr.Device] != 0 ? 1 : Math.Min(1, (light?.Lux[c] ?? 0) / 3000); // 방 조명(300lx)은 재배등의 10분의 1
            double t = F.Temp(c);
            double tf = Math.Max(0, 1 - Math.Pow((t - 296) / 12, 2));
            double pco2 = F.Partial(T.gCO2, c);
            double cf = pco2 / (pco2 + 0.03);
            double water = net.WaterTank > 100 ? 1 : 0;
            if (lamp < 0.1) cr.DarkHours += (float)(dt / 3600); else cr.DarkHours = 0;
            if (t < 275 || t > 318 || cr.DarkHours > 48 || water == 0) cr.Health = (float)Math.Max(0, cr.Health - dt / 86400);
            else cr.Health = (float)Math.Min(1, cr.Health + dt / (5 * 86400));
            // 양분 몰/초: 재배대 1m²에 하루 0.1몰 (먹는 부분 마른 무게 하루 18g/m² — NASA 생물 재생 실험(BPC)의 밀 · 감자 20g/m² 안팎 · 상추 7g/m²)
            double rate = CropMolPerM2Day / 86400 * L.Furniture[net.Devices[cr.Device].Furniture].Cells.Length * Field.CellFloor * lamp * tf * cf * water * cr.Health;
            double xi = rate * dt;
            xi = Math.Min(xi, F.Gas(T.gCO2, c) * 0.5 / 6);
            if (xi <= 0) continue;
            F.RemoveGas(c, T.gCO2, 6 * xi);
            net.WaterTank -= 6 * xi; sim.Book.Add(Res.Water, Flow.Watered, -6 * xi);
            F.AddGas(c, T.gO2, 6 * xi, t); sim.Book.Add(Res.O2, Flow.Photosynthesis, 6 * xi);
            cr.Biomass += (float)xi;
            // 빛 에너지가 반응열을 대므로 장부 밖 (재배등 전력이 그 몫) — 장의 열은 바꾸지 않는다
        }
    }

    /// <summary>곰팡이 (125틱마다): 상대 습도 80%↑ · 278~310K에서 자라고, 마르면 준다.</summary>
    private void MoldStep(double dt)
    {
        for (int c = 0; c < L.N; c++)
        {
            if (!L.IsSpace(c) || F.RegionOf.Length <= c || F.RegionOf[c] < 0) { Mold[c] = 0; continue; }
            double t = F.Temp(c);
            double rh = F.Partial(T.gH2O, c) / Math.Max(1e-6, Water.SatKPa(Math.Max(250, t)));
            bool wet = rh > 0.8 || F.Stored(T.sWater, c) > 50;
            if (wet && t > 278 && t < 310) Mold[c] = (float)Math.Min(1, Mold[c] + Math.Max(Mold[c], 0.01) * 0.7 * dt / 86400);
            else Mold[c] = (float)Math.Max(0, Mold[c] - 0.2 * dt / 86400);
        }
    }

    /// <summary>음식 상함 (125틱마다): 따뜻하고 습한 칸의 양분이 썩는다 (하루 1% · 10K마다 두 배) — 산소가 거의 없으면 메탄 발효.</summary>
    private void RotStep(Sim sim, double dt)
    {
        for (int c = 0; c < L.N; c++)
        {
            double food = F.Stored(T.sFood, c);
            if (food < 1e-3 || F.RegionOf.Length <= c || F.RegionOf[c] < 0) continue;
            double t = F.Temp(c);
            if (t < 277) continue; // 냉장고
            double rh = F.Partial(T.gH2O, c) / Math.Max(1e-6, Water.SatKPa(Math.Max(250, t)));
            if (rh < 0.5 && Mold[c] < 0.1) continue;
            double rate = 0.01 / 86400 * Math.Pow(2, (t - 293) / 10) * (1 + 3 * Mold[c]);
            double xi = food * rate * dt;
            bool anaerobic = F.Partial(T.gO2, c) < 2;
            var r = anaerobic ? _ferment : _rot;
            if (!anaerobic) xi = Math.Min(xi, F.Gas(T.gO2, c) * 0.3 / 6);
            if (xi <= 1e-9) continue;
            double q = r.Heat(T, t) * xi;
            F.TakeStore(c, T.sFood, xi);
            FoodRotted += xi; sim.Book.Add(Res.Food, Flow.Rotted, -xi);
            foreach (var x in r.In) { var s = T.Substances[x.S]; if (s.Gas >= 0) { F.RemoveGas(c, s.Gas, x.Nu * xi); if (s.Gas == T.gO2) sim.Book.Add(Res.O2, Flow.Rotted, -x.Nu * xi); } }
            foreach (var x in r.Out) { var s = T.Substances[x.S]; if (s.Gas >= 0) F.AddGas(c, s.Gas, x.Nu * xi, t); }
            F.AddHeat(c, q);
        }
    }

    /// <summary>에너지 장부: 몸의 양분 · 물(생성 엔탈피) + 몸의 열.</summary>
    public double EnergyLedger()
    {
        double e = 0;
        foreach (var b in Bodies) e += b.Reserve * _glucose.Hf * 1000 + b.Fat * _fat.Hf * 1000 + (b.Water + b.Bladder) * _waterL.Hf * 1000 + BodyC * b.CoreK;
        return e;
    }

    /// <summary>원소 장부용: 몸의 양분 · 물(방광 포함) · 지방 (몰).</summary>
    public (double glucose, double water, double fat) BodyMatter()
    {
        double g = 0, w = 0, f = 0;
        foreach (var b in Bodies) { g += b.Reserve; w += b.Water + b.Bladder; f += b.Fat; }
        return (g, w, f);
    }

    public void Save(StateWriter w) { w.List(Bodies); w.List(Crops); w.Arr(Mold); w.I64(Deaths); w.F64(FoodRotted); w.F64(O2Used); w.F64(CO2Made); w.F64(MetabolicJ); w.F64(UrineMade); }
    public void Load(StateReader r) { Bodies = r.List<Body>(); Crops = r.List<Crop>(); r.ArrInto(Mold); Deaths = r.I64(); FoodRotted = r.F64(); O2Used = r.F64(); CO2Made = r.F64(); MetabolicJ = r.F64(); UrineMade = r.F64(); }
}
