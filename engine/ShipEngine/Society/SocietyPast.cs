using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>출항 과거사 하나: 두 사람 · 갈래 (1 아는 사이 · 2 오랜 벗 · 3 묵은 앙금 · 4 남몰래 좋아함 — A가 B를) · 원인 마디.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PastTie
{
    public int A, B;
    public byte Kind;
    public int Cause;
}

/// <summary>
/// ⑫ 자연 사건 — 출항 과거사 (docs/ENGINE.md ⑫ · 함장 결정): 배에 탈 때 사람 사이의 지난 일을 한 번 만든다 (시드에서) — 아는 사이 · 오랜 벗 · 묵은 앙금 · 남몰래 좋아함.
/// 첫날부터 사람 판에 보이고, 따질지 · 풀지 · 고백할지는 그들이 (⑧). 항해 중에는 아무것도 심지 않는다 (이야기꾼은 없다).
/// 몫은 기능 확인용 첫 값 — 맨 끝에 다듬는다.
/// </summary>
public sealed partial class Society
{
    public const double KnewChance = 0.35, FriendShare = 0.3, GrudgeShare = 0.3, CrushChance = 0.1;
    public List<PastTie> Past = new();
    private bool _pastDone;

    private static readonly string[] OldFriend = { "예전 배에서 함께 일한 오랜 벗이다", "같은 훈련소를 나왔다", "고향 마을에서 함께 자랐다" };
    private static readonly string[] OldGrudge = { "지난 항구에서 크게 다툰 적이 있다", "예전 배에서 서로의 실수를 덮어 주지 않았다", "같은 자리를 두고 겨룬 적이 있다", "빌린 돈을 두고 사이가 틀어졌다" };

    /// <summary>승무원을 태운 뒤 한 번 (<see cref="ShipWorld.Crewed"/>): 사람마다 두 사람씩 지난 일을 시드에서 정한다.</summary>
    public void Backstory(Sim sim)
    {
        _mind ??= sim.Find<Mind>(); _life ??= sim.Find<Life>(); _net ??= sim.Find<Network>();
        if (_mind == null || _life == null || _pastDone) return;
        _pastDone = true;
        Resize(_mind.Agents.Count);
        var people = Enumerable.Range(0, N).Where(Person).ToList();
        ulong seed = sim.Seed;
        float R(string key, int a, int b) => (float)Rng.Unit(seed, Rng.Key(key), a, b);
        foreach (int i in people)
            foreach (int j in people)
            {
                if (j <= i || R("지난 일", i, j) >= KnewChance) continue;
                string ni = _mind.NameOf(i), nj = _mind.NameOf(j);
                float known = 0.4f + 0.4f * R("아는 정도", i, j);
                Known[i * N + j] = Math.Max(Known[i * N + j], known); Known[j * N + i] = Math.Max(Known[j * N + i], known);
                float v = R("지난 일 갈래", i, j);
                float rough = 1 - (C(i).Agree + C(j).Agree) / 2;   // 둘 다 거칠수록 앙금이 남기 쉽다
                if (v < FriendShare)
                {
                    int cause = sim.Causes.Add(CauseKind.Society, $"{Ko.Gwa(ni)} {Ko.Eun(nj)} {OldFriend[(int)(R("벗 사연", i, j) * OldFriend.Length) % OldFriend.Length]}", sim.Tick, C(i).Cell, 0, 0, i);
                    float b = 0.3f + 0.2f * R("정", i, j);
                    Bond[i * N + j] = Math.Max(Bond[i * N + j], b); Bond[j * N + i] = Math.Max(Bond[j * N + i], b);
                    Past.Add(new PastTie { A = i, B = j, Kind = 2, Cause = cause });
                }
                else if (v < FriendShare + GrudgeShare * (0.5f + rough))
                {
                    int cause = sim.Causes.Add(CauseKind.Society, $"{Ko.Gwa(ni)} {Ko.Eun(nj)} {OldGrudge[(int)(R("앙금 사연", i, j) * OldGrudge.Length) % OldGrudge.Length]}", sim.Tick, C(i).Cell, 0, 0, i);
                    float g = 0.3f + 0.3f * R("앙금", i, j);
                    bool iMore = R("앙금 쪽", i, j) < 0.5f;
                    Grudge[i * N + j] = Math.Max(Grudge[i * N + j], iMore ? g : g * 0.6f);
                    Grudge[j * N + i] = Math.Max(Grudge[j * N + i], iMore ? g * 0.6f : g);
                    Remember(sim, i, Topic.Blame, j, cause, iMore ? 0.6f : 0.4f, C(i).Cell);
                    Remember(sim, j, Topic.Blame, i, cause, iMore ? 0.4f : 0.6f, C(j).Cell);
                    Past.Add(new PastTie { A = iMore ? i : j, B = iMore ? j : i, Kind = 3, Cause = cause });
                }
                else Past.Add(new PastTie { A = i, B = j, Kind = 1, Cause = sim.Causes.Add(CauseKind.Society, $"{Ko.Gwa(ni)} {Ko.Eun(nj)} 전부터 아는 사이다", sim.Tick, C(i).Cell, 0, 0, i) });
                // 남몰래 좋아함 (한쪽만 · 둘 다 짝이 없을 때 · 서로 아는 사이에서)
                if (Partner[i] < 0 && Partner[j] < 0 && R("남몰래", i, j) < CrushChance)
                {
                    bool fromI = R("남몰래 쪽", i, j) < 0.5f;
                    int a = fromI ? i : j, b = fromI ? j : i;
                    float at = 0.45f + 0.25f * R("끌림", i, j);
                    Attract[a * N + b] = Math.Max(Attract[a * N + b], at);
                    int cause = sim.Causes.Add(CauseKind.Society, $"{Ko.Eun(_mind.NameOf(a))} 출항 전부터 {Ko.Eul(_mind.NameOf(b))} 남몰래 좋아했다", sim.Tick, C(a).Cell, 0, 0, a);
                    Remember(sim, a, Topic.Heart, b, cause, 0.5f, C(a).Cell);
                    Past.Add(new PastTie { A = a, B = b, Kind = 4, Cause = cause });
                }
            }
    }

    /// <summary>그 사람의 지난 일 한 줄 (사람 판 — 관객은 남몰래 좋아함도 본다).</summary>
    public string PastLine(int id)
    {
        if (_mind == null) return "";
        var parts = new List<string>();
        foreach (var t in Past)
        {
            if (t.A != id && t.B != id) continue;
            int o = t.A == id ? t.B : t.A;
            string n = _mind.NameOf(o);
            parts.Add(t.Kind switch { 2 => $"{n}의 오랜 벗", 3 => $"{Ko.Gwa(n)} 묵은 앙금", 4 => t.A == id ? $"{Ko.Eul(n)} 남몰래 좋아한다" : "", _ => $"{Ko.Eul(n)} 안다" });
        }
        parts.RemoveAll(p => p.Length == 0);
        return parts.Count > 0 ? "지난 일: " + string.Join(" · ", parts) : "";
    }

    private void SavePast(StateWriter w) { w.Bool(_pastDone); w.List(Past); }
    private void LoadPast(StateReader r) { _pastDone = r.Bool(); Past = r.List<PastTie>(); }
}
