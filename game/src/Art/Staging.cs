using System;
using System.Collections.Generic;
using ShipSim.Core;

namespace ShipSim.Art;

/// <summary>시작 소리 (사고가 처음 났을 때 한 번).</summary>
public enum StageSound : byte { None, Chirp, Siren, Zap, Hiss, Thud, Creak, Clang, Splash, Squelch, Glitch, Rumble, Whoosh, Boom }

/// <summary>카메라: 없음 · 살짝 흔들 · 흔들 · 흔들고 잠깐 느리게.</summary>
public enum StageCam : byte { None, Nudge, Shake, Slow }

/// <summary>연출 하나: 소리 · 카메라 · 화면에 무엇이 보이나(설명).</summary>
public readonly record struct StageCue(StageSound Sound, StageCam Cam, string Visual);

/// <summary>
/// 연출 장부 (도트 8단계): 모든 사고 · 우주 재난 · 폭발이 어떤 소리 · 카메라 · 그림으로 보이는지 한 곳에.
/// 빠진 것이 없는지는 headless --stagingtest 가 센다 (하나라도 빠지면 실패).
/// </summary>
public static class Staging
{
    private static readonly Dictionary<HazardKind, StageCue> Hz = new();
    private static readonly Dictionary<CosmicKind, StageCue> Cz = new();
    /// <summary>두 번 적힌 것 (장부 실수 — 시험이 잡는다).</summary>
    public static readonly List<string> Duplicates = new();

    private static void H(StageSound s, StageCam c, string v, params HazardKind[] ks)
    {
        foreach (var k in ks) { if (Hz.ContainsKey(k)) Duplicates.Add(k.ToString()); Hz[k] = new StageCue(s, c, v); }
    }

    private static void C(StageSound s, StageCam c, string v, params CosmicKind[] ks)
    {
        foreach (var k in ks) { if (Cz.ContainsKey(k)) Duplicates.Add(k.ToString()); Cz[k] = new StageCue(s, c, v); }
    }

    static Staging()
    {
        // ── 사고 100 ──
        H(StageSound.Thud, StageCam.Shake, "운석 섬광 · 외벽 파공 · 새는 공기 알갱이 · 빨려 가는 줄",
            HazardKind.MeteorShower, HazardKind.MicroShower, HazardKind.DebrisCloud, HazardKind.DebrisAlert, HazardKind.CometTail);
        H(StageSound.Zap, StageCam.Nudge, "고장 불꽃 · 꺼진 설비 그늘 · 깜빡이는 조명 · 탄 점",
            HazardKind.PowerSurge, HazardKind.ArcFault, HazardKind.GroundFault, HazardKind.BreakerCascade, HazardKind.StaticDischarge,
            HazardKind.StaticZap, HazardKind.HeaterOverload, HazardKind.PumpShock, HazardKind.CellAging, HazardKind.TrunkSag);
        H(StageSound.Hiss, StageCam.Nudge, "가스 안개 · 새는 공기 알갱이 · 서리 낀 씰",
            HazardKind.GasLeak, HazardKind.OxygenLeak, HazardKind.HydrogenBuildup, HazardKind.Co2Spike, HazardKind.ScrubberSaturation,
            HazardKind.Ozone, HazardKind.SealLeak, HazardKind.GasTankRupture, HazardKind.HatchSeal, HazardKind.DockSealFail);
        H(StageSound.Whoosh, StageCam.Nudge, "도트 불꽃 · 연기 덩이 · 그을음",
            HazardKind.DuctFire, HazardKind.GreaseFire, HazardKind.DryerFire, HazardKind.CableTrayFire, HazardKind.Smolder,
            HazardKind.PotFire, HazardKind.MoonshineFire, HazardKind.InsulationSmoke);
        H(StageSound.Splash, StageCam.None, "고인 물(물결 · 반짝임) · 결로 방울 · 갈색 고리 · 서리",
            HazardKind.CondensateFlood, HazardKind.SewageBackup, HazardKind.Backflow, HazardKind.DrainBackflow, HazardKind.GreywaterJam,
            HazardKind.PipeFreeze, HazardKind.ValveSeize, HazardKind.TankSludge, HazardKind.WaterContamination, HazardKind.HumiditySpike,
            HazardKind.CoolantLoss, HazardKind.FreezerFailure);
        H(StageSound.Creak, StageCam.Nudge, "균열 · 찌그러짐 · 갈리는 설비 · 넘어진 판 · 지렛대 자국",
            HazardKind.HullCrack, HazardKind.WindowCrack, HazardKind.WeldFatigue, HazardKind.ThermalStress, HazardKind.FrameCreak,
            HazardKind.InsulationCrack, HazardKind.BearingSeize, HazardKind.FanImbalance, HazardKind.ShaftMisalign, HazardKind.BearingWhine,
            HazardKind.LooseMount);
        H(StageSound.Clang, StageCam.Shake, "떨어진 짐 · 넘어진 칸막이 · 열린 뚜껑 · 갇힌 문",
            HazardKind.HoistDrop, HazardKind.PartitionFall, HazardKind.HatchFall, HazardKind.DoorJam, HazardKind.LockedIn);
        H(StageSound.Squelch, StageCam.None, "병든 작물 · 곰팡이 점 · 상한 그릇 · 파리 · 토한 자국",
            HazardKind.CropBlight, HazardKind.MoldOutbreak, HazardKind.SeedRot, HazardKind.NutrientCrash, HazardKind.SkinFungus,
            HazardKind.FoodPoisoning, HazardKind.Epidemic, HazardKind.BadBatch, HazardKind.WeevilSwarm, HazardKind.FermentBurst,
            HazardKind.ScaldSpill, HazardKind.SpaceSick);
        H(StageSound.Glitch, StageCam.None, "깨진 화면 · 다시 켜짐 줄무늬 · 빈 기록 띠 · 꺼진 조명 · 멈춘 로봇",
            HazardKind.ComputerFault, HazardKind.ComputerMisjudge, HazardKind.RobotMalfunction, HazardKind.RebootGlitch,
            HazardKind.BlackboxGap, HazardKind.LightsOut);
        H(StageSound.Siren, StageCam.Slow, "노심 빛 · 달아오름 · 아지랑이 · 경보 카드",
            HazardKind.ReactorTransient, HazardKind.Overheat);
        H(StageSound.Rumble, StageCam.Nudge, "화면 잡음 · 창의 하얀 줄 · 들러붙은 공구 · 오로라",
            HazardKind.SolarStorm, HazardKind.RadiationBurst, HazardKind.IonStorm, HazardKind.GammaFlash, HazardKind.MagnetarPulse);
        H(StageSound.Thud, StageCam.Shake, "흩어진 물건 · 부딪힌 자국 · 미끄러진 자국 · 당김 금 · 잔해 궤적",
            HazardKind.GravityHiccup, HazardKind.GravityFailure, HazardKind.ManeuverJolt, HazardKind.TidalPull, HazardKind.WreckDrift);
        H(StageSound.Chirp, StageCam.None, "다친 몸(건강 카드) · 넘어진 의자 · 할퀸 자국 · 깨진 화분 · 쪽지",
            HazardKind.WorkAccident, HazardKind.PanicAttack, HazardKind.MedError, HazardKind.HeatExhaustion, HazardKind.CatScratch,
            HazardKind.CatLost, HazardKind.MeetingBrawl, HazardKind.PlantTopple, HazardKind.RescueSignal);

        // ── 우주 재난 30 (고유 장면은 ShipViewCosmic · Cosmic2) ──
        C(StageSound.Rumble, StageCam.Slow, "다가오는 빛 · 화면 가장자리 물듦 · 고유 장면 · 그림자 쪽 대피",
            CosmicKind.Supernova, CosmicKind.GammaBurst, CosmicKind.SuperFlare, CosmicKind.CoronalMass, CosmicKind.PulsarBeam,
            CosmicKind.MagnetarStorm, CosmicKind.NeutronStar, CosmicKind.BlackHoleTide, CosmicKind.RedGiantShell, CosmicKind.BinaryEclipse,
            CosmicKind.WolfRayetWind);
        C(StageSound.Thud, StageCam.Shake, "다가오는 천체 · 충돌 섬광 · 파공 · 스치는 잔해",
            CosmicKind.BigAsteroid, CosmicKind.CometCore, CosmicKind.PlanetRing, CosmicKind.ShatteredPlanet, CosmicKind.Kessler,
            CosmicKind.HyperDust, CosmicKind.RoguePlanet);
        C(StageSound.Siren, StageCam.Shake, "멀리 번쩍임 · 충격파 · 날아오는 파편 · 함대 · 막은 문",
            CosmicKind.ReactorBlast, CosmicKind.StationCollapse, CosmicKind.AntimatterBreach, CosmicKind.FusionRunaway, CosmicKind.MineField,
            CosmicKind.OrbitalEmp, CosmicKind.Freighter, CosmicKind.PirateFleet);
        C(StageSound.Glitch, StageCam.Nudge, "어두워짐 · 화면 잡음 · 이온 빛 · 출렁이는 배",
            CosmicKind.DarkNebula, CosmicKind.CosmicRayShower, CosmicKind.IonNebula, CosmicKind.GravityWave);
    }

    public static bool Has(HazardKind k) => Hz.ContainsKey(k);
    public static bool Has(CosmicKind k) => Cz.ContainsKey(k);
    public static StageCue Of(HazardKind k) => Hz.TryGetValue(k, out var c) ? c : default;
    public static StageCue Of(CosmicKind k) => Cz.TryGetValue(k, out var c) ? c : default;

    /// <summary>폭발 22가지: 소리는 BlastSound(종류 여섯) · 그림은 도트 불덩이(색 여섯) · 큰 것은 흔들고 느리게.</summary>
    public static StageCue Of(BlastKind k) => k switch
    {
        BlastKind.Hydrogen => new(StageSound.Boom, StageCam.Slow, "파란 불덩이 · 짧은 섬광 · 충격파"),
        BlastKind.Battery => new(StageSound.Boom, StageCam.Slow, "흰 불꽃 · 누런 연기 · 튀는 셀"),
        BlastKind.Arc => new(StageSound.Boom, StageCam.Shake, "아크 번개 · 파란 불덩이"),
        BlastKind.Steam or BlastKind.Refrigerant or BlastKind.ColdGas or BlastKind.Powder or BlastKind.Aerosol
            => new(StageSound.Boom, StageCam.Shake, "하얀 분출 구름 · 서리 · 분말"),
        BlastKind.Dust => new(StageSound.Boom, StageCam.Slow, "번지는 노란 불길"),
        BlastKind.Ferment => new(StageSound.Boom, StageCam.Nudge, "붉은 국물 튀김 · 깨진 조각"),
        BlastKind.Grease => new(StageSound.Boom, StageCam.Shake, "기름 불기둥"),
        BlastKind.Flare => new(StageSound.Boom, StageCam.Shake, "붉은 불똥 · 떨어지는 불씨"),
        BlastKind.Charge => new(StageSound.Boom, StageCam.Slow, "짧은 흰빛 · 작은 불덩이 · 센 충격파"),
        _ => new(StageSound.Boom, StageCam.Slow, "주황 불덩이 · 검은 연기 기둥 · 파편 · 그을음"),
    };
}

/// <summary>시작 소리 합성 (22050Hz 모노 · 순수 C# — 게임은 이걸 소리로, 시험은 비어 있지 않은지 센다).</summary>
public static class StageSounds
{
    public const int Rate = 22050;

    public static float[] Make(StageSound k)
    {
        float len = k switch
        {
            StageSound.Chirp => 0.35f, StageSound.Siren => 0.9f, StageSound.Zap => 0.5f, StageSound.Hiss => 0.8f, StageSound.Thud => 0.5f,
            StageSound.Creak => 0.9f, StageSound.Clang => 1.0f, StageSound.Splash => 0.7f, StageSound.Squelch => 0.5f, StageSound.Glitch => 0.5f,
            StageSound.Rumble => 1.4f, StageSound.Whoosh => 0.8f, StageSound.Boom => 0.8f, _ => 0f,
        };
        int n = (int)(Rate * len);
        var s = new float[n];
        if (n == 0) return s;
        var rng = new Random(311 + (int)k);
        float ph = 0f, lp = 0f, lp2 = 0f, prev = 0f, crackle = 0f, held = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, u = t / len;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            float env = MathF.Min(1f, t * 80f) * MathF.Min(1f, (len - t) * 20f);
            float x = 0f;
            switch (k)
            {
                case StageSound.Chirp: // 두 음 짧은 알림
                    ph += MathF.Tau * (t < 0.15f ? 880f : 1320f) / Rate;
                    x = 0.6f * MathF.Sin(ph) * (t < 0.13f || t > 0.17f ? 1f : 0f) * MathF.Exp(-((t % 0.17f) * 9f));
                    break;
                case StageSound.Siren: // 오르내리는 경보
                    ph += MathF.Tau * (600f + 600f * (0.5f - 0.5f * MathF.Cos(u * MathF.Tau))) / Rate;
                    x = 0.5f * MathF.Sign(MathF.Sin(ph)) * 0.6f + 0.3f * MathF.Sin(ph);
                    break;
                case StageSound.Zap: // 지직 + 웅 소리
                    crackle = rng.NextDouble() < 0.05 * MathF.Exp(-t * 3f) ? 1f : crackle * 0.8f;
                    x = 0.8f * crackle * noise + 0.25f * MathF.Sin(t * MathF.Tau * 120f) * MathF.Exp(-t * 4f);
                    break;
                case StageSound.Hiss: // 쉬이익 (높은 소음이 부풀었다 잦아듦)
                    float hp = noise - prev; prev = noise;
                    x = 0.5f * hp * MathF.Sin(MathF.PI * u);
                    break;
                case StageSound.Thud: // 쿵 (낮은 사인 + 딸깍)
                    ph += MathF.Tau * (60f - 20f * u) / Rate;
                    x = 0.9f * MathF.Sin(ph) * MathF.Exp(-t * 9f) + 0.5f * noise * MathF.Exp(-t * 80f);
                    break;
                case StageSound.Creak: // 쇠가 끼익 (흔들리는 톱니파를 거른다)
                    ph += MathF.Tau * (85f + 25f * MathF.Sin(t * 7f) + 10f * MathF.Sin(t * 23f)) / Rate;
                    float saw = (ph / MathF.Tau % 1f) * 2f - 1f;
                    lp += 0.08f * (saw - lp);
                    x = 0.8f * lp * MathF.Sin(MathF.PI * u);
                    break;
                case StageSound.Clang: // 쾅 울리는 쇠 (어긋난 배음)
                    x = 0.35f * MathF.Sin(t * MathF.Tau * 523f) * MathF.Exp(-t * 3f) + 0.25f * MathF.Sin(t * MathF.Tau * 1187f) * MathF.Exp(-t * 4.5f)
                        + 0.18f * MathF.Sin(t * MathF.Tau * 1840f) * MathF.Exp(-t * 6f) + 0.12f * MathF.Sin(t * MathF.Tau * 2650f) * MathF.Exp(-t * 8f)
                        + 0.4f * noise * MathF.Exp(-t * 60f);
                    break;
                case StageSound.Splash: // 철벅 (거른 소음 + 물방울 소리)
                    lp += 0.2f * (noise - lp);
                    x = 0.8f * lp * MathF.Exp(-t * 5f);
                    if (rng.NextDouble() < 0.004) held = 1f;
                    held *= 0.997f;
                    x += 0.3f * held * MathF.Sin(t * MathF.Tau * (500f + 400f * held));
                    break;
                case StageSound.Squelch: // 꾸르륵 (낮게 출렁)
                    ph += MathF.Tau * (130f + 70f * MathF.Sin(t * 18f)) / Rate;
                    x = 0.6f * MathF.Sin(ph) * (0.5f + 0.5f * MathF.Sin(t * 31f));
                    break;
                case StageSound.Glitch: // 뚝뚝 끊기는 전자음 (높이가 계단처럼 바뀐다)
                    int step = (int)(t * 24f);
                    float f = 300f + 900f * ((step * 7919 % 13) / 13f);
                    ph += MathF.Tau * f / Rate;
                    x = 0.4f * MathF.Sign(MathF.Sin(ph)) * ((step & 3) != 3 ? 1f : 0f);
                    x = MathF.Round(x * 4f) / 4f;
                    break;
                case StageSound.Rumble: // 우르릉 (낮은 소음이 천천히 부풀었다 잦아듦)
                    lp += 0.03f * (noise - lp); lp2 += 0.03f * (lp - lp2);
                    x = 9f * lp2 * MathF.Sin(MathF.PI * u);
                    break;
                case StageSound.Whoosh: // 훅 (걸러지는 폭이 열렸다 닫힘)
                    float cut = 0.03f + 0.4f * MathF.Sin(MathF.PI * u);
                    lp += cut * (noise - lp);
                    x = 0.8f * lp * MathF.Sin(MathF.PI * u);
                    break;
                case StageSound.Boom:
                    ph += MathF.Tau * (70f - 35f * u) / Rate;
                    lp += 0.05f * (noise - lp);
                    x = 0.8f * MathF.Sin(ph) * MathF.Exp(-t * 4f) + 3f * lp * MathF.Exp(-t * 2f) + 0.6f * noise * MathF.Exp(-t * 50f);
                    break;
            }
            s[i] = x * env;
        }
        float peak = 1e-4f;
        foreach (var v in s) peak = MathF.Max(peak, MathF.Abs(v));
        for (int i = 0; i < n; i++) s[i] = Math.Clamp(s[i] * 0.85f / peak, -1f, 1f);
        return s;
    }
}
