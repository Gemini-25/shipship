namespace ShipEngine;

/// <summary>⑦ (다) 숨은 병 — 진단이 맞히려는 것 (가장 급한 하나). 체함 · 잠은 참으로는 없다 — 모르는 눈이 잘못 보는 것.</summary>
public enum Ail : byte { None, Cold, Infection, CO, Cyanide, Overdose, Liver, Heart, Radiation, Indigestion, Sleep, InternalBleed, Appendicitis, Bruise }

/// <summary>⑦ (다) 증상 — 몸에서 보이는 것.</summary>
[Flags]
public enum Sym : ushort
{
    None = 0, Fever = 1, Headache = 2, Nausea = 4, Breathless = 8, Confused = 16, Cough = 32, ChestPain = 64, Drowsy = 128, Weak = 256, Jaundice = 512, Pale = 1024,
    /// <summary>돌봄을 부를 만한 증상 (기운 없음 · 졸림 · 기침만으로는 부르지 않는다).</summary>
    Serious = Fever | Headache | Nausea | Breathless | Confused | ChestPain | Jaundice | Pale,
}

/// <summary>
/// ⑦ 진단 (깊이 바퀴 3 (다) — docs/ENGINE.md '장기 · 약 · 수술'). 몸에서 보이는 증상 · 참인 숨은 병 · 모르는 눈에 닮아 보이는 병 · 맞힐 몫.
/// <para>돌보는 이(⑧ 마음)는 증상만 본다 — 몸 지식과 도구(의료 침대의 피 검사 · 진단 장치 · 로봇의 기체 센서)만큼 맞히고 · 틀리면 닮은 병으로 본다.</para>
/// </summary>
public sealed partial class Life
{
    /// <summary>몸에서 보이는 증상.</summary>
    public static Sym Symptoms(in Body b)
    {
        if (b.Alive == 0) return Sym.None;
        var s = Sym.None;
        if (FeverK(b) * (1 - FeverCut(b)) > 0.5) s |= Sym.Fever;
        if (b.COHb > 0.08f || b.CN > 0.2f || b.Ill == 2) s |= Sym.Headache;
        if (b.COHb > 0.12f || b.Liver > 0.2f || b.RadRecent > 1 || OpioidEffect(b) > 15) s |= Sym.Nausea;
        if (b.Appendix > 0.3f) s |= Sym.Nausea;
        if (b.SpO2 < 0.85f || b.LungDamage > 0.3f || b.Heart > 0.35f || b.BloodL < 4) s |= Sym.Breathless;
        if (b.Hypoxia > 0.2f || b.COHb > 0.25f || b.CN > 0.4f || b.Brain > 0.1f || b.BloodL < 3.5f) s |= Sym.Confused;
        if (b.Ill == 2 || b.LungDamage > 0.2f) s |= Sym.Cough;
        if (b.Heart > 0.2f) s |= Sym.ChestPain;
        if (Sedation(b) > 0.3 || b.COHb > 0.2f) s |= Sym.Drowsy;
        if (b.RadRecent > 0.5 || b.Heart > 0.2f || b.Liver > 0.3f || b.Ill == 2 || b.Infect > 0.3f || b.BleedIn > 0.05f) s |= Sym.Weak;
        if (b.Liver > 0.5f) s |= Sym.Jaundice;
        if (b.BleedIn > BleedInSelfStop || b.BloodL < 4.2f) s |= Sym.Pale;   // 창백하고 어지럽다 (속 출혈 · 피를 잃었다)
        return s;
    }

    /// <summary>참인 숨은 병 (가장 급한 하나): 시안화수소 > 일산화탄소 > 속 출혈 > 진통제 과용 > 맹장염 > 곪음 > 간 > 심장 > 방사선 > 감기.</summary>
    public static Ail Ailment(in Body b)
    {
        if (b.Alive == 0) return Ail.None;
        if (b.CN > 0.3f) return Ail.Cyanide;
        if (b.COHb > 0.08f) return Ail.CO;
        if (b.BleedIn > BleedInSelfStop) return Ail.InternalBleed;
        if (RespDepression(b) > 0.05) return Ail.Overdose;
        if (b.Appendix > 0.2f) return Ail.Appendicitis;
        if (b.Infect > 0.2f) return Ail.Infection;
        if (b.Liver > 0.2f) return Ail.Liver;
        if (b.Heart > 0.2f) return Ail.Heart;
        if (b.RadRecent > 1) return Ail.Radiation;
        if (b.Ill == 2) return Ail.Cold;
        return Ail.None;
    }

    /// <summary>모르는 눈에 닮아 보이는 병: 시안화수소 → 일산화탄소 · 일산화탄소 → 감기 (두통 · 메스꺼움 — 열은 없다) · 과용 → 잠 · 곪음 → 감기 · 간 · 심장 → 체함 · 방사선 → 감기.</summary>
    public static Ail LookAlike(Ail a) => a switch
    {
        Ail.Cyanide => Ail.CO, Ail.CO => Ail.Cold, Ail.Overdose => Ail.Sleep, Ail.Infection => Ail.Cold,
        Ail.Liver => Ail.Indigestion, Ail.Heart => Ail.Indigestion, Ail.Radiation => Ail.Cold,
        Ail.InternalBleed => Ail.Bruise, Ail.Appendicitis => Ail.Indigestion, _ => a,
    };

    /// <summary>병마다 맞힐 바탕 (증상만 보고 · 아무것도 모르는 이).</summary>
    public static double DxBase(Ail a) => a switch
    {
        Ail.Cold => 0.9, Ail.Infection => 0.4, Ail.Appendicitis => 0.35, Ail.Overdose => 0.3, Ail.Heart => 0.25, Ail.Radiation => 0.2, Ail.InternalBleed => 0.2, Ail.CO => 0.15, Ail.Liver => 0.1, Ail.Cyanide => 0.1, _ => 1,
    };

    /// <summary>
    /// 맞힐 몫: 바탕 + 0.6 × 몸 지식 + 도구 — 의료 침대(피 검사 · 혈액 가스: 일산화탄소 · 시안화수소 · 과용 · 곪음 · 간 · 속 출혈 · 맹장염) +0.5 ·
    /// 진단 장치(심전도 · 몸 훑기: 모두) +0.6 · 기체 센서(그 칸 공기의 일산화탄소 · 시안화수소) +0.5. 덮지 않은 상처의 고름은 누구나 본다. 많아야 0.98.
    /// </summary>
    /// <summary>⑤ 통신 지연 (가) 원격 진료 — 고향 관제 의사가 증상 기록 · 생체 신호만 보고 맞힐 몫 (진단 장치 기록이 있으면 더 · 시험이 바꾼다).</summary>
    public double HomeDxChance = 0.85, HomeDxScanChance = 0.95;
    public long HomeDxAnswered;

    /// <summary>배가 보내는 생체 신호 (그 몸의 지금 병 — 보낸 때의 것이 고향에 닿는다).</summary>
    public Ail Telemetry(int body) => body >= 0 && body < Bodies.Count ? Ailment(Bodies[body]) : Ail.None;

    /// <summary>고향 관제 의사의 답: 보낸 생체 신호로 (세상의 확률 · 난수 갈래 '원격 진료') — 틀리면 닮은 병으로.</summary>
    public Ail HomeDx(Sim sim, Ail sent, bool scan, int body)
    {
        HomeDxAnswered++;
        if (sent is Ail.None or Ail.Cold) return sent;
        bool right = Rng.Unit(sim.Seed, Rng.Key("원격 진료"), sim.Tick, body) < (scan ? HomeDxScanChance : HomeDxChance);
        return right ? sent : LookAlike(sent);
    }

    public static double DxChance(Ail a, in Body b, double knBody, bool bed, bool scanner, bool sniff)
    {
        if (a is Ail.None or Ail.Cold) return 1;
        double p = DxBase(a) + 0.6 * knBody;
        if (a == Ail.Infection && b.Dressed <= 0 && b.Burn + b.Blunt > 0.1f) p = 0.95;   // 고름이 보인다
        if (bed && a is Ail.CO or Ail.Cyanide or Ail.Overdose or Ail.Infection or Ail.Liver or Ail.InternalBleed or Ail.Appendicitis) p += 0.5;   // 피 검사 (핏빛 수치 · 흰피톨)
        if (scanner) p += 0.6;
        if (sniff && a is Ail.CO or Ail.Cyanide) p += 0.5;
        return Math.Min(0.98, p);
    }

    public static string AilName(Ail a) => a switch
    {
        Ail.Cold => "감기", Ail.Infection => "곪음", Ail.CO => "일산화탄소 중독", Ail.Cyanide => "시안화수소 중독", Ail.Overdose => "진통제 과용",
        Ail.Liver => "간이 상함", Ail.Heart => "상한 심장", Ail.Radiation => "방사선병", Ail.Indigestion => "체함", Ail.Sleep => "그냥 잠든 것",
        Ail.InternalBleed => "속 출혈", Ail.Appendicitis => "맹장염", Ail.Bruise => "타박상", _ => "아픈 데 없음",
    };

    /// <summary>증상 글 (많아야 셋 — 진료 기록 · 원인 마디).</summary>
    public static string SymText(Sym s)
    {
        var parts = new List<string>(3);
        void Add(Sym f, string t) { if ((s & f) != 0 && parts.Count < 3) parts.Add(t); }
        Add(Sym.Confused, "헷갈려 함"); Add(Sym.ChestPain, "가슴 아픔"); Add(Sym.Pale, "창백함"); Add(Sym.Breathless, "숨참"); Add(Sym.Jaundice, "노란 눈");
        Add(Sym.Headache, "두통"); Add(Sym.Nausea, "메스꺼움"); Add(Sym.Fever, "열"); Add(Sym.Cough, "기침"); Add(Sym.Drowsy, "졸림"); Add(Sym.Weak, "기운 없음");
        return parts.Count == 0 ? "뚜렷한 증상 없음" : string.Join(" · ", parts);
    }
}
