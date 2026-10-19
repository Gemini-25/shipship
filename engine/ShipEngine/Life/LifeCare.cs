namespace ShipEngine;

/// <summary>
/// ⑦ 돌봄 · 감염 · 옮는 병 (깊이 바퀴 2).
/// <para><b>돌봄</b>: 마음(⑧)의 요청 — 구급 상자로 붕대 · 화상 덮개 (출혈을 멈추고 · 아픔을 덜고 · 열흘 동안 감염을 막는다 · 곪았거나 화상이 크면 항생제 사흘) ·
/// 상자가 없으면 맨손으로 (피만 멈추고 이틀 덮어 둔다). 덮어 둔 상처는 두 배 · 의료 침대에서는 세 배 빨리 낫는다 (아주 큰 상처는 돌봄을 받아야 낫기 시작한다).</para>
/// <para><b>상처 감염</b>: 덮지 않은 상처는 곪는다 — 하루 확률 = 화상 × 0.5 + 타박 × 0.2 (곰팡이 핀 칸이면 세 배까지) · 곪으면 열(설정점 — 곪음 0.6이면 +1.5℃) ·
/// 항생제가 없으면 사흘 남짓에 패혈증(쓰러진다) · 다시 사흘이면 죽는다 · 항생제는 이틀 남짓에 걷어 낸다. 중증 화상 환자의 사망 원인 1위가 감염이다.</para>
/// <para><b>옮는 병</b> (호흡기 바이러스 — SEIR): 잠복 2일(마지막 날부터 옮긴다) · 앓기 5일(열 +1.3℃) · 나으면 면역.
/// 1분마다 앓는 이 곁 2칸이면 시간당 0.08 · 같은 방이면 0.015로 옮는다 (잠복 중이면 반 · 우주복은 막는다 · 정화기가 돌면 0.7배). 아폴로 7호 승무원이 비행 중 감기를 앓았다.</para>
/// </summary>
public sealed partial class Life
{
    public const double DressHours = 240, BareDressHours = 48, AbxHours = 72;
    public const double IncubateHours = 48, SickHours = 120;
    public const double SpreadNear = 0.08, SpreadRoom = 0.015;   // 시간당 옮을 확률 (앓는 이 곁 2칸 · 같은 방)
    public long Infections, Sepses, Treated, Colds;

    /// <summary>마음의 요청: 돌본다 — kit면 구급 상자 (붕대 · 화상 덮개 · 항생제), 아니면 맨손.</summary>
    public void Treat(Sim sim, int body, bool kit, int abxDays = 3, int cause = 0)
    {
        if (body < 0 || body >= Bodies.Count) return;
        ref var b = ref this[body];
        if (b.Alive == 0) return;
        b.Bleed = 0;
        b.Pain = Math.Max(0, b.Pain - (kit ? 0.4f : 0.15f));
        b.Dressed = (float)Math.Max(b.Dressed, kit ? DressHours : BareDressHours);
        if (kit && abxDays > 0 && NeedsAbx(b)) Give(sim, body, Drug.Abx, 24.0 * abxDays, cause);
        Treated++;
    }

    /// <summary>항생제가 드는 몸 (곪았거나 크게 데었고 · 먹는 중이 아니다).</summary>
    public static bool NeedsAbx(in Body b) => (b.Infect > 0 || b.Burn > 0.5f) && b.Abx <= 0;

    /// <summary>돌봄이 필요한 몸인가: 덮지 않은 큰 상처 · 흐르는 피 · 항생제 없이 곪은 상처 · 약이 드는 몸 (⑦ 시안화수소 중독 · 진통제가 빠진 큰 아픔 — 금단도).</summary>
    public static bool NeedsCare(in Body b) => NeedsWoundCare(b) || NeedsAntidote(b) || NeedsPainkiller(b);

    /// <summary>상처를 돌봐야 하나: 덮지 않은 큰 상처 · 흐르는 피 · 항생제 없이 곪은 상처.</summary>
    public static bool NeedsWoundCare(in Body b) => b.Alive != 0 && (NeedsDressing(b) || b.Infect > 0.2f && b.Abx <= 0);

    /// <summary>덮어야 할 상처 (구급 상자가 드는 일): 덮지 않은 큰 화상 · 타박 · 흐르는 피.</summary>
    public static bool NeedsDressing(in Body b) => b.Dressed <= 0 && (b.Burn > 0.25f || b.Blunt > 0.4f || b.Bleed > 0.05f);

    /// <summary>해독제가 드는 몸 (시안화수소 중독 · 맞지 않았다).</summary>
    public static bool NeedsAntidote(in Body b) => b.Alive != 0 && b.CN > 0.5f && b.Antidote <= 0;

    /// <summary>진통제가 드는 몸 (큰 아픔 · 진통제가 거의 빠졌다 — 금단도).</summary>
    public static bool NeedsPainkiller(in Body b) => b.Alive != 0 && FeltPain(b) > 0.6 && b.Opioid < 2;

    /// <summary>옮는 병을 앓기 시작한다 (식량 상자 · 출항 때 잠복 · 넘어온 이 · 시험) — 잠복부터.</summary>
    public void CatchCold(Sim sim, int body, int cause)
    {
        if (body < 0 || body >= Bodies.Count) return;
        ref var b = ref this[body];
        if (b.Alive == 0 || b.Ill != 0) return;
        b.Ill = 1; b.IllH = 0; b.IllCause = cause; Colds++;
    }

    /// <summary>열 (설정점이 오른 K): 곪음 × 2.5 (많아야 2.5 — 곪음 0.6이면 38.5℃ 언저리) + 앓음 1.3 (38℃ 남짓).</summary>
    public static double FeverK(in Body b) => Math.Min(2.5, b.Infect * 2.5) + (b.Ill == 2 ? 1.3 : 0) + Math.Max(0, b.Appendix);   // ⑦ (라) 맹장염 +1℃ × 맹장

    /// <summary>잃은 몸무게의 몫 (처음 70kg에서): 지방 1몰 256g · 글리코겐 1몰 180g + 붙은 물 3배.</summary>
    public static double WeightLoss(in Body b) => Math.Max(0, (Fat0 - b.Fat) * 0.256 + (GlycogenMax * 0.8 - b.Reserve) * 0.18 * 4) / 70;

    /// <summary>앓는 중인가 (옮기는 병 — 앓음 단계).</summary>
    public static bool Sick(in Body b) => b.Ill == 2;

    /// <summary>
    /// ⑦ 항생제 한 걸음 (순수 — 시험이 바로 돌린다): 먹은 시간이 쌓이고 · 곪음을 이틀에 1씩 걷는다 (내성균이면 0.4배) ·
    /// 다 먹지 않고(48시간 못 미쳐) 끊었는데 곪음이 남았으면 내성균이 된다 (그때 참).
    /// </summary>
    public static bool AbxKinetics(ref Body b, double dt)
    {
        if (b.Abx <= 0) return false;
        b.AbxCourse += (float)(dt / 3600);
        b.Abx = (float)Math.Max(0, b.Abx - dt / 3600);
        if (b.Infect > 0) b.Infect = (float)Math.Max(0, b.Infect - dt / (2 * 86400.0) * (b.Resist != 0 ? 0.4 : 1));
        if (b.Abx > 0 || b.Infect <= 0.05f || b.AbxCourse >= 48 || b.Resist != 0) return false;
        b.Resist = 1;
        return true;
    }

    private void Care(Sim sim, int id, ref Body b, double dt)
    {
        double h = dt / 3600;
        if (b.Dressed > 0) b.Dressed = (float)Math.Max(0, b.Dressed - h);
        bool onAbx = b.Abx > 0;
        if (AbxKinetics(ref b, dt))
        {
            Resistances++;
            b.InfectCause = sim.Causes.Add(CauseKind.Biology, "항생제를 다 먹지 못해 곪음에 내성균이 남았다", sim.Tick, b.Cell, b.DrugCause != 0 ? b.DrugCause : b.InfectCause, b.InfectCause);
        }
        // 곪음
        double wound = b.Burn * 0.5 + b.Blunt * 0.2;
        if (b.Infect <= 0)
        {
            if (b.Dressed <= 0 && b.Abx <= 0 && wound > 0.08)
            {
                double perDay = wound * (1 + 2 * Math.Min(1, Mold[b.Cell]));
                if (Rng.Unit(sim.Seed, Rng.Key("곪음"), id, sim.Tick) < perDay * dt / 86400)
                {
                    b.Infect = 0.02f; Infections++;
                    b.InfectCause = sim.Causes.Add(CauseKind.Biology, "덮지 않은 상처가 곪았다", sim.Tick, b.Cell, b.HurtCause);
                    sim.Facts.Emit(sim.Tick, FactKind.Sick, b.Cell, 1, b.InfectCause, actor: id);
                }
            }
        }
        else
        {
            bool wasSeptic = b.Infect > 1;
            if (!onAbx) b.Infect += (float)(dt / (3 * 86400.0) * (0.5 + b.Burn + 0.5 * b.Blunt) * (OnTherapy(sim, b.Cell) ? 0.5 : 1));
            if (!wasSeptic && b.Infect > 1) Sepses++;
        }
        // 옮는 병의 단계
        if (b.Ill is 1 or 2)
        {
            b.IllH += (float)h;
            if (b.Ill == 1 && b.IllH >= IncubateHours)
            {
                b.Ill = 2; b.IllH = 0;
                int c = sim.Causes.Add(CauseKind.Biology, "열이 나고 기침을 한다 — 감기를 앓는다", sim.Tick, b.Cell, b.IllCause);
                b.IllCause = c;
                sim.Facts.Emit(sim.Tick, FactKind.Sick, b.Cell, 2, c, actor: id);
            }
            else if (b.Ill == 2 && b.IllH >= SickHours) { b.Ill = 3; b.IllH = 0; }
        }
    }

    /// <summary>1분마다: 앓는 이(잠복 마지막 날부터) 곁 · 같은 방에서 옮는다.</summary>
    private void Spread(Sim sim, double dt)
    {
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Bodies);
        double air = _net != null && _net.ScrubberWorking() ? 0.7 : 1;
        for (int i = 0; i < span.Length; i++)
        {
            ref var s = ref span[i];
            if (s.Alive == 0 || !(s.Ill == 2 || s.Ill == 1 && s.IllH >= IncubateHours - 24) || s.Suit != 0) continue;
            int ri = L.Room[s.Cell];
            double shed = s.Ill == 2 ? 1 : 0.5;
            for (int j = 0; j < span.Length; j++)
            {
                if (j == i) continue;
                ref var o = ref span[j];
                if (o.Alive == 0 || o.Ill != 0 || o.Suit != 0) continue;
                int d = Math.Abs(L.X(o.Cell) - L.X(s.Cell)) + Math.Abs(L.Y(o.Cell) - L.Y(s.Cell));
                double rate = d <= 2 ? SpreadNear : ri >= 0 && L.Room[o.Cell] == ri ? SpreadRoom : 0;
                if (rate <= 0) continue;
                if (Rng.Unit(sim.Seed, Rng.Key("옮음"), i * 4096 + j, sim.Tick) >= rate * shed * air * dt / 3600) continue;
                o.Ill = 1; o.IllH = 0; Colds++;
                o.IllCause = sim.Causes.Add(CauseKind.Biology, d <= 2 ? "앓는 이 곁에 있다가 감기가 옮았다" : "앓는 이와 같은 방에 있다가 감기가 옮았다", sim.Tick, o.Cell, s.IllCause);
            }
        }
    }

    private void SaveCare(StateWriter w) { w.I64(Infections); w.I64(Sepses); w.I64(Treated); w.I64(Colds); }
    private void LoadCare(StateReader r) { Infections = r.I64(); Sepses = r.I64(); Treated = r.I64(); Colds = r.I64(); }
}
