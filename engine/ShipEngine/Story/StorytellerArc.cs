namespace ShipEngine;

/// <summary>
/// ⑫ 이야기꾼 (깊이 바퀴 2) — 막의 리듬 · 배의 형편 · 이야기의 실 · 헛도는 씨앗 · 사람 씨앗.
/// <para><b>막</b>: 조짐(작은 씨앗 1~4개 — 낡은 부품 · 곰팡이 · 실수 · 앙금 · 감기 · 다침 · 짝사랑 · 숨긴 실수) → 절정(씨앗 하나 — 가혹함 6부터는 늘 큰 씨앗(운석 · 파편 구름 · 별 폭발 · 외판 피로 · 합선 불씨) · 4는 60% · 2는 20%,
/// 막을 거듭할수록 커진다 · 많아야 1.55배) → 여파(성격마다 4~14시간 × (4/가혹함)^1.5 · 많아야 48시간 동안 심지 않는다). 죽음 · 쓰러짐 · 벽이 터짐 · 문이 날아감은 막이 어디에 있든 곧장 여파로 —
/// 무너진 배를 더 때리지 않는다. 죽음의 여파에는 (가혹함 6까지) 두 시간 뒤 고향의 반가운 소식 하나.</para>
/// <para><b>배의 형편</b> (자비): 사람마다 형편 무게(쓰러짐 · 돌봄이 필요함 1 · 앓음 · 덮어 둔 큰 상처 0.6 · 덮어 둔 작은 상처 0.2)의 평균 + 이틀 안의 죽음 0.3 + 식량 이레 치 아래 0.3 — 가혹함 1~3은 한 사람이라도 크게 다쳤으면 쉬고 ·
/// 4~6은 형편이 0.3을 넘으면 쉬고 · 그 아래면 바라는 긴장을 자비(0.8 · 0.6 · 0.45)만큼 낮춘다 · 7은 0.25만큼 · 8 '끝까지'는 봐주지 않는다.</para>
/// <para><b>이야기의 실</b>: 다친 · 쓰러진 · 탓한 · 싸운 · 사이가 바뀐 · 슬퍼하는 · 씨앗에 닿은 사람은 실이 굵어진다 (이틀 반감) — 사람 씨앗은 실이 굵은 사람을 더 고른다 (주인공이 생긴다).</para>
/// <para><b>헛도는 씨앗</b>: 씨앗마다 그 뒤로 원인 사슬의 뿌리가 그 씨앗인 볼 만한 사실이 났나를 따진다 (종류마다 1~72시간) — 헛돌면 그 종류를 0.6배 (많아야 0.25까지) ·
/// 열매를 맺으면 1.2배 (많아야 2) 고른다. 열매를 맺은 씨앗의 대기 긴장은 덜어 낸다 (이제 진짜 긴장에 들어 있다).</para>
/// </summary>
public sealed partial class Storyteller
{
    public const byte ActBuild = 0, ActClimax = 1, ActAfter = 2;
    public const int KindCount = 16;
    /// <summary>지금 막 (조짐 · 절정 · 여파).</summary>
    public byte Act;
    /// <summary>여파가 끝나는 때 · 여파가 시작된 때.</summary>
    public long ActUntil, AfterFrom;
    /// <summary>끝난 막의 수 (절정을 심을 때마다).</summary>
    public int Arc;
    /// <summary>이 막에서 남은 조짐 씨앗.</summary>
    public byte BuildLeft = 1;
    /// <summary>여파에 반가운 소식을 줄 차례 (죽음 뒤).</summary>
    public byte NewsDue;
    public long DeathTick = long.MinValue / 2;
    /// <summary>사람마다 이야기의 실 (agent 번호).</summary>
    public float[] Thread = Array.Empty<float>();
    /// <summary>씨앗 종류마다 이 배에서 얼마나 열매를 맺었나 (고르는 무게에 곱한다).</summary>
    public float[] KindTrust = Enumerable.Repeat(1f, KindCount).ToArray();
    /// <summary>형편을 보고 쉰 생각 · 큰 일로 든 여파 · 헛돈 씨앗 · 열매 맺은 씨앗.</summary>
    public long Mercies, Aftermaths, Fizzled, Fruited;
    private readonly Dictionary<int, int> _seedOf = new();   // 씨앗 원인 → Seeds 번호 (찍기에 안 넣는다 — 되살릴 때 다시 만든다)
    private Mind? _mind;

    /// <summary>절정의 크기: 1.15 + 막마다 0.08 (많아야 1.55).</summary>
    public double ClimaxBoost => 1.15 + 0.08 * Math.Min(5, Arc);

    /// <summary>절정이 큰 씨앗(운석 · 파편 구름 · 별 폭발 · 외판 피로 · 합선 불씨)일 몫 — 잔잔한 항해의 절정은 재난이 아니어도 된다.</summary>
    public double BigShare => Math.Clamp((Level - 1) / 5.0, 0.15, 1);

    /// <summary>조짐 씨앗 사이 (시간).</summary>
    public double BuildGapHours => RestHours * 0.5;

    /// <summary>여파 (시간) — 성격 · 가혹함에 따라 (가혹할수록 가파르게 짧다 · 많아야 48시간).</summary>
    public double AfterHours => Math.Min(48, Style switch { StoryStyle.Gentle => 14, StoryStyle.Relentless => 4, StoryStyle.SlowBurn => 10, _ => 8 } * Math.Pow(4.0 / Math.Max(1, (int)Level), 1.5));

    /// <summary>자비: 형편만큼 바라는 긴장을 낮추는 몫.</summary>
    public double Mercy => Level switch { <= 3 => 1, 4 => 0.8, 5 => 0.6, 6 => 0.45, 7 => 0.25, _ => 0 };

    public static string ActName(byte act) => act switch { ActBuild => "조짐", ActClimax => "절정", _ => "여파" };

    public float ThreadOf(int agent) => agent >= 0 && agent < Thread.Length ? Thread[agent] : 0;

    private void Involve(int agent, float w)
    {
        if (agent < 0) return;
        if (agent >= Thread.Length) Array.Resize(ref Thread, Math.Max(agent + 1, _mind?.Agents.Count ?? 0));
        Thread[agent] = Math.Min(3, Thread[agent] + w);
    }

    // ───────────────────────── 사실을 읽는다 ─────────────────────────

    /// <summary>볼 만한 사실인가 (씨앗의 열매로 친다) — 경보 · 차단기 · 씨앗 자신은 빼고.</summary>
    private static bool Telling(FactKind k) => k is not (FactKind.StorySeed or FactKind.ComputerAlarm or FactKind.BreakerTrip)
        && (Weight(k) >= 1 || k is FactKind.Blamed or FactKind.Fight or FactKind.Relationship or FactKind.Sick or FactKind.Treated or FactKind.Confessed or FactKind.Grieved or FactKind.Sabotage or FactKind.Talk or FactKind.Hurt);

    private void Notice(Sim sim, in Fact f)
    {
        _mind ??= sim.Find<Mind>();
        // 큰 일 → 여파 (무너진 배를 더 때리지 않는다)
        if (f.Kind is FactKind.Died or FactKind.Collapsed or FactKind.WallBreach or FactKind.DoorBlown)
        {
            if (f.Kind == FactKind.Died) DeathTick = f.Tick;
            if (Act != ActAfter) Aftermaths++;
            EnterAfter(f.Tick, f.Kind == FactKind.Died);
        }
        // 이야기의 실
        if (_mind != null)
            switch (f.Kind)
            {
                case FactKind.Hurt: Involve(_mind.AgentOfBody(f.Actor), 0.5f); break;
                case FactKind.Collapsed: Involve(_mind.AgentOfBody(f.Actor), 0.8f); break;
                case FactKind.Blamed: Involve(f.Actor, 0.4f); Involve(f.Other, 0.4f); break;
                case FactKind.Fight: Involve(f.Actor, 1); Involve(f.Other, 1); break;
                case FactKind.Relationship: Involve(f.Actor, 0.6f); Involve(f.Other, 0.6f); break;
                case FactKind.Grieved: Involve(f.Actor, 0.6f); break;
                case FactKind.Pilfer or FactKind.Confessed: Involve(f.Actor, 0.3f); break;
            }
        // 씨앗의 열매
        if (_seedOf.Count > 0 && f.Cause != 0 && Telling(f.Kind) && _seedOf.TryGetValue(sim.Causes.Root(f.Cause), out int si))
        {
            var sd = Seeds[si];
            if (sd.Fruit == 0)
            {
                sd.Fruit = 1; Seeds[si] = sd;
                Pending = Math.Max(0, Pending - Potential(sd.Kind) * Level / 4.0);
            }
        }
    }

    private void EnterAfter(long tick, bool sad)
    {
        if (Act != ActAfter || sad) AfterFrom = tick;
        Act = ActAfter;
        ActUntil = Math.Max(ActUntil, tick + Time.Hours(AfterHours));
        if (sad) NewsDue = 1;
    }

    // ───────────────────────── 생각한다 (30분마다) ─────────────────────────

    private void Think(Sim sim)
    {
        _mind ??= sim.Find<Mind>();
        float fade = (float)Math.Exp(-ThinkEvery * Time.SecondsPerTick * 0.6931 / (2 * 86400));
        for (int i = 0; i < Thread.Length; i++) Thread[i] *= fade;
        Judge(sim);
        if (!Enabled || sim.Tick < Time.Hours(2)) return; // 첫 두 시간은 배를 보여 준다
        if (Act == ActAfter)
        {
            if (NewsDue != 0 && Level <= 6 && sim.Tick >= AfterFrom + Time.Hours(2)) { NewsDue = 0; Plant(sim, SeedKind.GoodNews); }
            if (sim.Tick < ActUntil) return;
            Act = ActBuild; BuildLeft = NewBuild(); NewsDue = 0;
        }
        if (sim.Tick - LastSeedTick < Time.Hours(BuildGapHours)) return;
        double h = Hardship(sim, out bool anyHurt);
        if (Level <= 3 ? anyHurt || h >= 0.3 - 1e-9 : Level <= 6 && h >= 0.3 - 1e-9) { Mercies++; return; }   // (0.6 × 3 / 6이 0.2999…가 되지 않게)
        if (Tension + Pending >= Target(sim.Tick) * (1 - Mercy * h)) return;
        if (Act == ActBuild)
        {
            if (Plant(sim, Choose(sim, 1)) && --BuildLeft == 0) Act = ActClimax;
        }
        else if (Plant(sim, Choose(sim, _rng.Unit() < BigShare ? 2 : 0)))
        {
            Arc++;
            EnterAfter(sim.Tick, false);
        }
    }

    /// <summary>조짐 씨앗 몇 개 — 성격마다.</summary>
    private byte NewBuild() => (byte)(Style switch
    {
        StoryStyle.Gentle or StoryStyle.Cosmic => 1,
        StoryStyle.Relentless or StoryStyle.Human => 2 + _rng.Range(0, 2),
        StoryStyle.SlowBurn => 2 + _rng.Range(0, 3),
        _ => 1 + _rng.Range(0, 2),
    });

    /// <summary>한 사람의 형편 무게: 쓰러졌거나 돌봄이 필요하면 1 · 앓거나 덮어 둔 큰 상처(화상 0.5 · 타박 0.8)면 0.6 · 덮어 둔 작은 상처면 0.2 (낫는 중) · 아니면 0.</summary>
    public static double HurtWeight(in Body b) =>
        b.Conscious == 0 || Life.NeedsCare(b) ? 1
        : Life.Sick(b) || b.Burn > 0.5f || b.Blunt > 0.8f || b.Infect > 0.2f ? 0.6
        : b.Burn > 0.25f || b.Blunt > 0.4f ? 0.2 : 0;

    /// <summary>배의 형편 (0 ~ 1): 사람마다 형편 무게의 평균 + 이틀 안의 죽음 0.3 + 식량 이레 치 아래 0.3. anyHurt: 한 사람이라도 무게 0.6 이상 (쓰러짐 · 돌봄이 필요함 · 앓음 · 큰 상처).</summary>
    public double Hardship(Sim sim, out bool anyHurt)
    {
        anyHurt = false;
        var life = sim.Find<Life>();
        _mind ??= sim.Find<Mind>();
        if (_mind == null || life == null) return 0;
        int n = 0; double hurt = 0;
        foreach (var a in _mind.Agents)
        {
            if (!a.IsPerson) continue;
            ref var b = ref life[a.C.Body];
            if (b.Alive == 0) continue;
            n++;
            double w = HurtWeight(b);
            hurt += w;
            if (w >= 0.6) anyHurt = true;
        }
        if (n == 0) return 0;
        double h = hurt / n;
        if (sim.Tick - DeathTick < Time.Hours(48)) h += 0.3;
        double st = sim.Book.LastStock((int)Res.Food);
        var (use, make) = sim.Book.PerDay((int)Res.Food, sim.Tick);
        if (sim.Tick >= Time.Hours(6) && st >= 0 && use - make > 1e-9 && st / (use - make) < 7) h += 0.3;
        return Math.Min(1, h);
    }

    /// <summary>씨앗이 헛돌았나 · 열매를 맺었나 — 종류마다 기다리는 시간.</summary>
    public static double JudgeHours(SeedKind k) => k switch
    {
        SeedKind.Rock => 2, SeedKind.Cloud or SeedKind.Flare => 4, SeedKind.WornPart => 12, SeedKind.HullFatigue => 24, SeedKind.Mold => 36, SeedKind.ArcRisk => 16,
        SeedKind.Mistake => 3, SeedKind.OldGrudge => 24, SeedKind.Cold => 60, SeedKind.Injury => 1, SeedKind.Crush => 72, SeedKind.HiddenFault => 16, _ => 24,
    };

    private void Judge(Sim sim)
    {
        for (int i = 0; i < Seeds.Count; i++)
        {
            var sd = Seeds[i];
            if (sd.Judged != 0 || sd.Kind == SeedKind.GoodNews) continue;
            if (sd.Fruit == 0 && sim.Tick - sd.Tick < Time.Hours(JudgeHours(sd.Kind))) continue;
            sd.Judged = 1; Seeds[i] = sd;
            int k = (int)sd.Kind;
            if (sd.Fruit != 0) { Fruited++; KindTrust[k] = Math.Min(2f, KindTrust[k] * 1.2f + 0.05f); }
            else { Fizzled++; KindTrust[k] = Math.Max(0.25f, KindTrust[k] * 0.6f); }
        }
    }

    // ───────────────────────── 사람 씨앗 ─────────────────────────

    /// <summary>사람 하나를 고른다: 알맞음 × (1 + 1.5 × 실) — 실이 굵은 사람이 주인공이 된다.</summary>
    public Agent? PickPerson(IEnumerable<Agent> people, Func<Agent, double> fit)
    {
        var list = people.ToList();
        if (list.Count == 0) return null;
        var w = new double[list.Count];
        double sum = 0;
        for (int i = 0; i < list.Count; i++) { w[i] = Math.Max(0.01, fit(list[i])) * (1 + 1.5 * Math.Min(2, ThreadOf(list[i].Id))); sum += w[i]; }
        double r = _rng.Unit() * sum;
        for (int i = 0; i < list.Count; i++) { r -= w[i]; if (r <= 0) return list[i]; }
        return list[^1];
    }

    private bool PlantPerson(Sim sim, SeedKind kind, double g)
    {
        var mind = sim.Find<Mind>(); var life = sim.Find<Life>(); var so = sim.Find<Society>(); var net = sim.Find<Network>();
        if (mind == null || life == null) return false;
        _mind ??= mind;
        bool Up(Agent a) => a.IsPerson && a.C.Up != 0 && life[a.C.Body].Alive != 0 && life[a.C.Body].Conscious != 0;
        switch (kind)
        {
            case SeedKind.Cold:
            {
                // 감기: 보급 상자에 묻어 온 바이러스 — 한 사람이 걸린다 (⑦ 잠복 · 앓기 · 옮김)
                var who = PickPerson(mind.Agents.Where(a => a.IsPerson && life[a.C.Body].Alive != 0 && life[a.C.Body].Ill == 0), _ => 1);
                if (who == null) return false;
                int c0 = sim.Causes.Add(CauseKind.Story, "보급 상자 하나에 감기 바이러스가 묻어 있었다", sim.Tick, who.C.Cell);
                int c1 = sim.Causes.Add(CauseKind.Biology, $"{Ko.I(mind.NameOf(who.Id))} 그 상자를 열다 감기가 옮았다", sim.Tick, who.C.Cell, c0, 0, who.Id);
                life.CatchCold(sim, who.C.Body, c1);
                Involve(who.Id, 1);
                return Record(sim, kind, c0, who.C.Cell, 1);
            }
            case SeedKind.Injury:
            {
                // 일하다 다침: 뜨거운 관에 데거나 공구가 미끄러진다 (덜 조심스러울수록) — 돌봄이 필요한 상처 (⑦ · ⑩ · ⑧)
                var who = PickPerson(mind.Agents.Where(a => Up(a) && life[a.C.Body].Burn < 0.2f && life[a.C.Body].Blunt < 0.3f), a => 1.2 - 0.5 * a.C.Careful - 0.3 * a.C.Consc);
                if (who == null) return false;
                bool burn = _rng.Chance(0.5);
                float size = (float)((burn ? 0.32 : 0.48) + _rng.Range(0.0, 0.15) * g);
                string name = mind.NameOf(who.Id);
                int c0 = sim.Causes.Add(CauseKind.Story, burn ? $"{Ko.I(name)} 뜨거운 관에 팔을 데었다" : $"{Ko.I(name)} 일하다 공구가 미끄러져 크게 다쳤다", sim.Tick, who.C.Cell, 0, 0, who.Id);
                if (burn) life.Scald(sim, who.C.Body, size, c0); else life.Strike(sim, who.C.Body, size, 0.12f, c0);
                Involve(who.Id, 1);
                return Record(sim, kind, c0, who.C.Cell, size);
            }
            case SeedKind.Crush:
            {
                // 짝사랑 (비밀): 짝이 없는 둘 — 정 · 아는 정도가 있는 사이에서 한쪽만 끌린다 (고백할지 · 받아 줄지는 그들이 — ⑨)
                if (so == null) return false;
                var people = mind.Agents.Where(a => Up(a) && so.PartnerOf(a.Id) < 0).ToList();
                if (people.Count < 2) return false;
                Agent? a1 = null, a2 = null; double best = double.MinValue;
                foreach (var x in people)
                    foreach (var y in people)
                    {
                        if (x == y) continue;
                        double sc = so.BondOf(x.Id, y.Id) + 0.3 * so.KnownOf(x.Id, y.Id) + 0.4 * _rng.Unit() + 0.3 * Math.Min(1, ThreadOf(x.Id));
                        if (sc > best) { best = sc; a1 = x; a2 = y; }
                    }
                int N = so.N, k = a1!.Id * N + a2!.Id;
                float at = (float)Math.Min(0.85, Society.ConfessAt + 0.06 + 0.03 * g);   // 고백 문턱을 넉넉히 넘는다 (가혹함 4: 0.69)
                so.Attract[k] = Math.Max(so.Attract[k], at);
                int c0 = sim.Causes.Add(CauseKind.Person, $"{Ko.Eun(mind.NameOf(a1.Id))} {Ko.Eul(mind.NameOf(a2.Id))} 남몰래 좋아한다", sim.Tick, a1.C.Cell, 0, 0, a1.Id);
                so.Remember(sim, a1.Id, Topic.Heart, a2.Id, c0, 0.5f, a1.C.Cell);
                Involve(a1.Id, 1); Involve(a2.Id, 0.5f);
                return Record(sim, kind, c0, a1.C.Cell, at);
            }
            case SeedKind.HiddenFault:
            {
                // 숨긴 실수 (비밀): 손보다 부품을 상하게 하고 말하지 않았다 — 몇 시간 뒤 고장 나면 그 사람만 제 탓인 줄 안다 (털어놓을지 숨길지는 그 사람이 — ⑨)
                if (net == null) return false;
                var running = net.Devices.Where(d => net.On[d.Id] != 0 && Enum.GetValues<Part>().Any(p => d.Life(p) > 0 && !net.IsBroken(d.Id, p))).ToList();
                var who = PickPerson(mind.Agents.Where(Up), a => 1.3 - 0.6 * a.C.Consc);
                if (running.Count == 0 || who == null) return false;
                var dev = running[_rng.Range(0, running.Count)];
                var parts = Enum.GetValues<Part>().Where(p => dev.Life(p) > 0 && !net.IsBroken(dev.Id, p)).ToList();
                var part = parts[_rng.Range(0, parts.Count)];
                int c0 = sim.Causes.Add(CauseKind.Person, $"{Ko.I(mind.NameOf(who.Id))} {Ko.Eul(dev.Name)} 손보다 {Device.PartName(part)} 부품을 상하게 했다 — 아무에게도 말하지 않았다", sim.Tick, dev.Cell, 0, 0, who.Id);
                net.AgeLeft(dev.Id, part, _rng.Range(2.0, 10.0) / g, c0);
                sim.Facts.Emit(sim.Tick, FactKind.Mistake, dev.Cell, 2, c0, who.Id, dev.Id);
                Involve(who.Id, 1);
                return Record(sim, kind, c0, dev.Cell, 1);
            }
            case SeedKind.GoodNews:
            {
                // 반가운 소식 (여파의 쉼): 고향에서 온 소식 — 받은 이는 슬픔 · 긴장이 크게 풀리고 · 배 사람 모두 조금
                var people = mind.Agents.Where(a => a.IsPerson && life[a.C.Body].Alive != 0).ToList();
                var who = PickPerson(people, a => 0.3 + a.C.Sad + a.C.Stress);
                if (who == null) return false;
                string what = new[] { "아이가 태어났다", "아픈 어머니가 나았다", "동생이 시험에 붙었다", "고향 마을에 새 학교가 섰다" }[_rng.Range(0, 4)];
                int c0 = sim.Causes.Add(CauseKind.Story, $"고향에서 {mind.NameOf(who.Id)}에게 반가운 소식이 왔다 — {what}", sim.Tick, who.C.Cell, 0, 0, who.Id);
                foreach (var a in people)
                {
                    ref var c = ref a.C;
                    float k = a == who ? 1 : 0.25f;
                    c.Sad = Math.Max(0, c.Sad - 0.3f * k); c.Stress = Math.Max(0, c.Stress - 0.2f * k); c.Joy = Math.Min(1, c.Joy + 0.4f * k);
                }
                Involve(who.Id, 0.5f);
                return Record(sim, kind, c0, who.C.Cell, 1);
            }
        }
        return false;
    }

    // ───────────────────────── 찍기 ─────────────────────────

    private void SaveArc(StateWriter w)
    {
        w.Arr(new[] { Act, BuildLeft, NewsDue }); w.I64(ActUntil); w.I64(AfterFrom); w.I32(Arc); w.I64(DeathTick);
        w.Arr(Thread); w.Arr(KindTrust); w.I64(Mercies); w.I64(Aftermaths); w.I64(Fizzled); w.I64(Fruited);
    }

    private void LoadArc(StateReader r)
    {
        var b = r.Arr<byte>(); Act = b[0]; BuildLeft = b[1]; NewsDue = b[2];
        ActUntil = r.I64(); AfterFrom = r.I64(); Arc = r.I32(); DeathTick = r.I64();
        Thread = r.Arr<float>(); KindTrust = r.Arr<float>(); Mercies = r.I64(); Aftermaths = r.I64(); Fizzled = r.I64(); Fruited = r.I64();
        _seedOf.Clear();
        for (int i = 0; i < Seeds.Count; i++) _seedOf[Seeds[i].Cause] = i;
        _mind = null;
    }
}
