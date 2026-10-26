namespace ShipEngine;

/// <summary>
/// 사건 경과 하나 (docs/OBSERVER.md §7.1 — V8 · 연출 장부 · 원인 사슬의 뿌리마다): 일어남 (첫 무게 있는 일) → 처음 안 이 (어떻게) → 주컴퓨터가 앎 (무엇으로) → 맡김 → 닿음 → 끔 · 막음 · 고침 → 끝 ·
/// 다침 · 쓰러짐 · 숨짐 · 구함 · 늦은 까닭 (그때 아는 것만 — 무선 센서 방 · 못 보는 방 · 맡은 이가 막힘 · 문 끼임 · 잠에서 깸). 마디 때는 틱 (없으면 −1).
/// </summary>
public sealed class Episode
{
    public int Root, Cell = -1, Room = -1;
    public FactKind Kind;            // 첫 무게 있는 일 (불이 붙음 · 벽이 뚫림 · 관이 샘 · 합선 · 고장 …)
    public bool Hand;                // 함장이 일으킨 일 (뿌리 원인이 함장)
    public string Name = "", Title = "";   // '침실 둘 불' · 머리 (함장이 일으킨 일이면 뿌리 원인 글)
    public long Start, Last, End = -1;
    public long NoticeAt = -1; public int Noticer = -1; public BeliefKind NoticeKind;
    public long ComputerAt = -1; public IncidentKind ComputerKind;
    public long AssignAt = -1; public int Assignee = -1; public bool Asked;   // 부탁받은 사람이면 Asked
    public long ArriveAt = -1;
    public long FixAt = -1; public FactKind FixKind; public int Fixer = -1;
    /// <summary>알아챈 사람 (마음 번호 · 처음 안 차례).</summary>
    public readonly List<int> Knowers = new();
    /// <summary>다침 · 쓰러짐 · 숨짐 (사람 · 갈래마다 한 번 · 때 차례).</summary>
    public readonly List<(int agent, FactKind kind)> Harm = new();
    /// <summary>구해진 이.</summary>
    public readonly List<int> Saved = new();
    /// <summary>늦은 까닭 (그때 짓는다 · 아는 것만 · 넷까지).</summary>
    public readonly List<string> Why = new();
    public bool Open => End < 0;
    /// <summary>처음 안 때 (사람 · 로봇이 알아챘거나 주컴퓨터가 안 때 가운데 이른 것 — 없으면 −1).</summary>
    public long KnewAt => NoticeAt < 0 ? ComputerAt : ComputerAt < 0 ? NoticeAt : Math.Min(NoticeAt, ComputerAt);
}

/// <summary>
/// 경과 장부 (docs/OBSERVER.md §7.1 — V8): 연출이 사실 고리에서 적기만 한다 (세계 밖 · 읽기만 — 관심 점수 · 하이라이트 문턱에는 넣지 않는다).
/// 무게 있는 일 (<see cref="Starts"/>)이 새 뿌리에서 나면 경과를 열고 · 그 뿌리의 사실이 마디를 채운다 · 맡은 이의 방만 틱마다 본다 (닿음) ·
/// 마지막 일에서 2분 조용하고 그 뿌리의 주컴퓨터 사건이 모두 닫히면 끝. 열린 경과가 없으면 사실마다 뿌리를 찾지 않는다 (무게 있는 일만).
/// </summary>
public sealed partial class Director
{
    public const int MaxEpisodes = 64, MaxWhy = 4;
    public static readonly long EpisodeQuiet = Time.Minutes(2);
    public readonly List<Episode> Episodes = new();
    private int _openEpisodes;

    /// <summary>경과를 여는 일: 불이 붙음 · 벽이 뚫림 · 관이 샘 · 합선 · 장치 고장 · 과열 · 독한 기체 · 충돌.</summary>
    public static bool Starts(FactKind k) => k is FactKind.Ignition or FactKind.WallBreach or FactKind.PipeLeak or FactKind.Short or FactKind.MachineFail or FactKind.Overheat or FactKind.Toxic or FactKind.Impact;

    /// <summary>그 뿌리의 경과 (없으면 null — 늦은 것부터 찾는다).</summary>
    public Episode? EpisodeOf(int root) { for (int i = Episodes.Count - 1; i >= 0; i--) if (Episodes[i].Root == root) return Episodes[i]; return null; }

    /// <summary>원인 마디의 뿌리 (첫 부모를 따라 — <see cref="Causes.Root"/>와 같다 · 목록을 짓지 않는다).</summary>
    public static int RootOf(Causes c, int id)
    {
        for (int k = 0; id > 0 && k < 63; k++) { int p = c[id].Parent; if (p <= 0 || p == id) return id; id = p; }
        return id;
    }

    /// <summary>갈래 낱말: 불 · 외판 구멍 · 벽 구멍 · 관 샘 · 합선 · 고장 · 과열 · 독한 기체 · 충돌.</summary>
    public static string KindWord(FactKind k, bool hull = false) => k switch
    {
        FactKind.Ignition => "불", FactKind.WallBreach => hull ? "외판 구멍" : "벽 구멍", FactKind.PipeLeak => "관 샘", FactKind.Short => "합선", FactKind.MachineFail => "고장",
        FactKind.Overheat => "과열", FactKind.Toxic => "독한 기체", FactKind.Impact => "충돌", _ => "일",
    };

    /// <summary>알아챈 길 (믿음 갈래 → 눈 · 귀 · 코 · 몸).</summary>
    public static string Sense(BeliefKind k) => k switch
    {
        BeliefKind.SmokeSmell => "냄새", BeliefKind.Bang or BeliefKind.Strain or BeliefKind.Grind or BeliefKind.Help => "소리",
        BeliefKind.Leak or BeliefKind.Cold or BeliefKind.Hot or BeliefKind.Vacuum or BeliefKind.BadAir => "몸으로",
        _ => "눈으로",
    };

    /// <summary>주컴퓨터가 안 길 (사건 갈래 → 센서).</summary>
    public static string Sensor(IncidentKind k) => k switch
    {
        IncidentKind.Fire => "연기 센서", IncidentKind.Leak or IncidentKind.Vacuum => "기압 센서", IncidentKind.Hot => "온도 센서",
        IncidentKind.CO or IncidentKind.BadAir or IncidentKind.Toxic or IncidentKind.O2Rich or IncidentKind.Flammable => "공기 센서",
        IncidentKind.PersonDown or IncidentKind.Injured or IncidentKind.Arrest => "몸 신호", IncidentKind.Heard => "마이크",
        IncidentKind.DoorJam or IncidentKind.Strain => "문 · 벽 센서",
        _ => "장치 신호",
    };

    /// <summary>칸이 든 방 (벽 · 문 칸이면 곁의 방 — 없으면 −1).</summary>
    public static int RoomNear(Layout L, int cell)
    {
        if (cell < 0 || cell >= L.N) return -1;
        if (L.Room[cell] >= 0) return L.Room[cell];
        int x = L.X(cell), y = L.Y(cell);
        if (L.In(x, y - 1) && L.Room[L.Index(x, y - 1)] >= 0) return L.Room[L.Index(x, y - 1)];
        if (L.In(x + 1, y) && L.Room[L.Index(x + 1, y)] >= 0) return L.Room[L.Index(x + 1, y)];
        if (L.In(x, y + 1) && L.Room[L.Index(x, y + 1)] >= 0) return L.Room[L.Index(x, y + 1)];
        if (L.In(x - 1, y) && L.Room[L.Index(x - 1, y)] >= 0) return L.Room[L.Index(x - 1, y)];
        return -1;
    }

    /// <summary>경과의 마지막 일로 치는 갈래 (몸 · 세계 · 맞섬 — 알아챔 · 말은 넣지 않는다: 연기 냄새를 뒤늦게 맡은 이가 끝을 미루지 않게).</summary>
    private static bool Moves(FactKind k) => Starts(k) || k is FactKind.FireSpread or FactKind.SmokeThick or FactKind.PressureDrop or FactKind.RegionVented or FactKind.WallCrack or FactKind.DoorJammed or FactKind.DoorBlown
        or FactKind.Flood or FactKind.Hurt or FactKind.Collapsed or FactKind.Died or FactKind.Rescue or FactKind.Extinguished or FactKind.FireOut or FactKind.Patched or FactKind.Repaired
        or FactKind.ComputerAlarm or FactKind.ComputerAssign or FactKind.ComputerSeal;

    /// <summary>사실 하나를 경과 장부에 (<see cref="Observe"/> 사실 고리 · 적기만).</summary>
    private void EpisodeNote(Sim sim, in Fact f, Mind? mind)
    {
        if (f.Kind == FactKind.Blocked) { if (_openEpisodes > 0 && f.Actor >= 0) BlockedNote(sim, f, mind); return; }
        if (f.Cause == 0 || _openEpisodes == 0 && !Starts(f.Kind)) return;
        int root = RootOf(sim.Causes, f.Cause);
        var e = EpisodeOf(root);
        if (e == null)
        {
            if (!Starts(f.Kind)) return;
            e = NewEpisode(sim, f, root);
        }
        else if (e.End >= 0)
        {
            if (!Starts(f.Kind)) return;
            e.End = -1; _openEpisodes++;   // 같은 뿌리의 일이 다시 났다 (꺼진 불이 다시 붙었다) — 경과를 잇는다
        }
        if (Moves(f.Kind)) e.Last = Math.Max(e.Last, f.Tick);
        int n = mind?.Agents.Count ?? 0;
        switch (f.Kind)
        {
            case FactKind.Noticed when f.Actor >= 0 && f.Actor < n:
                if (e.NoticeAt < 0) { e.NoticeAt = f.Tick; e.Noticer = f.Actor; e.NoticeKind = (BeliefKind)f.Other; }
                if (mind!.Agents[f.Actor].IsPerson && !e.Knowers.Contains(f.Actor)) e.Knowers.Add(f.Actor);
                break;
            case FactKind.ComputerAlarm when e.ComputerAt < 0: e.ComputerAt = f.Tick; e.ComputerKind = (IncidentKind)f.Other; break;
            case FactKind.ComputerAssign when e.AssignAt < 0 && f.Actor >= 0: e.AssignAt = f.Tick; e.Assignee = f.Actor; e.Asked = f.Size >= 1; break;
            case FactKind.Extinguished or FactKind.FireOut or FactKind.Patched or FactKind.Repaired:
                // 처음 끝낸 때 — 저절로 꺼진 뒤 누가 남은 불을 껐으면 그 사람 (맞선 이가 마디에 남게)
                if (e.FixAt < 0 || e.Fixer < 0 && f.Actor >= 0 && f.Actor < n && f.Kind != FactKind.FireOut) { e.FixAt = f.Tick; e.FixKind = f.Kind; e.Fixer = f.Actor < n ? f.Actor : -1; }
                break;
            case FactKind.Hurt or FactKind.Collapsed or FactKind.Died when f.Actor >= 0 && mind != null:
            {
                int a = mind.AgentOfBody(f.Actor);
                if (a >= 0 && mind.Agents[a].IsPerson && !e.Harm.Contains((a, f.Kind))) e.Harm.Add((a, f.Kind));
                break;
            }
            case FactKind.Rescue when f.Other >= 0 && f.Other < n && !e.Saved.Contains(f.Other): e.Saved.Add(f.Other); break;
            case FactKind.DoorJammed: Note(e, "문이 끼어 열리지 않았다"); break;
            case FactKind.SensorCut: Note(e, "센서 선이 끊겼다"); break;
            case FactKind.Woke when f.Actor >= 0 && f.Actor < n && e.NoticeAt < 0: Note(e, $"{Ko.I(mind!.NameOf(f.Actor))} 자다 깨서야 알았다"); break;
        }
        if (e.ComputerAt < 0) SensorNote(sim, e);
    }

    private Episode NewEpisode(Sim sim, in Fact f, int root)
    {
        var L = sim.Layout;
        int room = RoomNear(L, f.Cell);
        bool hull = f.Kind == FactKind.WallBreach && f.Cell >= 0 && f.Cell < L.N && L.Hull[f.Cell];
        var e = new Episode { Root = root, Kind = f.Kind, Cell = f.Cell, Room = room, Start = f.Tick, Last = f.Tick, Hand = sim.Causes[root].Kind == CauseKind.Captain };
        e.Name = $"{(room >= 0 ? L.Rooms[room].Name : "배")} {KindWord(f.Kind, hull)}";
        e.Title = e.Hand ? sim.Causes.Text(root) : e.Name;
        Episodes.Add(e); _openEpisodes++;
        if (Episodes.Count > MaxEpisodes)
        {
            int drop = Episodes.FindIndex(x => x.End >= 0);
            if (drop < 0) { drop = 0; _openEpisodes--; }
            Episodes.RemoveAt(drop);
        }
        return e;
    }

    private static void Note(Episode e, string why) { if (e.Why.Count < MaxWhy && !e.Why.Contains(why)) e.Why.Add(why); }

    /// <summary>주컴퓨터가 아직 모를 때: 그 방 센서가 무선으로만 닿나 · 못 닿나 (주컴퓨터가 그 방을 읽은 길 — 아는 것).</summary>
    private static void SensorNote(Sim sim, Episode e)
    {
        if (e.Room < 0 || sim.Find<ShipMind>() is not { } sm || sm.Reads == 0 || e.Room >= sm.RoomSeen.Length) return;   // 아직 한 번도 읽지 않았다 (띄운 첫 틱) — 모른다
        string room = sim.Layout.Rooms[e.Room].Name;
        switch (sm.RoomSeen[e.Room])
        {
            case 2: Note(e, $"{Ko.Eun(room)} 무선 센서다 (36초마다 읽는다)"); break;
            case 0:
                bool power = sim.Find<Network>()?.RoomPowered(e.Room) ?? true;
                Note(e, power ? $"{room} 센서 선이 끊겨 주컴퓨터가 그 방을 못 본다" : $"{room}에 전기가 끊겨 주컴퓨터가 그 방을 못 본다");
                break;
        }
    }

    /// <summary>맡은 이가 닿기 전에 막혔다 (까닭 글 — 판단 일지와 같은 원인 마디).</summary>
    private void BlockedNote(Sim sim, in Fact f, Mind? mind)
    {
        foreach (var e in Episodes)
            if (e.End < 0 && e.Assignee == f.Actor && e.AssignAt >= 0 && e.ArriveAt < 0)
                Note(e, $"{Ko.I(mind?.NameOf(f.Actor) ?? "맡은 이")} 막혔다 — {(f.Cause != 0 ? sim.Causes.Text(f.Cause) : "길이 막혔다")}");
    }

    /// <summary>틱마다 (<see cref="Observe"/> 끝 — 열린 경과만): 맡은 이가 그 방에 들었나 (맡은 이의 방만 본다) · 끝났나 (마지막 일에서 2분 조용 · 그 뿌리의 주컴퓨터 사건이 모두 닫혔다 — 1분에 한 번 본다).</summary>
    private void EpisodeTick(Sim sim, Mind? mind, long t)
    {
        if (_openEpisodes == 0) return;
        var L = sim.Layout;
        for (int i = Episodes.Count - 1; i >= 0; i--)
        {
            var e = Episodes[i];
            if (e.End >= 0) continue;
            if (e.AssignAt >= 0 && e.ArriveAt < 0 && mind != null && e.Assignee < mind.Agents.Count && e.Room >= 0)
            {
                int c = mind.Agents[e.Assignee].C.Cell;
                if (c >= 0 && c < L.N && L.Room[c] == e.Room) { e.ArriveAt = t; e.Last = Math.Max(e.Last, t); }   // 닿음도 경과의 일 (불이 저절로 꺼진 뒤 닿아도 끝이 닿음 앞에 서지 않게)
            }
            long quiet = t - e.Last;
            if (quiet < EpisodeQuiet || quiet % Time.Minutes(1) != 0) continue;
            if (!OpenIncident(sim, e.Root)) { e.End = e.Last; _openEpisodes--; }
        }
    }

    /// <summary>그 뿌리의 주컴퓨터 사건이 아직 열려 있나.</summary>
    private static bool OpenIncident(Sim sim, int root)
    {
        if (sim.Find<ShipMind>() is not { } sm) return false;
        foreach (var x in sm.Incidents) if (x.Active != 0 && x.Cause != 0 && RootOf(sim.Causes, x.Cause) == root) return true;
        return false;
    }

    // ── 연출 기록 (DirectorRecord — 판 4) ──
    private void SaveEpisodes(StateWriter w)
    {
        w.I32(Episodes.Count);
        foreach (var e in Episodes)
        {
            w.I32(e.Root); w.I32(e.Cell); w.I32(e.Room); w.I32((int)e.Kind); w.Bool(e.Hand); w.Str(e.Name); w.Str(e.Title);
            w.I64(e.Start); w.I64(e.Last); w.I64(e.End);
            w.I64(e.NoticeAt); w.I32(e.Noticer); w.Byte((byte)e.NoticeKind);
            w.I64(e.ComputerAt); w.Byte((byte)e.ComputerKind);
            w.I64(e.AssignAt); w.I32(e.Assignee); w.Bool(e.Asked); w.I64(e.ArriveAt);
            w.I64(e.FixAt); w.I32((int)e.FixKind); w.I32(e.Fixer);
            w.I32(e.Knowers.Count); foreach (int a in e.Knowers) w.I32(a);
            w.I32(e.Harm.Count); foreach (var (a, k) in e.Harm) { w.I32(a); w.I32((int)k); }
            w.I32(e.Saved.Count); foreach (int a in e.Saved) w.I32(a);
            w.I32(e.Why.Count); foreach (var s in e.Why) w.Str(s);
        }
    }

    private void LoadEpisodes(StateReader r)
    {
        Episodes.Clear(); _openEpisodes = 0;
        for (int i = 0, n = r.I32(); i < n; i++)
        {
            var e = new Episode { Root = r.I32(), Cell = r.I32(), Room = r.I32(), Kind = (FactKind)r.I32(), Hand = r.Bool(), Name = r.Str() ?? "", Title = r.Str() ?? "" };
            e.Start = r.I64(); e.Last = r.I64(); e.End = r.I64();
            e.NoticeAt = r.I64(); e.Noticer = r.I32(); e.NoticeKind = (BeliefKind)r.Byte();
            e.ComputerAt = r.I64(); e.ComputerKind = (IncidentKind)r.Byte();
            e.AssignAt = r.I64(); e.Assignee = r.I32(); e.Asked = r.Bool(); e.ArriveAt = r.I64();
            e.FixAt = r.I64(); e.FixKind = (FactKind)r.I32(); e.Fixer = r.I32();
            for (int k = 0, m = r.I32(); k < m; k++) e.Knowers.Add(r.I32());
            for (int k = 0, m = r.I32(); k < m; k++) e.Harm.Add((r.I32(), (FactKind)r.I32()));
            for (int k = 0, m = r.I32(); k < m; k++) e.Saved.Add(r.I32());
            for (int k = 0, m = r.I32(); k < m; k++) e.Why.Add(r.Str() ?? "");
            Episodes.Add(e);
            if (e.End < 0) _openEpisodes++;
        }
    }
}
