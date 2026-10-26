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
    public float CN;           // 시안화수소를 마신 양 (Purser FED — 1이면 쓰러지고 3이면 숨진다 · 간이 1시간 반감으로 푼다)
    public float LungDamage;   // 폐 손상 (암모니아 · 오존 · 연기 · 곰팡이 포자)
    public double CoreK;       // 체온 (K)
    public float Burn, Blunt, Barotrauma, Frost;
    public float Bleed;        // 출혈 (L/시)
    public float BloodL;       // 피 (L)
    public float RadGy, RadRecent;
    public float Pain;
    public float FaceCO2;      // 무중력에서 얼굴 앞에 고인 날숨 (이산화탄소 kPa — 방 공기 위에 더한다 · 공조 바람 · 움직임이 걷는다)
    public float BoneLoss, MuscleLoss;   // 무중력에서 잃은 뼈 · 근육 (몫 — 0 그대로 · 뼈 달마다 1.25% · 근육 주마다 1.5%)
    public float Nausea;       // 우주 멀미 (0 ~ 1 — ⑧이 무중력에서 지낸 시간으로 정한다)
    public float SuitO2;       // 우주복 산소 (몰)
    public float MaskO2;       // 산소 마스크 병의 산소 (몰 — 기압이 남은 곳에서만 쓸모가 있다)
    public byte Mask;          // 산소 마스크를 썼다
    public byte Low;           // ③ 국소 현상: 머리가 낮다 (기어감 · 누움 · 잠 · 쓰러짐 — 칸의 아래 층을 마신다 · 마음이 요청한다)
    public float LastKPa;
    public int DeathCause;
    public long DiedAt;
    public int HurtCause;
    public double Fat;         // 몸의 지방 (팔미트산 몰 — 1몰 256g · 10MJ) — 쉴 때 주로 태운다
    public double Bladder;     // 방광의 물 (몰) — 비우는 것은 사람이 변기에서 (판단) · 0.6L를 넘으면 참지 못하고 지린다 (월드)
    // 화장실 · 샤워실 (함장 요청 — docs/WASH.md)
    public double Bowel;       // 장에 찬 찌꺼기 (kg — 먹은 것에서 · 0.45kg를 넘으면 참지 못한다)
    public float Dirt;         // 몸의 더러움 (0 깨끗 · 1 몹시 — 날마다 · 땀 · 피 · 지림 · 씻으면 준다)
    public byte Wet;           // 지렸다 (1 — 씻을 때까지 옷이 젖어 있다)
    public int SoilCause;      // 마지막으로 지린 원인 마디
    // ⑦ 돌봄 · 감염 · 옮는 병 (깊이 바퀴 2)
    public float Dressed;      // 상처를 덮어 둔 남은 시간 (시간 — 붕대 · 화상 덮개)
    public float Infect;       // 상처 감염 (0 없음 · 1 넘으면 패혈증 — 쓰러진다 · 2면 죽는다)
    public float Abx;          // 항생제가 남은 시간 (시간)
    public byte Ill;           // 옮는 병: 0 안 걸림 · 1 잠복 · 2 앓음 · 3 나음(면역)
    public float IllH;         // 그 단계에 든 지 (시간)
    public int IllCause;       // 병의 원인 마디 (옮긴 이 쪽으로 이어진다)
    public int InfectCause;    // 곪음의 원인 마디
    // ⑦ 장기 · 심정지 (깊이 바퀴 3)
    public float Brain, BrainPeak;   // 뇌 손상 (0.5면 깨어나지 못하고 · 1이면 죽는다) · 가장 컸던 것 (그 40%는 낫지 않는다)
    public float Heart, HeartPeak;   // 심장 손상 · 가장 컸던 것 (그 50%는 낫지 않는다)
    public float LungScar;           // 폐 흉터 — 폐 손상이 이 아래로 낫지 않는다
    public byte Arrest;              // 심정지: 0 뛴다 · 1 잔떨림(제세동이 듣는다) · 2 멎음
    public float NoFlow, LowFlow;    // 멎은 뒤 압박 없이 · 압박을 받으며 지난 분
    public long CprAt, ShockAt;      // 마지막으로 가슴 압박을 받은 틱 · 마지막 제세동 틱
    public int ArrestCause;          // 심정지의 원인 마디
    // ⑦ 약 (깊이 바퀴 3 (나))
    public float Opioid, OpioidTol, OpioidDep;   // 진통제가 몸에 남은 양 (mg) · 내성 · 의존
    public float Apap, ApapDay, Liver, LiverPeak; // 해열 진통제 (mg) · 24시간치 (mg) · 간 손상 · 가장 컸던 것
    public float Antidote;                       // 해독제가 남은 시간 (시간)
    public float AbxCourse;                      // 이번 항생제를 먹은 시간 (시간)
    public byte Resist;                          // 내성균 (다 먹지 않아 남은 곪음)
    public long EpiAt, OpioidAt;                 // 마지막 아드레날린 · 마지막 진통제 (틱)
    public int DrugCause;                        // 마지막으로 약을 준 원인 마디
    // ⑦ (라) 숨은 병 둘
    public float BleedIn;                        // 속 출혈 (L/시 — 겉으로 안 보인다 · 0.05 아래면 저절로 멎는다)
    public float Appendix;                       // 맹장염 (0 없음 · 1이면 터진다 · −1 떼어 냈다)
    public int AppendixCause;                    // 맹장염의 원인 마디
    // ⑦ (라-2) 재생 · 인공 장기 · 이식
    public float Regen;                          // 재생 젤이 남은 시간 (치료 침대에 누운 시간만 줄어든다)
    public byte Implant;                         // 1 인공 심장 · 2 인공 폐 · 4 이식한 심장 · 8 이식한 간 · 16 이식한 폐 / 숨진 이: 32 심장 · 64 간 · 128 폐를 내주었다
    // ⑧ 수면제 (기억 · 정신 건강 (다))
    public float Hypnotic, HypnoticTol;          // 수면제가 몸에 남은 양 (mg) · 내성 (끊으면 그만큼 잠이 얕다)
    public long HypnoticAt;                      // 마지막 수면제 (틱)
    public float HearShift;                      // 일시 청력 손실 (dB — 들리는 문턱이 오른 만큼 · 소리 단계 S3)
    public int HearCause;                        // 귀를 먹먹하게 한 소리의 원인 마디
    // 5단 화장실 (라)(마) · 일상 물리 (docs/STAGE5.md)
    public float HandGerm;                       // 손에 묻은 장염 균 (몫 — 만지면 겉면과 맞바꾼다 · 씻으면 준다)
    public int HandCause;                        // 손 균의 원인 마디
    public byte Gut;                             // 배탈: 0 안 걸림 · 1 잠복 · 2 앓음 · 3 나음
    public float GutHours;                       // 그 단계에 든 지 (시간)
    public int GutCause;                         // 배탈의 원인 마디
    public float Spore;                          // 마시는 곰팡이 포자 (그 방 몫 — 마스크 · 우주복이면 0)
    public int PropId;                           // ④ 제 힘을 잃은 동안의 물체 Id (0 없음 — docs/PHYSICS.md PH14)
    public float Groggy;                         // 잠기운 0~1 (자는 동안 잔 만큼 차오르고 · 깨면 분 단위로 풀린다 — 깊이 고침 DF4)
    public float Spent;                          // 기운 없음 0~1 (멎은 심장이 다시 뛴 뒤 · 몇 시간에 걸쳐 풀린다 — DF4)
    public long SleptFrom;                       // 잠든 틱 + 1 (0: 깨어 있다)
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
public sealed partial class Life : IEngine
{
    public string Name => "생명";
    public Phase Phase => Phase.Life;
    public int Every => 5;
    public int Offset => 3;

    public const double BodyC = 245_000;     // J/K (70kg × 3.5kJ/kg·K)
    public const double SkinArea = 1.8;      // m²
    public const double BodyTemp = 310.15;
    /// <summary>무중력에서 가만히 있는 이의 얼굴 앞에 고이는 날숨 (이산화탄소 kPa · 많아야) · 두통이 나는 들이쉬는 이산화탄소 (kPa — 1.5%).</summary>
    public const double FacePocketKPa = 2.0, HeadacheKPa = 1.5;
    /// <summary>무중력의 몸 (NASA 장기 체류 자료): 근육은 주마다 1.5% · 뼈(엉덩이 · 다리)는 달마다 1.25% 준다 · 운동(달리기틀 · 맨몸 — 힘든 일 대사) 한 시간은 그 15배를 되찾는다 (하루 45분이면 반쯤 늦춘다).</summary>
    public const double MuscleLossPerWeek = 0.015, BoneLossPerMonth = 0.0125, ExerciseGain = 15;

    /// <summary>
    /// 무중력의 몸 (바퀴 3 인공 중력 (마)): 0.3g 아래에서 근육 · 뼈를 잃고 운동하는 동안 되찾는다 · 중력이 있으면 천천히 돌아온다 (근육 두 배 빠르기 · 뼈는 잃은 것보다 훨씬 더디게).
    /// </summary>
    public static void Decondition(ref Body b, float g, double dt)
    {
        double h = dt / 3600, mr = MuscleLossPerWeek / 168, br = BoneLossPerMonth / 720;
        bool loading = b.ActivityW >= 400;   // 힘든 운동 (운동 걸음 487W)
        if (g < Field.LowG)
        {
            b.MuscleLoss = (float)Math.Clamp(b.MuscleLoss + (loading ? -ExerciseGain : 1) * mr * h, 0, 0.4);
            b.BoneLoss = (float)Math.Clamp(b.BoneLoss + (loading ? -ExerciseGain : 1) * br * h, 0, 0.3);
        }
        else if (b.MuscleLoss > 0 || b.BoneLoss > 0)
        {
            b.MuscleLoss = (float)Math.Max(0, b.MuscleLoss - 2 * mr * Math.Min(1, g) * h * (loading ? ExerciseGain : 1));
            b.BoneLoss = (float)Math.Max(0, b.BoneLoss - 0.3 * br * Math.Min(1, g) * h * (loading ? ExerciseGain : 1));
        }
    }

    /// <summary>근육이 약해진 정도 (0 ~ 1 — 근육 20%를 잃으면 1): 중력에서 걸음 · 내려앉음이 서툴다.</summary>
    public static double Weakness(in Body b) => Math.Clamp(b.MuscleLoss / 0.2, 0, 1);

    /// <summary>잠기운이 가득 차는 잠 (분) · 깬 뒤 풀리는 시간 상수 (초) · 되살아난 뒤 기운 없음의 시간 상수 (시간).</summary>
    public const double GroggyFullMin = 30, GroggyTauSec = 90, SpentTauHours = 3;

    /// <summary>
    /// 잠기운 (sleep inertia — Tassi &amp; Muzet 2000 · 깬 직후 1~3분이 가장 짙고 반응 · 손놀림이 30~50% 느리다 · 깊이 잤을수록 짙다):
    /// 자는 동안 잔 만큼 차오르고 (30분이면 가득 — 깨는 그 틱에 이미 차 있다) · 깨면 1분 반의 시간 상수로 풀린다 (3분에 8분의 1).
    /// 기운 없음 (심정지 뒤 증후군 — 되살아난 이는 몇 시간 · 며칠 기운이 없다): 멎은 심장이 다시 뛰면 1 · 3시간의 시간 상수로 풀린다.
    /// 값만 낸다 — 손발이 얼마나 느린지는 ⑧ 판단이 읽는다.
    /// </summary>
    private static void Drowse(long tick, ref Body b, double dt)
    {
        if (b.Asleep != 0)
        {
            if (b.SleptFrom == 0) b.SleptFrom = tick + 1;
            b.Groggy = (float)Math.Min(1, Time.ToSeconds(tick + 1 - b.SleptFrom) / (GroggyFullMin * 60));
        }
        else
        {
            b.SleptFrom = 0;
            if (b.Groggy > 0) { b.Groggy *= (float)Math.Exp(-dt / GroggyTauSec); if (b.Groggy < 0.01f) b.Groggy = 0; }
        }
        if (b.Spent > 0) { b.Spent *= (float)Math.Exp(-dt / (SpentTauHours * 3600)); if (b.Spent < 0.01f) b.Spent = 0; }
    }

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
    public const double BluntHealDays = 7, BurnHealDays = 14;
    // 곰팡이 포자 (깊이 고침 DF1 — WHO 2009 '실내 습기 · 곰팡이' 지침: 습하고 곰팡이 핀 방은 기침 · 쌕쌕거림 · 천식을 늘린다 · 건강한 어른이 며칠 만에 죽지는 않는다 /
    //   과민성 폐렴 (농부 폐): 짙게 마신 지 몇 시간 뒤 기침 · 숨참 — 몇 주 ~ 몇 달에 걸쳐 폐가 굳는다):
    //   마시는 포자 = 칸 곰팡이와 그 방 곰팡이 평균의 가운데 (마스크 · 우주복이면 0) · 0.15 넘게 마시면 곧 기침 ·
    //   폐 손상은 포자 × 0.15까지 2주 시간 상수로 천천히 다가간다 (곰팡이 1.0 방에서 한 주 0.06 · 다 차도 숨이 조금 찰 뿐 — 예전: 0.3 넘는 순간 계단 독 · 하룻밤에 죽음)
    public const double SporeCough = 0.15, SporeLungMax = 0.15, SporeLungDays = 14;
    /// <summary>곰팡이가 '피었다'고 원인 마디를 다는 짙기 (그 아래는 아직 보이지 않는 얼룩).</summary>
    public const float MoldSeen = 0.05f;
    public const double NightUrine = 0.5;          // 자는 동안 오줌 빠르기 (낮의 절반)
    // 화장실 · 샤워실 (docs/WASH.md): 방광은 0.6L(33몰)를 넘으면 참지 못한다 (보통 어른 방광 0.4~0.6L) · 장의 찌꺼기는 먹은 음식의 13% (하루 0.15kg 남짓) · 0.45kg를 넘으면 참지 못한다
    public const double BladderMax = 33, BowelPerMol = 0.039, BowelMax = 0.45;
    // 더러움: 날마다 0.2 (닷새면 몹시) · 땀 1몰(18mL)에 0.01 · 피 1L에 0.5 · 샤워 물 8L(444몰)면 다 씻긴다
    public const double DirtPerDay = 0.2, DirtPerSweatMol = 0.01, DirtPerBloodL = 0.5, ShowerMol = 444;
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
    /// <summary>칸마다 곰팡이가 핀 원인 마디 (깊이 고침 DF1 — 핀 칸의 젖음 · 고인 물의 원인에 잇는다 · 걷히면 0).</summary>
    public int[] MoldCause;
    /// <summary>방마다 떠다니는 곰팡이 포자 (그 방 곰팡이 평균 — 5단 · 곰팡이 단계마다).</summary>
    public float[] RoomSpore;
    private readonly HashSet<long> _sporeSeen = new();   // 몸 × 곰팡이 원인 마디 — 포자로 '다쳤다'를 알린 얼룩 (두 얼룩을 오가도 얼룩마다 한 번 · 걷힌 얼룩은 곰팡이 단계가 지운다 — DF1 검토 고침)
    private readonly HashSet<int> _moldLive = new();     // 곰팡이 단계의 빈 그릇: 지금 핀 얼룩의 마디
    public long Deaths;
    public double FoodRotted;   // 썩은 양분 (몰 — 장부)

    public Life(Field f)
    {
        F = f; L = f.L; T = f.T;
        _breathe = T.R("breathe"); _photo = T.R("photosynth"); _rot = T.R("rot"); _ferment = T.R("ferment");
        _glucose = T.S("Glucose"); _waterL = T.S("H2O_l"); _fat = T.S("Fat");
        Mold = new float[L.N]; MoldCause = new int[L.N]; RoomSpore = new float[L.Rooms.Count];
        _sealed = new bool[L.N];
        foreach (var fu in L.Furniture) if (T.Equip(fu.Type).Sealed) foreach (int c in fu.Cells) _sealed[c] = true;
        WashArrays();
    }

    /// <summary>포장된 식량 칸 (식량 보관함 — 상하지 않는다 · 불에는 탄다).</summary>
    private readonly bool[] _sealed;

    /// <summary>작물이 자라는 넓이 (m² — 칸 × 단).</summary>
    public double CropArea(Network? net, int k)
    {
        if (net == null || k >= Crops.Count || Crops[k].Device >= net.Devices.Count) return Field.CellFloor;
        var d = net.Devices[Crops[k].Device];
        return L.Furniture[d.Furniture].Cells.Length * Field.CellFloor * Math.Max(1, d.Def.Tiers);
    }

    // ───────────────────────── 요청 (⑧ 마음 · 시험) ─────────────────────────

    public int AddBody(int cell)
    {
        Bodies.Add(new Body
        {
            Cell = cell, Alive = 1, Conscious = 1, ActivityW = 90, Reserve = GlycogenMax * 0.8, Fat = Fat0, Water = 42 * 55.5, SpO2 = 0.97f,
            CoreK = BodyTemp, BloodL = 5, LastKPa = (float)F.Pressure(cell),
            CprAt = long.MinValue / 2, ShockAt = long.MinValue / 2, EpiAt = long.MinValue / 2, OpioidAt = long.MinValue / 2, HypnoticAt = long.MinValue / 2,
        });
        return Bodies.Count - 1;
    }

    public ref Body this[int i] => ref CollectionsMarshal.AsSpan(Bodies)[i];

    public void Move(int body, int cell) => this[body].Cell = cell;
    public void Eat(int body, double glucoseMol) { this[body].Reserve += glucoseMol; this[body].Bowel += glucoseMol * BowelPerMol; }
    public void Drink(int body, double waterMol) => this[body].Water += waterMol;

    /// <summary>⑨ 맞았다 (몸싸움): 타박 · 출혈 · 아픔 — 원인 마디에 때린 사람 (죽으면 그 사슬이 죽음의 원인).</summary>
    public void Strike(Sim sim, int body, float blunt, float bleed, int cause)
    {
        if (body < 0 || body >= Bodies.Count) return;
        ref var b = ref this[body];
        if (b.Alive == 0) return;
        float before = b.Blunt;
        b.Blunt += blunt; b.Bleed += bleed; b.Pain = Math.Min(1, b.Pain + blunt);
        b.HurtCause = cause;
        BleedInside(sim, body, ref b, before, cause);
        sim.Facts.Emit(sim.Tick, FactKind.Hurt, b.Cell, blunt, cause, actor: body);
    }

    /// <summary>날아든 물체에 맞아 다치는 에너지 (J — 이보다 약하면 닿기만 했다).</summary>
    public const double HitHurtJ = 20;

    /// <summary>
    /// ④ 물체 맞음 장부를 읽는다 (5틱마다 — 사람 꼬리표 = 몸 번호): 20J 넘으면 타박 E/800 (많아야 1.5) · 날카로우면 피 E/4000 — 원인은 그 맞음 (민 것까지 이어진다).
    /// 읽은 것은 지운다 (로봇 꼬리표는 ⑥ 몫으로 남긴다).
    /// </summary>
    private void TakeHits(Sim sim, Structure st)
    {
        var hits = st.PropHits;
        int n = 0;
        for (int k = 0; k < hits.Count; k++)
        {
            var h = hits[k];
            if (h.Tag < 0) { hits[n++] = h; continue; }
            if (h.Tag < Bodies.Count && h.E >= HitHurtJ) Strike(sim, h.Tag, (float)Math.Min(1.5, h.E / 800), h.Sharp != 0 ? h.E / 4000 : 0, h.Cause);
        }
        hits.RemoveRange(n, hits.Count - n);
    }

    /// <summary>⑫ 데었다 (⑫ 일하다 뜨거운 관에 — 세상의 확률): 화상 · 아픔 — 원인 마디.</summary>
    public void Scald(Sim sim, int body, float burn, int cause)
    {
        if (body < 0 || body >= Bodies.Count) return;
        ref var b = ref this[body];
        if (b.Alive == 0) return;
        b.Burn += burn; b.Pain = Math.Min(1, b.Pain + burn);
        b.HurtCause = cause;
        sim.Facts.Emit(sim.Tick, FactKind.Hurt, b.Cell, burn, cause, actor: body);
    }

    /// <summary>재배대마다 작물을 심는다 (망의 장치에서).</summary>
    public void Plant(Network net)
    {
        Crops.Clear();
        foreach (var d in net.Devices)
            if (d.Def.Role == "grow") Crops.Add(new Crop { Device = d.Id, Cell = d.Cell, Biomass = 0, Health = 1 });
    }

    // ───────────────────────── 한 걸음 ─────────────────────────

    public void Bind(Sim sim) => _net ??= sim.Find<Network>();

    public void Step(Sim sim, double dt)
    {
        var st = sim.Find<Structure>();
        var outside = sim.Find<Outside>();
        var chem = sim.Find<Chemistry>();
        _flames.Clear();
        if (chem != null && chem.BurningCount > 0)
            for (int i = 0; i < L.N; i++) if (chem.Burning[i] != 0 && chem.Hrr[i] > 0) _flames.Add(i);
        if (st != null && st.PropHits.Count > 0) TakeHits(sim, st);
        var span = CollectionsMarshal.AsSpan(Bodies);
        for (int b = 0; b < span.Length; b++)
        {
            ref var body = ref span[b];
            if (body.Alive == 0) continue;
            StepBody(sim, b, ref body, dt, st, outside, chem);
        }
        if (st != null) Array.Clear(st.ImpactJ);
        BodyNoise(sim, dt);   // 소리 단계 S2: 코골이 · 기침
        Ears(sim, dt);        // 소리 단계 S3: 큰 소리가 귀를 먹먹하게 한다
        var net = sim.Find<Network>();
        if (net != null) Grow(sim, net, dt);
        if (sim.Tick % 125 == 3) { MoldStep(sim, dt * 25); RotStep(sim, dt * 25); }
        if (sim.Tick % 25 == 3) Spread(sim, dt * 5);   // 옮는 병 (1분마다)
        if (st != null) StampAirLoss(sim, st, sim.Tick % 25 == 7);
    }

    // 방마다 공기를 앗아 간 원인 (구멍 · 찢어짐 — 그 구역이 바깥에 열렸던 마지막 원인 · 구역이 이어지면 함께 번진다):
    // 구멍을 막은 뒤에도 옅은 공기에 쓰러지고 죽은 이의 원인 사슬이 그 구멍에 잇는다 (찾아 고친 결함: '산소가 모자랐다 → 죽었다'에 뿌리가 없었다)
    public int[] AirLostCause = Array.Empty<int>();
    public long[] AirLostTick = Array.Empty<long>();
    private void StampAirLoss(Sim sim, Structure st, bool spread)
    {
        if (AirLostCause.Length != L.Rooms.Count) { AirLostCause = new int[L.Rooms.Count]; AirLostTick = new long[L.Rooms.Count]; }
        Dictionary<int, int>? open = null;   // 틱마다: 바깥에 열린 구역만 (드물다) · 1분마다: 이어진 구역으로 번진다
        foreach (var k in F.Links) if (k.RA >= 0 && k.RB < 0 && st.DamageCause[k.Via] != 0) (open ??= new())[k.RA] = st.DamageCause[k.Via];
        if (open == null && !spread) return;
        for (int r = 0; r < F.Regions.Count; r++)
        {
            if (!spread && !open!.ContainsKey(r)) continue;
            int cause = open?.GetValueOrDefault(r) ?? 0; long at = sim.Tick;
            if (cause == 0)
            {
                // 바깥에 열려 있지 않은 구역: 그 구역에 이어진 방 가운데 가장 최근에 공기를 잃은 원인 (12시간 안)
                at = 0;
                foreach (int c in F.Regions[r]) { int rm = L.Room[c]; if (rm >= 0 && AirLostCause[rm] != 0 && AirLostTick[rm] > at && sim.Tick - AirLostTick[rm] < Time.Hours(12)) { at = AirLostTick[rm]; cause = AirLostCause[rm]; } }
                if (cause == 0 || F.ZoneKPa(r) > 80) continue;   // 공기가 돌아왔다
            }
            int last = -1;
            foreach (int c in F.Regions[r]) { int rm = L.Room[c]; if (rm < 0 || rm == last) continue; last = rm; if (AirLostTick[rm] <= at || AirLostCause[rm] == 0) { AirLostCause[rm] = cause; AirLostTick[rm] = at; } }
        }
    }

    private readonly List<int> _flames = new(); // 이번 틱 불 붙은 칸 (틱 안에서만 — 찍지 않는다)
    public const int RadiantReach = 6;
    public const double RadiantFraction = 0.3; // 불꽃 열의 3할이 복사로 (SFPE — 가구 불 0.2~0.4)

    /// <summary>
    /// 이 칸이 받는 불의 복사열 (kW/m²): 6칸 안 · 벽 · 닫힌 문에 가리지 않은 불 칸마다 점원 χQ/(4πr²) (r은 칸 가운데 사이 m · 1m 아래는 1m).
    /// src: 가장 센 열을 보낸 불 칸. 몸이 받는 열은 불 열의 아주 작은 몫이라 장부에 옮기지 않는다 (화상만 잰다).
    /// </summary>
    public double RadiantKwM2(int c, Chemistry chem, out int src)
    {
        double q = 0, best = 0; src = -1;
        int cx = L.X(c), cy = L.Y(c);
        foreach (int j in _flames)
        {
            int dx = L.X(j) - cx, dy = L.Y(j) - cy;
            if (j == c || Math.Abs(dx) > RadiantReach || Math.Abs(dy) > RadiantReach) continue;
            double r2 = Math.Max(1, dx * dx + dy * dy);
            if (!Sees(cx, cy, L.X(j), L.Y(j))) continue;
            double qj = RadiantFraction * chem.Hrr[j] / (4 * Math.PI * r2);
            q += qj;
            if (qj > best) { best = qj; src = j; }
        }
        return q;
    }

    /// <summary>두 칸 사이에 벽 · 닫힌 문이 없나 (사이 칸만 — 직선 칸 따라가기).</summary>
    private bool Sees(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        int x = x0, y = y0;
        while (true)
        {
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
            if (x == x1 && y == y1) return true;
            int i = L.Index(x, y);
            if (L.Kind[i] == CellKind.Wall || L.Kind[i] == CellKind.Outside) return false;
            if (L.Kind[i] == CellKind.Door && F.DoorOpen[L.DoorAt[i]] < 0.5f) return false;
        }
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
        Drowse(sim.Tick, ref b, dt);   // 잠기운 · 기운 없음 (DF4)
        double p = F.Pressure(c), tair = F.Temp(c);
        bool suit = b.Suit != 0, low = b.Low != 0;
        double thead = F.HeadTemp(c, low);   // ③ 머리 높이의 공기 (뜨거운 층이 서면 선 이는 위 · 기어가거나 누운 이는 아래)
        // 산소 마스크: 기압이 남아 있으면 병의 산소를 마신다 (얼굴을 덮어 연기 · 독도 덜 들이쉰다 — 진공에는 소용없다 · 날숨은 밸브로: 장부는 우주복과 같다)
        bool mask = !suit && b.Mask != 0 && b.MaskO2 > 0 && p > 6.3;
        bool own = suit || mask;   // 제 병의 숨
        double pO2 = suit ? 21 : F.HeadPartial(T.gO2, c, low);
        // 무중력: 날숨이 부력으로 떠오르지 않아 가만히 있는 이의 얼굴 앞에 고인다 (10분에 걸쳐 · 많아야 2kPa) — 공조 바람이 닿거나 움직이면 흩어진다
        float gc = F.Grav[c];
        if (!own && gc < Field.LowG && p > 20)
        {
            double still = b.Asleep != 0 || b.ActivityW <= 110 ? 1 : 0.3, target = FacePocketKPa * still * (1 - F.Fan(c)) * (1 - gc / Field.LowG);
            double tauF = target >= b.FaceCO2 ? 600 : 30 + 570 * (1 - F.Fan(c));   // 고이는 데 10분 · 바람이 닿으면 30초에 걷힌다
            b.FaceCO2 += (float)((target - b.FaceCO2) * (1 - Math.Exp(-dt / tauF)));
        }
        else if (b.FaceCO2 > 0) b.FaceCO2 = (float)(b.FaceCO2 * Math.Exp(-dt / 60));
        double pCO2 = own ? 0.04 : F.HeadPartial(T.gCO2, c, low) + b.FaceCO2;
        double co = own ? 0 : F.HeadPpm(T.gCO, c, low), nh3 = own ? 0 : F.HeadPpm(T.gNH3, c, low), o3 = own ? 0 : F.HeadPpm(T.gO3, c, low), soot = own ? 0 : F.HeadPpm(T.gSoot, c, low);
        double hcn = own ? 0 : F.HeadPpm(T.gHCN, c, low), hcl = own ? 0 : F.HeadPpm(T.gHCl, c, low);
        // ② (라) 배터리가 뿜은 플루오린 화합물 — 플루오린화수소로 센다 (POF₃는 둘 · PF₅는 셋을 낸다)
        double hf = own ? 0 : F.HeadPpm(T.gHF, c, low) + 2 * F.HeadPpm(T.gPOF3, c, low) + 3 * F.HeadPpm(T.gPF5, c, low);
        int env = 0; // 이번에 몸을 해친 것의 원인
        string? harm = null;
        if (b.Conscious == 0) b.ActivityW = 80; // 의식 없는 몸은 기초 대사로 (쓰러지기 전 달리던 대사량이 남아 스스로 익지 않게)

        // ── 숨 ── (추우면 떨어서 대사가 는다)
        double fever = FeverK(b) * (1 - FeverCut(b));   // 열: 감염 · 앓음이 뇌의 설정점을 올린다 — 떨어서 체온을 올리고 · 땀은 늦게 (깊이 바퀴 2) · 해열제가 내린다 (⑦ 바퀴 3)
        double shiver = b.Conscious != 0 ? Math.Clamp((309.75 + fever - b.CoreK) * 300, 0, 400) : 0; // 36.6℃ 아래로 내려간 만큼 떤다 (0.1℃에 30W · 많아야 400W)
        double watts = b.ActivityW + shiver; // 잠의 대사량은 ⑧ 마음이 잠 77W로 준다 (여기서 또 줄이지 않는다)
        double fc = b.Reserve <= 0.01 ? 0 : b.Fat <= 0.01 ? 1 : CarbShare(watts); // 탄수화물 몫 (글리코겐이 바닥나면 지방만)
        double vo2 = watts / (fc * GlucoseJPerO2 + (1 - fc) * FatJPerO2) * dt; // 필요한 산소 (몰)
        double hill(double x) => Math.Pow(x, 2.7) / (Math.Pow(x, 2.7) + Math.Pow(7, 2.7));
        // 산소 요법: 치료 침대에 누우면 마스크로 거의 순수한 산소를 마신다 — 상한 폐로도 피에 산소가 들어간다
        bool therapy = !own && p > 30 && OnTherapy(sim, c);
        double pO2e = therapy ? Math.Max(pO2, 0.9 * Math.Min(p, 101.3)) : mask ? Math.Max(pO2, 0.95 * Math.Min(p, 101.3)) : pO2;
        double sat = hill(pO2e) * (1 - b.COHb) * (1 - Math.Min(0.9, b.LungDamage * (therapy ? 0.5 : 1)));
        double resp = RespDepression(b);   // ⑦ 진통제를 너무 많이 맞으면 숨이 얕아진다
        if (resp > 0) sat *= 1 - resp;
        b.SpO2 = (float)sat;
        double o2Got;
        // ⑦ 심정지: 숨도 피도 돌지 않는다 (가슴 압박을 받으면 피가 조금 돈다 — 포화도 0.3 언저리)
        bool cpr = b.Arrest != 0 && ArrestStep(sim, id, ref b, dt);
        if (b.Arrest != 0) { o2Got = 0; b.SpO2 = cpr ? (float)Math.Min(sat, 0.3) : 0; }
        else if (suit) { o2Got = Math.Min(vo2, b.SuitO2); b.SuitO2 -= (float)o2Got; if (b.SuitO2 <= 0) b.SpO2 = 0; }
        else if (mask) { o2Got = Math.Min(vo2 * Math.Min(1, sat / 0.95), b.MaskO2); b.MaskO2 -= (float)o2Got; if (b.MaskO2 <= 0) b.Mask = 0; }
        else o2Got = p > 1 ? Math.Min(vo2 * Math.Min(1, sat / 0.95), F.Gas(T.gO2, c) * 0.5) : 0;
        // 숨: 포도당 + 6O₂ → 6CO₂ + 6H₂O · 지방(팔미트산) + 23O₂ → 16CO₂ + 16H₂O — 섞어 태운다 (호흡 몫 0.79 ~ 1.0)
        double xg = Math.Min(o2Got * fc / 6, b.Reserve), xf = Math.Min(o2Got * (1 - fc) / 23, b.Fat);
        if (xg + xf > 0)
        {
            double o2 = 6 * xg + 23 * xf, co2 = 6 * xg + 16 * xf, h2o = 6 * xg + 16 * xf;
            // 반응열은 몸으로 (장부: E(기체, T) = hf + cv(T−298))
            double q = xg * _glucose.Hf * 1000 + xf * _fat.Hf * 1000;
            if (!own) { q += o2 * T.Gases[T.gO2].Energy(tair); F.RemoveGas(c, T.gO2, o2); sim.Book.Add(Res.O2, Flow.Breathed, -o2); }
            else q += o2 * T.Gases[T.gO2].Energy(BodyTemp);
            q -= co2 * T.Gases[T.gCO2].Energy(BodyTemp) + h2o * T.Gases[T.gH2O].Energy(BodyTemp);
            b.Reserve -= xg; b.Fat -= xf;
            if (!own && p > 1) { F.AddGas(c, T.gCO2, co2, BodyTemp); F.AddGas(c, T.gH2O, h2o, BodyTemp); }
            b.CoreK += q / BodyC;
            O2Used += o2; CO2Made += co2; MetabolicJ += xg * GlucoseJPerO2 * 6 + xf * FatJPerO2 * 23;
        }
        // 남는 탄수화물은 지방으로 (4 포도당 + O₂ → 팔미트산 + 8CO₂ + 8H₂O · 3시간에 걸쳐 — 열이 조금 난다)
        if (b.Reserve > GlycogenMax && !own && p > 1)
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
        else if (b.Arrest != 0)
        {
            // 피가 돌지 않는다 — 숨 쉴 공기마저 나쁘면 그 빠르기로 (옅은 공기는 폐에서 피의 산소를 오히려 빼낸다)
            double air = sat < 0.75 ? (0.75 - sat) / 0.75 : 0;
            b.Hypoxia += (float)(Math.Max(cpr ? CprRate : ArrestRate, air) * dt / 60);
            env = air > 0.5 ? EnvCause(sim, c, st, chem) : b.ArrestCause; harm = air > 0.5 ? (b.COHb > 0.3 ? "일산화탄소에 중독됐다" : "산소가 모자랐다") : "심장이 멎었다";
        }
        else if (b.SpO2 < 0.75) { b.Hypoxia += (float)((0.75 - b.SpO2) / 0.75 * dt / 60); bool drug = resp > 0.3; env = drug ? b.DrugCause : EnvCause(sim, c, st, chem); harm = drug ? "진통제를 너무 많이 맞아 숨이 얕아졌다" : b.COHb > 0.3 ? "일산화탄소에 중독됐다" : "산소가 모자랐다"; if (drug && b.Hypoxia > 0.5f && b.Hypoxia - (0.75 - b.SpO2) / 0.75 * dt / 60 <= 0.5) Overdoses++; }
        else b.Hypoxia = (float)Math.Max(0, b.Hypoxia - dt / 600);
        // ⑦ 장기: 깊은 산소 부족에 뇌가 · 오래 옅은 피의 산소에 심장이 상한다
        if (b.Hypoxia > BrainHypoxia) b.Brain = (float)Math.Min(1.2, b.Brain + BrainRate * (b.Hypoxia - BrainHypoxia) * dt / 60);
        if (b.Arrest == 0 && b.SpO2 < 0.5f) b.Heart = (float)Math.Min(1, b.Heart + 0.02 * (0.5 - b.SpO2) * dt / 60);
        // 일산화탄소 헤모글로빈 → 평형 1−e^(−ppm/1000), 시간 상수 4시간 × (80W/대사량)
        double eq = 1 - Math.Exp(-co / 1000);
        double tau = 4 * 3600 * 80 / Math.Max(60, b.ActivityW) / (therapy ? 4 : 1); // 순수한 산소는 일산화탄소를 네 배 빨리 밀어낸다
        b.COHb += (float)((eq - b.COHb) * (1 - Math.Exp(-dt / tau)));
        // 시안화수소 (Purser · ISO 13571): 세포가 산소를 못 쓴다 — 마신 양 FED += ppm^2.36 / 1.2×10⁶ (분마다 · 숨 쉬는 양만큼 — 쉴 때 1 · 힘든 일 2.5배):
        // 100ppm이면 23분 · 200ppm이면 4.5분에 1 (쓰러짐) · 3이면 숨진다 · 간이 풀어 1시간 반감 (치료 — ⑦)
        if (hcn > 1) b.CN += (float)(Math.Pow(hcn, 2.36) / 1.2e6 * dt / 60 * Math.Clamp(b.ActivityW / 100, 0.8, 2.5));
        b.CN = (float)(b.CN * Math.Exp(-0.693 * dt / (b.Antidote > 0 ? 360 : 3600)));   // 해독제는 열 배 빨리 푼다
        if (b.CN > 0.3) { harm ??= "시안화수소에 중독됐다"; env = env == 0 ? EnvCause(sim, c, st, chem) : env; }
        // 이산화탄소 · 독
        if (pCO2 > 7) { b.Hypoxia += (float)((pCO2 - 7) / 5 * dt / 60); harm ??= "이산화탄소가 너무 짙었다"; env = env == 0 ? EnvCause(sim, c, st, chem) : env; }
        if (pCO2 > 3) b.Pain = (float)Math.Min(1, b.Pain + (pCO2 - 3) / 40 * dt / 60);
        else if (pCO2 > HeadacheKPa && b.Pain < 0.3f) b.Pain = (float)Math.Min(0.3, b.Pain + (pCO2 - HeadacheKPa) * 0.1 * dt / 60);   // 두통 (1.5% 넘게 오래 — 우주 정거장 승무원의 두통)
        double lung = 0;
        // 암모니아: 300ppm은 한 시간 넘게 버티고 · 3000ppm은 30분이면 위태롭고 · 1만ppm은 몇 분 (분마다 폐 손상 0.0167 × (ppm/3000)^1.3)
        if (nh3 > 100) lung += 0.0167 * Math.Pow(nh3 / 3000, 1.3);
        // 염화수소: 암모니아보다 두 배쯤 독하다 (쥐 LC50 1시간 3100 대 7300ppm) — 50ppm 넘으면 눈 · 목 · 폐를 찌른다
        if (hcl > 50) lung += 0.0167 * Math.Pow(hcl / 1500, 1.3);
        if (hcl > 5) b.Pain = (float)Math.Min(1, b.Pain + Math.Min(0.5, hcl / 1000) * dt / 300);   // 눈이 따갑고 기침
        // 플루오린화수소: 염화수소보다 약 3.5배 독하다 (AEGL-3 10분 170ppm 대 620ppm) — 폐 · 눈 · 피부
        if (hf > 15) lung += 0.0167 * Math.Pow(hf / 430, 1.3);
        if (hf > 2) b.Pain = (float)Math.Min(1, b.Pain + Math.Min(0.5, hf / 300) * dt / 300);
        if (o3 > 5) lung += o3 / 200;
        if (soot > 2000) lung += soot / 200_000;
        // 곰팡이 포자 (DF1): 짙기에 비례해 날 단위로 천천히 — 쓰러짐 · 죽음의 이름이 되지 않는 가벼운 해 (mild)
        int room = L.Room[c];
        double spore = own ? 0 : 0.5 * (Mold[c] + (room >= 0 && room < RoomSpore.Length ? RoomSpore[room] : Mold[c]));
        b.Spore = (float)spore;
        double sporeTo = SporeLungMax * spore;
        string? mild = null;
        if (lung > 0) { b.LungDamage += (float)(lung * dt / 60); b.Pain = (float)Math.Min(1, b.Pain + lung * dt / 120); harm ??= hf > 15 ? "플루오린화수소를 마셨다" : hcl > 50 ? "염화수소를 마셨다" : nh3 > 100 ? "암모니아를 마셨다" : o3 > 5 ? "오존을 마셨다" : "연기를 마셨다"; env = env == 0 ? EnvCause(sim, c, st, chem) : env; }
        else if (b.LungDamage < sporeTo) b.LungDamage += (float)((sporeTo - b.LungDamage) * (1 - Math.Exp(-dt / (SporeLungDays * 86400))));
        else { LungHeal(ref b, dt); if (b.LungDamage < sporeTo) b.LungDamage = (float)sporeTo; }
        if (spore > SporeCough) mild = "곰팡이 포자를 마셨다";

        // ── 체온 ──
        double tsurf = F.HasSurface(c) ? F.Tf[c] : F.Ts[c];
        double wind = Math.Sqrt(F.WindX[c] * F.WindX[c] + F.WindY[c] * F.WindY[c]);
        double thin = Math.Sqrt(Math.Clamp(p / 101.3, 0, 2));
        // 체온 조절 (ISO 7730 · Gagge 두 마디를 가볍게): 피부 온도는 혈관이 조였다 풀었다 (추우면 낮게 · 더우면 높게) ·
        // 마른 열은 '옷의 저항 + 겉 공기층의 저항'을 지나 공기(대류)와 둘레 겉면(복사)으로 — 옷 1clo · 침대 이불 2.5clo (1clo = 0.155 m²K/W)
        double coreC = b.CoreK - 273.15 - fever;
        double skin = 273.15 + Math.Clamp(33.7 - 4 * Math.Max(0, 36.8 - coreC) + 2 * Math.Max(0, coreC - 37.0), 28, 36);
        double hc = (3 + 4 * Math.Pow(wind, 0.6)) * thin;
        double hr = 4 * 0.95 * Field.Sigma * 0.7 * Math.Pow((skin + tsurf) / 2, 3);   // 복사를 곧게 편 것 (W/m²K)
        bool inBed = b.Asleep != 0 && L.FurnitureAt[c] >= 0 && L.Furniture[L.FurnitureAt[c]].Type == "Bed";
        double icl = suit ? 1.2 : inBed ? 2.5 * 0.155 : 0.155;                            // 우주복은 열을 거의 막는다 (냉각 속옷이 뺀다)
        double h = hc + hr;
        double top = h > 0 ? (hc * thead + hr * tsurf) / h : thead;                         // 느끼는 온도 (머리 높이 공기 · 겉면 평균)
        double dry = (skin - top) * SkinArea / (icl + 1 / Math.Max(0.5, h));                // W (+ 몸이 잃음)
        double conv = h > 0 ? dry * hc / h : 0, rad = h > 0 ? dry * hr / h : 0;
        double sweat = 0;
        if (b.CoreK > 310.4 + fever && !suit && p > 6.3)
        {
            // 땀: 몸이 내려는 만큼(심부 37.25℃ 넘은 만큼) — 마를 수 있는 한도까지 (ISO 7933: 피부 포화 수증기압 − 공기 수증기압 ÷ 증발 저항(옷 + 공기층 · 루이스 16.5))
            double demand = Math.Min(600, (b.CoreK - 310.4 - fever) * 400);
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
                b.Dirt = (float)Math.Min(1, b.Dirt + n * DirtPerSweatMol);   // 땀이 마르면 몸이 끈적하다
                F.AddGas(c, T.gH2O, n, BodyTemp);
                // sweat W는 이미 체온에서 뺐다 — 장부를 맞추는 차이만 몸에서
                b.CoreK -= (need - sweat * dt) / BodyC;
            }
        }
        // 콩팥: 물이 넉넉하면 오줌을 많이 · 모자라면 적게 (하루 0.5 ~ 4.5L) — 방광이 차면 비운다 (오수로 — 망이 있으면)
        // 자는 동안은 절반 (밤의 항이뇨 호르몬 — 건강한 어른의 밤 오줌은 하루의 20% 아래: ICS 2002)
        double urine = Math.Min(b.Water * 0.001, Urine0 * Math.Clamp(1 + (b.Water - UrineRefWater) / (0.01 * Mind.BodyWater0), 0.33, 3) * (b.Asleep != 0 ? NightUrine : 1) * dt);
        b.Water -= urine; b.Bladder += urine; UrineMade += urine;
        // 몸이 더러워진다: 날마다 · 땀 · 피 (씻는 것은 사람의 판단 — 샤워 칸 · 물수건)
        b.Dirt = (float)Math.Min(1, b.Dirt + DirtPerDay * dt / 86400 + (b.Bleed + b.BleedIn > 0 ? DirtPerBloodL * b.Bleed * dt / 3600 : 0));
        // 참지 못한다 (월드): 방광 · 장이 한계를 넘으면 그 자리에 지린다 — 바닥이 젖고 몸이 더러워진다 (비우러 가는 것은 사람의 판단)
        if (b.Bladder > BladderMax || b.Bowel > BowelMax) Soil(sim, id, ref b, c);
        // 늘 조금씩 물을 잃는다 (숨 · 피부 — 하루 1.5L)
        double insens = Insensible * dt;
        b.Water -= insens;
        if (!suit && p > 1) { F.AddGas(c, T.gH2O, insens, BodyTemp); b.CoreK -= insens * ((T.Gases[T.gH2O].Hf - _waterL.Hf) * 1000 + T.Gases[T.gH2O].C * (BodyTemp - 298.15)) / BodyC; }

        // ── 화상 · 동상 ──
        if (!suit && thead > 330) { b.Burn += (float)((thead - 330) / 100 * dt / 60); harm ??= "뜨거운 공기에 데었다"; env = env == 0 ? EnvCause(sim, c, st, chem) : env; }
        if (chem != null)
        {
            // 불 속: 불꽃에 닿는다
            double flame = chem.Hrr[c];
            if (flame > 20) { b.Burn += (float)(flame / 200 * dt / 30 * (suit ? 0.2 : 1)); harm ??= "불길에 데었다"; env = env == 0 ? (chem.FireCause[c] != 0 ? chem.FireCause[c] : EnvCause(sim, c, st, chem)) : env; }
            // 복사열: 보이는 불 칸마다 받는 열 (kW/m²) — 열량 q^(4/3)·초로 화상 (2.5kW/m²는 1분에 아프고 · 10kW/m²는 30초면 물집 — Eisenberg 열량)
            int src = -1;
            double q = _flames.Count > 0 ? RadiantKwM2(c, chem, out src) : 0;
            if (q > 1.0 && src >= 0)
            {
                b.Burn += (float)(Math.Pow(q, 4.0 / 3) * dt / 800 * (suit ? 0.2 : 1));
                harm ??= "불의 열기에 데었다"; env = env == 0 ? (chem.FireCause[src] != 0 ? chem.FireCause[src] : EnvCause(sim, c, st, chem)) : env;
            }
        }
        if (!suit && thead < 250 && p > 1) b.Frost += (float)((250 - thead) / 100 * dt / 600);
        if (b.CoreK < 306.5 && b.Alive != 0) { harm ??= "추위에 몸이 식었다"; env = env == 0 ? EnvCause(sim, c, st, chem) : env; }
        // 열사병 (DF1): 심부 40℃를 넘으면 쓰러진다 — 까닭은 그 방을 덥힌 것 (불 · 달군 장치 · 멈춘 냉각 — 없으면 이 마디가 뿌리)
        //   감염 · 앓음 · 맹장의 열 (오른 설정점)이 더 크게 올렸으면 몸을 해친 바깥 것이 없다 — 쓰러짐의 까닭은 그 병에서 (DownWhy · DF1 검토 고침)
        if (b.CoreK > 313 && b.Alive != 0 && !FeverDriven(b)) { harm ??= "몸이 너무 뜨거워졌다"; env = env == 0 ? HotCause(sim, c, st, chem) : env; }

        // ── 압력 · 충격 · 방사선 ──
        double dp = b.LastKPa - p;
        if (!suit && dp > 40) { b.Barotrauma += (float)(dp / 150); b.LungDamage += (float)(dp / 400); harm ??= "급감압에 폐를 다쳤다"; env = env == 0 ? EnvCause(sim, c, st, chem) : env; }
        b.LastKPa = (float)p;
        if (st != null && st.ImpactJ[c] > 0)
        {
            double e = st.ImpactJ[c];
            float before = b.Blunt;
            b.Blunt += (float)(e / 8000);
            BleedInside(sim, id, ref b, before, st.DamageCause[c]);
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
        b.BloodL -= (float)((b.Bleed + b.BleedIn) * dt / 3600);
        InsideStep(sim, id, ref b, dt);   // ⑦ (라) 속 출혈 · 맹장염
        b.Bleed = (float)(b.Bleed * Math.Exp(-dt / 1800)); // 굳는다
        // 상처가 낫는다: 타박 · 화상은 살아 있으면 천천히 (가벼운 타박 1~2주 · 화상 2~3주 — e로 줄어드는 날 7일 · 14일) — 아주 큰 상처는 돌봄을 받아야 낫는다
        //   덮어 둔 상처는 두 배 · 의료 침대에서는 세 배 빨리 · 곪은 상처는 항생제가 없으면 반만 (깊이 바퀴 2)
        double careX = OnTherapy(sim, c) ? 3 : b.Dressed > 0 ? 2 : 1;
        if (b.Infect > 0.1f && b.Abx <= 0) careX *= 0.5;
        bool cared = b.Dressed > 0 || OnTherapy(sim, c);
        if (b.Blunt > 0 && (b.Blunt < 1.0f || cared)) b.Blunt = (float)(b.Blunt * Math.Exp(-dt * careX / (BluntHealDays * 86400)));
        if (b.Burn > 0 && (b.Burn < 0.8f || cared)) b.Burn = (float)(b.Burn * Math.Exp(-dt * careX / (BurnHealDays * 86400)));
        Care(sim, id, ref b, dt);
        if (b.Burn > 0.2) b.Water -= (b.Burn * 55.5 * dt / 3600); // 넓은 화상은 물을 잃는다

        // ── 의식 · 죽음 ──
        double pain = FeltPain(b);   // 약이 던 만큼 덜 아프다
        b.Pain = (float)Math.Max(0, b.Pain - dt / 1800);
        if (b.Cell >= 0 && b.Cell < L.N) Decondition(ref b, F.Grav[b.Cell], dt);   // 무중력의 뼈 · 근육
        DrugStep(sim, id, ref b, dt);              // ⑦ 약
        ArrestTriggers(sim, id, ref b, dt, env);   // ⑦ 심정지
        RegenKinetics(ref b, dt, OnTherapy(sim, c));   // ⑦ (라-2) 재생 젤 — 치료 침대에 누운 동안
        HealOrgans(ref b, dt);
        bool down = b.Arrest != 0 || b.Brain >= 0.5f || b.Hypoxia > 0.5 || b.COHb > 0.5 || b.CN > 1 || pCO2 > 7 || b.CoreK < 305 || b.CoreK > 313 || b.BloodL < 3.5 || pain > 0.95 || b.RadRecent > 6 || b.Infect > 1;
        if (down && b.Conscious != 0)
        {
            b.Conscious = 0;
            // 이번에 몸을 해친 것이 없으면 쓰러뜨린 몸의 상태에서 까닭을 · 앞서 다친 일에 잇는다 (DF1 — 예전: 까닭 · 원인 없는 '쓰러졌다')
            var (why, from) = harm != null ? (harm, env) : DownWhy(b, pain, outside);
            int cause = b.Infect > 1 && harm == null ? sim.Causes.Add(CauseKind.Biology, "패혈증으로 쓰러졌다 — 곪은 상처를 치료하지 못했다", sim.Tick, c, b.InfectCause)
                      : sim.Causes.Add(CauseKind.Biology, why, sim.Tick, c, from);
            b.HurtCause = cause;
            sim.Facts.Emit(sim.Tick, FactKind.Collapsed, c, (float)b.Hypoxia, cause, actor: id);
        }
        else if (!down && b.Conscious == 0 && b.Hypoxia < 0.3 && b.CN < 0.7 && b.CoreK < 312.15 && b.Arrest == 0 && b.Brain < 0.5f) b.Conscious = 1; // 열로 쓰러지면 39℃ 아래로 식어야 깬다
        string? death = b.CN > 3 ? "시안화수소에 중독됐다" : b.Hypoxia > 3 ? harm ?? "숨을 못 쉬었다" : b.Brain >= 1 ? "뇌가 상했다"
            : b.CoreK < 301 ? "얼어 죽었다" : b.CoreK > 316 ? "열사병" : b.BloodL < 2.5 ? "피를 너무 흘렸다"
            : b.Burn > 1.2 ? "화상" : b.Blunt > 1.5 ? "크게 다쳤다" : b.LungDamage > 1.2 ? "폐가 상했다"
            : b.Infect > 2 ? "패혈증" : b.Liver >= 1 ? "간이 상했다" : b.Water < 42 * 55.5 * 0.82 ? "목이 말라" : b.Reserve <= 0.01 && b.Fat <= 0.01 && b.CoreK < 306 ? "굶어서" : b.RadRecent > 9 ? "방사선" : null;
        if (death != null)
        {
            b.Alive = 0; b.Conscious = 0; b.DiedAt = sim.Tick;
            int cause = sim.Causes.Add(CauseKind.Biology, $"죽었다 — {death}", sim.Tick, c, death == "패혈증" ? b.InfectCause : death == "간이 상했다" ? b.HurtCause : b.Arrest != 0 ? b.ArrestCause : env != 0 ? env : b.HurtCause);
            b.DeathCause = cause;
            Deaths++;
            sim.Facts.Emit(sim.Tick, FactKind.Died, c, 0, cause, actor: id);
        }
        else if (harm != null && b.HurtCause == 0)
        {
            b.HurtCause = sim.Causes.Add(CauseKind.Biology, harm, sim.Tick, c, env);
            sim.Facts.Emit(sim.Tick, FactKind.Hurt, c, 0, b.HurtCause, actor: id);
        }
        // 곰팡이 포자 (가벼운 해 — DF1): 핀 얼룩마다 한 번 '다쳤다' (기침 — 원인은 그 곰팡이 · 두 얼룩을 오가도 다시 알리지 않는다) · 쓰러짐 · 죽음의 이름이나 앞서 다친 일이 되지 않는다
        if (mild != null && b.Alive != 0 && SporeCause(c) is int sc && sc != 0 && _sporeSeen.Add((long)id << 32 | (uint)sc))
        {
            sim.Facts.Emit(sim.Tick, FactKind.Hurt, c, 0, sim.Causes.Add(CauseKind.Biology, mild, sim.Tick, c, sc), actor: id);
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
        // 떨어져 나간 조각 안: 공기 · 전력이 끊겼다 (산소 발생기 · 정화기 · 공조가 배에 남았다)
        if (st != null && st.FragmentOf(c) > 0) return st.FragmentCutCause(c);
        // 그 방의 공기를 앗아 간 마지막 구멍 (막은 뒤에도 옅은 공기)
        int room = L.Room[c];
        if (room >= 0 && room < AirLostCause.Length && AirLostCause[room] != 0 && sim.Tick - AirLostTick[room] < Time.Hours(12)) return AirLostCause[room];
        return 0;
    }

    /// <summary>
    /// 쓰러뜨린 몸의 상태 (이번 틱에 몸을 해친 것이 없을 때 — 앞서 쌓인 것): 까닭의 글 · 부모 원인 (앞서 다친 일 · 심정지 · 별 폭발).
    /// 쓰러짐의 조건 (StepBody의 down)과 같은 차례로 본다.
    /// </summary>
    private static (string why, int from) DownWhy(in Body b, double pain, Outside? outside)
    {
        if (b.Arrest != 0) return ("심장이 멎어 쓰러졌다", b.ArrestCause);
        if (b.Brain >= 0.5f) return ("뇌를 다쳐 쓰러졌다", b.HurtCause);
        if (b.CN > 1) return ("시안화수소에 중독돼 쓰러졌다", b.HurtCause);
        if (b.COHb > 0.5) return ("일산화탄소에 중독돼 쓰러졌다", b.HurtCause);
        if (b.Hypoxia > 0.5) return ("숨이 모자라 쓰러졌다", b.HurtCause != 0 ? b.HurtCause : b.DrugCause);
        if (b.BloodL < 3.5) return ("피를 많이 흘려 쓰러졌다", b.HurtCause);
        if (b.CoreK > 313) return FeverDriven(b) ? ("열이 너무 올라 쓰러졌다", FeverCause(b)) : ("몸이 너무 뜨거워 쓰러졌다", b.HurtCause);
        if (b.CoreK < 305) return ("몸이 식어 쓰러졌다", b.HurtCause);
        if (pain > 0.95) return ("너무 아파 쓰러졌다", b.HurtCause);
        if (b.RadRecent > 6) return ("방사선을 많이 쬐어 쓰러졌다", outside != null && outside.FlareCause != 0 ? outside.FlareCause : b.HurtCause);
        return ("기운이 다해 쓰러졌다", b.HurtCause);
    }

    /// <summary>
    /// 체온이 오른 몫 (36.6℃ 위) 가운데 열 — 감염 · 앓음 · 맹장이 올린 뇌의 설정점 — 이 바깥 열보다 큰가 (DF1 검토 고침).
    /// 그렇다면 40℃를 넘긴 까닭은 그 병 (<see cref="FeverCause"/>) · 아니면 그 방을 덥힌 것 (<see cref="HotCause"/>).
    /// </summary>
    private static bool FeverDriven(in Body b)
    {
        double fever = FeverK(b) * (1 - FeverCut(b));
        return fever > 0 && fever >= b.CoreK - 309.75 - fever;
    }

    /// <summary>열의 원인 마디: 설정점을 가장 크게 올린 것 — 곪은 상처 · 옮는 병 · 맹장염 (<see cref="FeverK"/>의 몫대로 · 없으면 앞서 다친 일).</summary>
    private static int FeverCause(in Body b)
    {
        double infect = Math.Min(2.5, b.Infect * 2.5), ill = b.Ill == 2 ? 1.3 : 0, app = Math.Max(0, b.Appendix);
        int why = 0; double most = 0;
        if (infect > most && b.InfectCause != 0) { most = infect; why = b.InfectCause; }
        if (ill > most && b.IllCause != 0) { most = ill; why = b.IllCause; }
        if (app > most && b.AppendixCause != 0) why = b.AppendixCause;
        return why != 0 ? why : b.HurtCause;
    }

    /// <summary>이 칸을 덥힌 까닭 (열사병 — DF1): 불 · 구멍 (<see cref="EnvCause"/>) → 달군 장치 · 불씨 → 멈춘 냉각 · 공조 · 원자로 (⑥ — 없으면 0).</summary>
    private int HotCause(Sim sim, int c, Structure? st, Chemistry? chem)
    {
        int e = EnvCause(sim, c, st, chem);
        if (e != 0) return e;
        if (chem != null && chem.HeatCauseAt(c) is int h && h != 0) return h;
        return _net?.HeatCauseAt(sim, c) ?? 0;
    }

    /// <summary>곰팡이 포자의 원인 (DF1): 그 칸 곰팡이의 마디 · 없으면 그 방에서 가장 짙게 핀 칸의 마디.</summary>
    private int SporeCause(int c)
    {
        if (MoldCause[c] != 0) return MoldCause[c];
        int room = L.Room[c], best = 0; float most = 0;
        if (room < 0) return 0;
        foreach (int k in L.Rooms[room].Cells) if (MoldCause[k] != 0 && Mold[k] > most) { most = Mold[k]; best = MoldCause[k]; }
        return best;
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
            double water = net.WaterTank > 100 && net.WaterAtDevice(cr.Device) ? 1 : 0;   // 물관이 끊긴 선반은 물을 못 받는다
            if (lamp < 0.1) cr.DarkHours += (float)(dt / 3600); else cr.DarkHours = 0;
            if (t < 275 || t > 318 || cr.DarkHours > 48 || water == 0) cr.Health = (float)Math.Max(0, cr.Health - dt / 86400);
            else cr.Health = (float)Math.Min(1, cr.Health + dt / (5 * 86400));
            // 양분 몰/초: 재배대 1m²에 하루 0.1몰 (먹는 부분 마른 무게 하루 18g/m² — NASA 생물 재생 실험(BPC)의 밀 · 감자 20g/m² 안팎 · 상추 7g/m²) · 다단 선반은 단마다
            double rate = CropMolPerM2Day / 86400 * CropArea(net, k) * lamp * tf * cf * water * cr.Health;
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

    /// <summary>
    /// 곰팡이 (125틱마다): 상대 습도 80%↑ · 278~310K에서 자라고, 마르면 준다. 핀 칸 (0.05↑)에 원인 마디 — 고인 물이면 그 물의 원인에 잇는다 ·
    /// 곁의 칸이 같은 까닭으로 핀 마디가 있으면 그것을 쓴다 (번진 얼룩 하나에 마디 하나) · 걷히면 0. 방마다 떠다니는 포자 = 그 방 곰팡이 평균 (DF1).
    /// </summary>
    private void MoldStep(Sim sim, double dt)
    {
        Span<int> nb = stackalloc int[4];
        for (int c = 0; c < L.N; c++)
        {
            if (!L.IsSpace(c) || F.RegionOf.Length <= c || F.RegionOf[c] < 0) { Mold[c] = 0; MoldCause[c] = 0; continue; }
            double t = F.Temp(c);
            double rh = F.Partial(T.gH2O, c) / Math.Max(1e-6, Water.SatKPa(Math.Max(250, t)));
            bool pooled = F.Stored(T.sWater, c) > 50, wet = rh > 0.8 || pooled;
            if (wet && t > 278 && t < 310) Mold[c] = (float)Math.Min(1, Mold[c] + Math.Max(Mold[c], 0.01) * 0.7 * dt / 86400);
            else Mold[c] = (float)Math.Max(0, Mold[c] - 0.2 * dt / 86400);
            if (Mold[c] < 0.01f) { MoldCause[c] = 0; continue; }
            if (MoldCause[c] != 0 || Mold[c] < MoldSeen) continue;
            int from = F.WetCause[c], cause = 0;   // 고인 물 · 젖은 바닥의 원인 (마른 칸이면 0 — 습한 공기)
            L.Neighbors(c, nb);
            foreach (int j in nb) if (j >= 0 && MoldCause[j] != 0 && sim.Causes[MoldCause[j]].Parent == from) { cause = MoldCause[j]; break; }
            MoldCause[c] = cause != 0 ? cause : sim.Causes.Add(CauseKind.Biology, pooled ? "고인 물에 곰팡이가 피었다" : "습한 공기에 곰팡이가 피었다", sim.Tick, c, from);
        }
        // 걷힌 얼룩 (어느 칸에도 그 마디가 없다)은 포자를 알린 기억에서 지운다 — 다시 피면 새 마디 · 새 알림
        if (_sporeSeen.Count > 0)
        {
            _moldLive.Clear();
            for (int c = 0; c < L.N; c++) if (MoldCause[c] != 0) _moldLive.Add(MoldCause[c]);
            _sporeSeen.RemoveWhere(k => !_moldLive.Contains((int)k));
        }
        for (int r = 0; r < L.Rooms.Count && r < RoomSpore.Length; r++)
        {
            double sum = 0; int n = 0;
            foreach (int c in L.Rooms[r].Cells) if (L.IsSpace(c)) { sum += Mold[c]; n++; }
            RoomSpore[r] = n > 0 ? (float)(sum / n) : 0;
        }
    }

    /// <summary>음식 상함 (125틱마다): 따뜻하고 습한 칸의 양분이 썩는다 (하루 1% · 10K마다 두 배) — 산소가 거의 없으면 메탄 발효.</summary>
    private void RotStep(Sim sim, double dt)
    {
        for (int c = 0; c < L.N; c++)
        {
            double food = F.Stored(T.sFood, c);
            if (food < 1e-3 || _sealed[c] || F.RegionOf.Length <= c || F.RegionOf[c] < 0) continue;
            double t = F.Temp(c);
            if (t < 277) continue; // 냉장고
            double rh = F.Partial(T.gH2O, c) / Math.Max(1e-6, Water.SatKPa(Math.Max(250, t)));
            if (rh < 0.5 && Mold[c] < 0.1) continue;
            double rate = 0.01 / 86400 * Math.Pow(2, (t - 293) / 10) * (1 + 3 * Mold[c]);
            double xi = Decompose(sim, c, F.Partial(T.gO2, c) < 2 ? _ferment : _rot, food * rate * dt, t);
            if (xi <= 0) continue;
            F.TakeStore(c, T.sFood, xi);
            FoodRotted += xi; sim.Book.Add(Res.Food, Flow.Rotted, -xi);
        }
    }

    /// <summary>
    /// 썩힘 (음식 상함 · 오물): 칸 cell에서 반응 r을 mol몰 일으킨다 — 산소를 쓰는 반응은 칸 산소의 30%까지 · 기체를 빼고 더하고 (온도 temp의 기체) · 반응열.
    /// 실제로 일어난 몰을 돌려준다 (1e-9 아래면 0). 썩은 고체(양분 · 오물)를 빼고 장부에 적는 것은 부른 쪽.
    /// </summary>
    public double Decompose(Sim sim, int cell, Reaction r, double mol, double temp)
    {
        foreach (var x in r.In) if (T.Substances[x.S].Gas == T.gO2) mol = Math.Min(mol, F.Gas(T.gO2, cell) * 0.3 / x.Nu);
        if (mol <= 1e-9) return 0;
        double q = r.Heat(T, temp) * mol;
        foreach (var x in r.In) { var s = T.Substances[x.S]; if (s.Gas >= 0) { F.RemoveGas(cell, s.Gas, x.Nu * mol); if (s.Gas == T.gO2) sim.Book.Add(Res.O2, Flow.Rotted, -x.Nu * mol); } }
        foreach (var x in r.Out) { var s = T.Substances[x.S]; if (s.Gas >= 0) F.AddGas(cell, s.Gas, x.Nu * mol, temp); }
        F.AddHeat(cell, q);
        return mol;
    }

    /// <summary>
    /// ⑧의 요청: 칸들을 닦았다 (청소 · 쏟은 것 닦기 · 소독이 함께 쓴다) — what의 갈래만: 곰팡이를 걷어 낸다 · 고인 물을 닦아 오수 통으로 (재활용 장치가 걸러 다시 쓴다).
    /// 닦아 낸 물 (몰)을 돌려준다.
    /// </summary>
    public double Mop(ReadOnlySpan<int> cells, MopKind what)
    {
        double wiped = 0;
        foreach (int k in cells)
        {
            if (k < 0 || k >= L.N) continue;
            if ((what & MopKind.Mold) != 0) { Mold[k] = 0; MoldCause[k] = 0; }
            if ((what & MopKind.Water) == 0 || _net == null) continue;
            double w = F.Stored(T.sWater, k);
            if (w > 0) { double got = F.TakeStore(k, T.sWater, w); _net.WasteWater += got; wiped += got; }
        }
        return wiped;
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

    public void Save(StateWriter w) { SaveCare(w); w.List(Bodies); w.List(Crops); w.Arr(Mold); w.I64(Deaths); w.F64(FoodRotted); w.F64(O2Used); w.F64(CO2Made); w.F64(MetabolicJ); w.F64(UrineMade); w.Arr(AirLostCause); w.Arr(AirLostTick); w.I64(CratesOpened); w.Tag("씻기"); w.F64(SewageKg); w.I64(Soilings); w.I64(Showers); w.I64(Voids); w.Tag("변기"); w.Arr(Tank); w.Arr(Bowl); w.Arr(Heap); w.Arr(Clogged); w.Arr(ClogCause); w.Arr(Grime); w.Arr(Flushes); w.Arr(ScrubbedAt); w.I64(Clogs); w.I64(Overflows); w.F64(WasteCarried); w.Tag("균 · 포자"); w.Arr(SurfGerm); w.Arr(SurfCause); w.Arr(RoomSpore); w.F64(WasteRotKg); w.I64(Slips); w.Arr(MoldCause); w.Arr(_sporeSeen.OrderBy(x => x).ToArray()); }
    public void Load(StateReader r) { LoadCare(r); Bodies = r.List<Body>(); Crops = r.List<Crop>(); r.ArrInto(Mold); Deaths = r.I64(); FoodRotted = r.F64(); O2Used = r.F64(); CO2Made = r.F64(); MetabolicJ = r.F64(); UrineMade = r.F64(); AirLostCause = r.Arr<int>(); AirLostTick = r.Arr<long>(); CratesOpened = r.I64(); r.Tag("씻기"); SewageKg = r.F64(); Soilings = r.I64(); Showers = r.I64(); Voids = r.I64(); r.Tag("변기"); Tank = r.Arr<double>(); Bowl = r.Arr<double>(); Heap = r.Arr<double>(); Clogged = r.Arr<byte>(); ClogCause = r.Arr<int>(); Grime = r.Arr<float>(); Flushes = r.Arr<int>(); ScrubbedAt = r.Arr<int>(); Clogs = r.I64(); Overflows = r.I64(); WasteCarried = r.F64(); r.Tag("균 · 포자"); SurfGerm = r.Arr<float>(); SurfCause = r.Arr<int>(); r.ArrInto(RoomSpore); WasteRotKg = r.F64(); Slips = r.I64(); r.ArrInto(MoldCause); _sporeSeen.Clear(); foreach (long v in r.Arr<long>()) _sporeSeen.Add(v); }
}

/// <summary>닦는 갈래 (Life.Mop — 겹쳐 쓴다).</summary>
[Flags]
public enum MopKind : byte { None = 0, Mold = 1, Water = 2 }
