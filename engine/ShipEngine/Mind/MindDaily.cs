namespace ShipEngine;

/// <summary>⑧ 쉬는 시간에 하는 일 — 성격 · 솜씨 · 곁의 설비로 고른다.</summary>
public enum Hobby : byte { Rest, Read, Game, Film, Music, Study, Tinker }

/// <summary>
/// ⑧ 하루의 리듬 · 일상 활동 (깊이 바퀴 2).
/// <para><b>아침형 · 저녁형</b>: 사람마다 몸의 시계가 몇 시간 앞서거나 늦다 (표준 편차 1.2시간 · 성실할수록 아침형 · 열릴수록 저녁형 — 아침형 연구의 경향).
/// 잘 시간대(교대에 맞춘 여덟 시간)가 몸의 시계만큼 앞뒤로 옮겨 간다 — 아침형은 일찍 눕고 일찍 일어나고 · 저녁형은 늦게까지 깨어 있다 (교대 시작은 그대로라 저녁형은 졸린 채 일하러 간다).</para>
/// <para><b>쉬는 시간</b>: 책 (열림 · 내향) · 놀이판 (외향 · 같이 할 사람) · 영상 · 음악 (신경성 — 마음이 가라앉는다) · 자료 공부 (과학 솜씨 · 열림) · 손으로 만들기 (수리 솜씨 · 성실).
/// 가까운 빈 설비 쪽으로 · 한 시간마다 다시 고른다 · 같은 시드면 같다. 공부 · 만들기는 하는 만큼 솜씨가 조금씩 는다 (45분에 남은 몫의 0.8%).</para>
/// </summary>
public sealed partial class Mind
{
    private int[] _books = Array.Empty<int>(), _games = Array.Empty<int>(), _films = Array.Empty<int>(), _study = Array.Empty<int>(), _tinker = Array.Empty<int>();
    private bool _hobbyBound;
    public long[] HobbyCount = new long[7];

    public static string HobbyName(Hobby h) => h switch
    {
        Hobby.Read => "책을 읽는다", Hobby.Game => "놀이판을 둔다", Hobby.Film => "영상을 본다", Hobby.Music => "음악을 듣는다",
        Hobby.Study => "자료를 들여다본다", Hobby.Tinker => "손으로 무언가 만든다", _ => "쉰다",
    };

    /// <summary>아침형 · 저녁형 (시간 — 음수가 아침형): 따로 갈래 낸 난수 + 성격.</summary>
    private static float ChronoOf(ulong seed, int person, float consc, float open)
    {
        var r = Rng.Stream(seed, "몸의 시계", person);
        double u1 = Math.Max(1e-12, r.Unit()), u2 = r.Unit();
        double g = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        return (float)Math.Clamp(1.2 * g - 1.5 * (consc - 0.5) + 0.8 * (open - 0.5), -2.5, 2.5);
    }

    private void BindHobbies()
    {
        if (_hobbyBound) return;
        _hobbyBound = true;
        string RoomType(FurnitureDef fu) => fu.Room >= 0 ? L.Rooms[fu.Room].Type : "";
        int[] Furn(Func<FurnitureDef, bool> ok) => L.Furniture.Where(fu => fu.Cells.Length > 0 && ok(fu)).Select(fu => fu.Id).ToArray();
        _books = Furn(fu => fu.Type is "Bookshelf");
        _games = Furn(fu => fu.Type is "GameTable");
        _films = Furn(fu => fu.Type is "Projector");
        _study = Furn(fu => fu.Type is "Console" or "DiagnosticScanner" or "SensorArray" && RoomType(fu) is "Lab" or "Observatory" or "Bridge" or "School");
        _tinker = Furn(fu => fu.Type is "Workbench" or "SolderStation" or "Lathe");
    }

    /// <summary>함께 쓰는 설비 (책장 · 놀이 탁자 · 영사기): 같은 일로 곁에 와 있는 이가 cap 명이 안 되는 가장 가까운 것의 칸 (−1 없음).</summary>
    private int NearestShared(Agent a, int[] furn, int cap, Hobby h)
    {
        int best = -1, bd = int.MaxValue;
        foreach (int fu in furn)
        {
            int cell = L.Furniture[fu].Cells[0], n = 0;
            foreach (var o in Agents) if (o != a && o.IsPerson && o.C.Goal == GoalKind.Relax && o.C.Hobby == (byte)h && Manhattan(o.C.GoalCell >= 0 ? o.C.GoalCell : o.C.Cell, cell) <= 2) n++;
            if (n >= cap) continue;
            int d = Manhattan(a.C.Cell, cell) + (int)(60 * DreadAt(a, cell)); // 꺼리는 방은 그만큼 멀게 친다
            if (d < bd) { bd = d; best = cell; }
        }
        return best;
    }

    /// <summary>쉬는 시간: 할 일을 고르고 그 자리로 간다 — 가까운 설비 · 성격 · 솜씨 (한 시간마다 다시 고른다).
    /// 책장(셋) · 놀이 탁자(넷) · 영사기(여섯)는 여럿이 함께 쓰고 · 의자 · 작업대 · 자료 화면은 한 사람씩.</summary>
    private bool BuildRelax(Sim sim, Agent a, List<PlanStep> plan)
    {
        BindHobbies();
        ref var c = ref a.C;
        var opts = new List<(Hobby h, int cell, int near, float w)>();
        int me = c.Cell;
        void Opt(Hobby h, int cell, int near, float w)
        {
            if (cell < 0 || w <= 0) return;
            if (RivalNear(a, cell)) w *= 0.3f;                       // ⑨ 벌어진 무리 사람 곁에서는 쉬지 않는다
            w *= 1 - 0.85f * DreadAt(a, cell);                       // 무서운 일을 겪은 방에서는 쉬지 않는다
            opts.Add((h, cell, near, w / (1f + Manhattan(me, cell) / 30f)));
        }
        int seat = NearestFree(a, _relax);
        Opt(Hobby.Rest, seat, 0, 0.5f);
        int book = NearestShared(a, _books, 3, Hobby.Read);
        Opt(Hobby.Read, book >= 0 ? book : seat, book >= 0 ? 1 : 0, 0.3f + c.Open + 0.5f * (1 - c.Extra));
        int game = NearestShared(a, _games, 4, Hobby.Game);
        if (game >= 0)
        {
            bool mate = false;
            foreach (var o in Agents) if (o != a && o.IsPerson && o.C.Up != 0 && RoomOf(o.C.Cell) == RoomOf(game) && o.C.Goal is GoalKind.Relax or GoalKind.Socialize or GoalKind.Idle) { mate = true; break; }
            Opt(Hobby.Game, game, 1, 0.2f + 1.2f * c.Extra + (mate ? 0.6f * c.Extra : 0)); // 같이 둘 사람이 있으면 — 외향적일수록 더 끌린다
        }
        Opt(Hobby.Film, NearestShared(a, _films, 6, Hobby.Film), 2, 0.5f + 0.3f * c.Extra);
        Opt(Hobby.Music, seat >= 0 ? seat : c.Bed >= 0 && !Taken(a, c.Bed) ? c.Bed : -1, 0, 0.3f + 0.6f * c.Neuro);
        Opt(Hobby.Study, NearestFree(a, _study), 0, 0.2f + c.SkScience + 0.6f * c.Open);
        Opt(Hobby.Tinker, NearestFree(a, _tinker), 0, 0.2f + c.SkRepair + 0.6f * c.Consc);
        if (opts.Count == 0) return false;
        float sum = 0; foreach (var o in opts) sum += o.w;
        float r = (float)Rng.Unit(sim.Seed, Rng.Key("쉼"), a.Id, sim.Tick / Time.Hours(1)) * sum;
        var pick = opts[^1];
        foreach (var o in opts) { r -= o.w; if (r <= 0) { pick = o; break; } }
        c.Hobby = (byte)pick.h;
        plan.Add(Go(pick.cell, pick.near));
        plan.Add(Use(UseKind.Relax, Time.Minutes(45), pick.cell, (int)pick.h));
        return true;
    }

    /// <summary>쉼을 마쳤다: 하는 일마다 다르게 풀린다 · 공부 · 만들기는 솜씨가 는다.</summary>
    private void FinishRelax(Agent a)
    {
        ref var c = ref a.C;
        var h = (Hobby)c.Hobby;
        HobbyCount[(int)h]++;
        switch (h)
        {
            case Hobby.Game:
            {
                c.Bored = Math.Max(0, c.Bored - 0.25f); c.Joy = Math.Min(1, c.Joy + 0.1f);
                foreach (var o in Agents) if (o != a && o.IsPerson && o.C.Up != 0 && Dist(o.C.Cell, c.Cell) <= 4) { c.Lonely = Math.Max(0, c.Lonely - 0.2f); break; }
                break;
            }
            case Hobby.Film: c.Bored = Math.Max(0, c.Bored - 0.25f); break;
            case Hobby.Read: c.Bored = Math.Max(0, c.Bored - 0.1f); c.Stress = Math.Max(0, c.Stress - 0.1f); break;
            case Hobby.Music: c.Stress = Math.Max(0, c.Stress - 0.2f); break;
            case Hobby.Study: c.SkScience += 0.008f * (1 - c.SkScience); c.Bored = Math.Max(0, c.Bored - 0.1f); break;
            case Hobby.Tinker: c.SkRepair += 0.008f * (1 - c.SkRepair); c.Bored = Math.Max(0, c.Bored - 0.1f); break;
        }
    }
}
