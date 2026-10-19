namespace ShipEngine;

/// <summary>
/// ⑩ → ⑧ 마음의 표 (깊이 바퀴 3 — docs/ENGINE.md '기억 · 정신 건강' (다)). 주컴퓨터는 마음을 읽지 못한다 — 보이는 표만 본다:
/// 사흘 안의 악몽(침실의 비명) · 공황 발작(그 방 카메라) · 얕은 잠(침대 감지 — 잠의 질 0.5 아래) · 끼니를 거름 (열여섯 시간 넘게).
/// 표가 있으면 하루 한 번 그 방 스피커로 말을 걸고 (우울 · 불안 −0.03 × 주컴퓨터를 믿는 정도) · 사흘째에도 남으면 '마음 돌봄' — 사람에게 상담을 부탁한다
/// (로봇은 이야기를 들어 줄 수 없다 · 친화 · 치료 솜씨 · 그 사람과의 정이 높은 이부터).
/// </summary>
public sealed partial class ShipMind
{
    public long CheckIns, CounselAsks;
    /// <summary>사람 → 표가 남은 채 말을 건 날 수 (표가 사라지거나 상담을 받으면 0).</summary>
    public readonly Dictionary<int, int> MoodTalks = new();
    private readonly Dictionary<int, long> _moodSaid = new();
    private readonly HashSet<int> _counselWanted = new();
    private long _moodCursor;

    /// <summary>주컴퓨터가 보는 마음의 표 (없으면 빈 글).</summary>
    private static string MoodSign(long t, in AgentCore c)
    {
        if (c.AttackAt > 0 && t - c.AttackAt < Time.Hours(72)) return "까닭 없이 숨이 가빠 주저앉곤 한다";
        if (c.NightmareAt > 0 && t - c.NightmareAt < Time.Hours(72)) return "자다가 악몽에 깬다";
        if (c.SleepLoss > 0.5f) return "잠을 설친다";
        if (c.AteAt > 0 && t - c.AteAt > Time.Hours(16)) return "끼니를 거른다";
        return "";
    }

    /// <summary>그 사람(몸)의 이야기를 들어 줄 만한 정도: 친화 0.6 + 치료 솜씨 0.4 + 그 사람과의 정.</summary>
    private float CounselFit(Sim sim, Agent a, int body)
    {
        var soc = sim.Find<Society>();
        int pa = -1;
        foreach (var o in _mind!.Agents) if (o.IsPerson && o.C.Body == body) { pa = o.Id; break; }
        float bond = soc != null && pa >= 0 ? Math.Max(0, soc.BondOf(a.Id, pa)) : 0;
        return Math.Min(1, 0.6f * a.C.Agree + 0.4f * a.C.SkMedic + 0.5f * bond);
    }

    /// <summary>그 몸의 사람에게 아직 상담이 필요한가 (들어 준 사실을 읽었으면 아니다 — 사건이 걷히기 전에 다시 부탁하지 않게).</summary>
    private bool CounselStill(Sim sim, int body)
    {
        MoodFacts(sim);
        foreach (var o in _mind!.Agents) if (o.IsPerson && o.C.Body == body) return _counselWanted.Contains(o.Id);
        return false;
    }

    /// <summary>들어 준 사실을 읽는다: 그 사람은 상담을 받았다 (말을 건 날 수도 처음부터).</summary>
    private void MoodFacts(Sim sim)
    {
        foreach (var f in sim.Facts.Since(_moodCursor))
            if (f.Kind == FactKind.Heart && f.Size == (float)HeartEvent.Counseled && f.Other >= 0) { _counselWanted.Remove(f.Other); MoodTalks.Remove(f.Other); }
        _moodCursor = sim.Facts.Total;
    }

    /// <summary>한 사람의 표를 본다 (사람 표를 읽을 때마다): 하루 한 번 말을 걸고 · 사흘째면 상담을 부탁한다.</summary>
    private void MoodWatch(Sim sim, Agent a, in Body b)
    {
        long t = sim.Tick;
        MoodFacts(sim);
        if (b.Conscious == 0 || b.Asleep != 0 || a.C.Up == 0 || a.C.Mode != 0) return;
        string sign = MoodSign(t, a.C);
        if (sign.Length == 0) { MoodTalks.Remove(a.Id); _counselWanted.Remove(a.Id); return; }
        if (_moodSaid.TryGetValue(a.Id, out long last) && t - last < Time.Hours(20)) return;
        _moodSaid[a.Id] = t;
        int n = MoodTalks.GetValueOrDefault(a.Id) + 1;
        MoodTalks[a.Id] = n;
        string name = _mind!.NameOf(a.Id);
        Say(sim, $"{name}님, 요즘 {sign} — 괜찮으세요? 이야기 나눌 사람이 필요하면 말씀하세요");
        float trust = sim.Find<Society>()?.Trust(a.Id) ?? 0.5f;
        _mind.Eased(a.Id, 0.03f * trust, 0, 0.03f * trust);
        int cause = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 {name}에게 말을 걸었다 — 요즘 {sign}", t, b.Cell, a.C.ShockCause, 0);
        sim.Facts.Emit(t, FactKind.Heart, b.Cell, (float)HeartEvent.CheckIn, cause, a.Id);
        CheckIns++;
        if (n >= 3 && _counselWanted.Add(a.Id))
        {
            CounselAsks++;
            Say(sim, $"{name}님 곁에 이야기를 들어 줄 사람이 필요합니다");
        }
    }

    private void SaveMood(StateWriter w)
    {
        w.I64(CheckIns); w.I64(CounselAsks); w.I64(_moodCursor);
        w.Arr(MoodTalks.OrderBy(k => k.Key).Select(k => (long)k.Key << 32 | (uint)k.Value).ToArray());
        w.Arr(_moodSaid.OrderBy(k => k.Key).SelectMany(k => new[] { k.Key, k.Value }).ToArray());
        w.Arr(_counselWanted.OrderBy(x => x).ToArray());
    }

    private void LoadMood(StateReader r)
    {
        CheckIns = r.I64(); CounselAsks = r.I64(); _moodCursor = r.I64();
        MoodTalks.Clear(); foreach (long v in r.Arr<long>()) MoodTalks[(int)(v >> 32)] = (int)(uint)v;
        _moodSaid.Clear(); var ms = r.Arr<long>(); for (int i = 0; i + 1 < ms.Length; i += 2) _moodSaid[(int)ms[i]] = ms[i + 1];
        _counselWanted.Clear(); foreach (int x in r.Arr<int>()) _counselWanted.Add(x);
    }
}
