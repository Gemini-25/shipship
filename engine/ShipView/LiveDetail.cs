using ShipEngine;

namespace ShipView;

/// <summary>
/// 자세한 판 (docs/UI2.md §4 — 화면만 · 세계를 읽기만): 사람 · 로봇 · 주컴퓨터 하나를 넓은 판에 · 작은 탭마다 줄 · 로봇 판 (G).
/// 틀 (U1): 줄은 지금 사람 판 · 카드 · 로봇 카드 · 주컴퓨터 판의 글을 작은 탭에 나눠 싣는다 — 작은 탭마다 더 자세한 줄은 다음 단계 (U5 사람 · U6 로봇 · U7 주컴퓨터 · U8 예측).
/// </summary>
public sealed partial class LiveShow
{
    public static string[] DetailTabsOf(DetailKind k) => k switch
    {
        DetailKind.Person => PersonTabs, DetailKind.Robot => RobotTabs, DetailKind.Computer => ComputerTabs, DetailKind.Record => RecordTabs, _ => Array.Empty<string>(),
    };

    /// <summary>자세히 연 이 (사람 · 로봇) — 없으면 null.</summary>
    private Agent? DetailAgent()
    {
        var mind = World.Find<Mind>();
        return Detail is DetailKind.Person or DetailKind.Robot && mind != null && DetailId >= 0 && DetailId < mind.Agents.Count ? mind.Agents[DetailId] : null;
    }

    /// <summary>자세히 머리의 이름: '김하늘 '불꽃' · 기관사' · '로봇 1' · '주컴퓨터'.</summary>
    private string DetailTitle()
    {
        if (Detail == DetailKind.Computer) return "주컴퓨터";
        if (Detail == DetailKind.Record) return "기록";
        var a = DetailAgent();
        var w = World; var mind = w.Find<Mind>();
        if (a == null || mind == null) return Panel == PanelKind.Robots ? "로봇 · 드론" : "사람";
        string role = a.IsPerson && a.C.Role < mind.RoleNames.Length ? " · " + mind.RoleNames[a.C.Role] : "";
        string nick = a.IsPerson && w.Find<Society>()?.NickOf(a.Id) is string nk ? $" '{nk}'" : "";
        return mind.NameOf(a.Id) + nick + role;
    }

    /// <summary>자세히 머리의 둘째 줄: 사람 '식당 · 불을 끈다 ▸ 가는 중 · 3분째' · 로봇 '하는 일: … · 전지 62%' · 주컴퓨터 상태 한 줄.</summary>
    private string DetailLineNow()
    {
        var w = World;
        if (Detail == DetailKind.Computer) return ComputerHeadLine(w);
        if (Detail == DetailKind.Record) return RecordHeadLine();
        var a = DetailAgent();
        var mind = w.Find<Mind>();
        if (a == null || mind == null) return "";
        string thought = mind.Thought(a.Id).Replace(" — ", " ▸ ");
        if (!a.IsPerson)
        {
            var net = w.Find<Network>();
            float battery = net != null && a.C.Mech >= 0 && a.C.Mech < net.Robots.Count ? net.Robots[a.C.Mech].Battery : a.C.Battery;
            return $"하는 일: {thought} · 전지 {battery * 100:0}%";
        }
        var life = w.Find<Life>();
        if (life != null && a.C.Body >= 0 && life.Bodies[a.C.Body].Alive == 0) return "숨을 거두었다 — " + Time.Clock(life.Bodies[a.C.Body].DiedAt);
        int room = a.C.Cell >= 0 && a.C.Cell < w.Layout.N ? w.Layout.Room[a.C.Cell] : -1;
        string where = room >= 0 ? w.Layout.Rooms[room].Name : "통로";
        double min = Math.Max(0, w.Tick - a.C.GoalSince) / (double)Time.Minutes(1);
        return $"{where} · {thought} · {min:0}분째";
    }

    /// <summary>자세히 머리의 얼굴 (사람 줄 얼굴 그림의 칸 — 사람 차례) · 로봇 · 주컴퓨터는 -1.</summary>
    private int DetailFaceNow()
    {
        var a = DetailAgent();
        if (a == null || !a.IsPerson) return -1;
        int slot = 0;
        foreach (var pa in World.Find<Mind>()!.Agents) { if (pa.Id == a.Id) return slot; if (pa.IsPerson) slot++; }
        return -1;
    }

    /// <summary>막대: 남은 몫 (가득이 좋다 — 숨 · 전지 · 다리) · 무게 (많을수록 나쁘다 — 두려움 · 긴장).</summary>
    private static PanelBar Bar(string name, float v) => new(name, Math.Clamp(v, 0, 1), v < 0.3f ? 2 : v < 0.6f ? 1 : 0);
    private static PanelBar Weight(string name, float v) => new(name, Math.Clamp(v, 0, 1), v > 0.7f ? 2 : v > 0.4f ? 1 : 0);

    /// <summary>자세한 판의 줄: 고른 작은 탭.</summary>
    private void DetailRows(List<PanelRow> rows)
    {
        if (Detail == DetailKind.Computer) { ComputerRows(rows); return; }   // 주컴퓨터 자세히 (LiveComputer.cs — U7)
        if (Detail == DetailKind.Record) { RecordRows(rows); return; }      // 기록 (LiveRecord.cs — V3)
        var a = DetailAgent();
        var w = World; var mind = w.Find<Mind>();
        if (a == null || mind == null) { rows.Add(new PanelRow("그 이를 찾을 수 없다", "목록으로 돌아간다 (오른쪽 누르기)", false, Key: -1)); return; }
        if (a.IsPerson) PersonRows(w, mind, a, rows); else RobotDetailRows(w, mind, a, rows);
    }

    private long DK(long n) => KeyDetail(DetailSub, n);

    /// <summary>주컴퓨터 '아는 것' 표를 다시 셈하는 틈 (화면 초 — 시험은 0).</summary>
    public double KnowsEverySec = 0.5;

    /// <summary>사람 자세히 (docs/UI2.md §4.2 — U5): 몸 · 마음 · 하는 일 · 아는 것 · 사람 사이 · 일지 · 지나온 길 (자리만 — docs/OBSERVER_REVIEW.md §4).</summary>
    private void PersonRows(Sim w, Mind mind, Agent a, List<PanelRow> rows)
    {
        var life = w.Find<Life>(); var so = w.Find<Society>();
        if (life == null || a.C.Body < 0) return;
        var b = life.Bodies[a.C.Body];
        switch (DetailSub)
        {
            case 0: BodyRows(w, b, rows); break;
            case 1: MindRows(w, mind, so, a, b, rows); break;
            case 2: DoingRows(w, mind, a, b.Alive == 0, rows); break;
            case 3: BeliefRows(w, mind, a, rows); KnowsRows(w, mind, a, rows); break;
            case 4: PeopleRows(w, mind, so, a, rows); break;
            case 5: JudgedRows(a, rows); NoteRowsFor(w, mind.NameOf(a.Id), rows); break;   // 일지 · 주컴퓨터가 이 사람에게 한 부탁
            default:   // 지나온 길 (자리 — 함장 결정 10-26: 구함 · 다침 · 외상과 회복 · 짝 · 꿈 · 숨짐을 항해 내내 적는다)
                rows.Add(new PanelRow("곧 — 이 사람이 항해 내내 지나온 길", "구한 일 · 다친 일 · 겪은 충격과 회복 · 짝 · 꿈 · 숨짐을 차례로 — 맨 위에 '겁이 많았다 → 맞서며 단단해졌다'처럼 달라진 것", false, Key: -1));
                if (so?.PastLine(a.Id) is { Length: > 0 } past) rows.Add(new PanelRow("출항 전 지난 일", past, false, Key: DK(0)));
                break;
        }
    }

    private string Cause(Sim w, int cause) => cause > 0 && cause < w.Causes.Count ? "원인: " + WhyText(cause) : "";

    /// <summary>몸: 숨 · 피 / 체온 / 다침 (원인) / 병 / 장기 · 심장 / 약 / 무중력 · 방사선 / 귀 / 위생 / 입은 것 / 진료 기록 / 숨졌으면 때 · 원인 사슬.</summary>
    private void BodyRows(Sim w, in Body b, List<PanelRow> rows)
    {
        if (b.Alive == 0) { rows.Add(new PanelRow("숨을 거두었다 — " + Time.Clock(b.DiedAt), b.DeathCause != 0 ? w.Causes.Story(b.DeathCause) : "", true, Key: DK(0))); return; }
        var c = DetailAgent()!.C;
        string state = b.Conscious == 0 ? "쓰러져 있다" : b.Asleep != 0 ? "자는 중" : "깨어 있다";
        var blood = new List<string> { $"핏속 산소 {b.SpO2 * 100:0}%", $"피 {b.BloodL:0.0}L" };
        if (b.COHb > 0.02f) blood.Add($"일산화탄소 {b.COHb * 100:0}%");
        if (b.CN > 0.05f) blood.Add($"시안화 {b.CN * 100:0}");
        if (b.LungDamage > 0.05f) blood.Add($"폐 {b.LungDamage * 100:0}");
        rows.Add(new PanelRow($"숨 · 피 — {state}", string.Join(" · ", blood), b.Conscious == 0 || b.SpO2 < 0.85f,
            Bars: new[] { Bar("숨", (b.SpO2 - 0.7f) / 0.28f), Bar("피", b.BloodL / 5f), Bar("배", 1 - c.Hunger), Bar("잠", 1 - c.Sleepy) }, Key: DK(0)));
        var heat = new List<string> { $"체온 {b.CoreK - 273.15:0.0}℃" };
        if (b.Frost > 0.05f) heat.Add($"언 데 {b.Frost * 100:0}");
        rows.Add(new PanelRow("체온", string.Join(" · ", heat), b.CoreK < 308 || b.CoreK > 312, Key: DK(1)));
        var hurt = new List<string>();
        if (b.Burn > 0.05f) hurt.Add($"화상 {b.Burn * 100:0}");
        if (b.Blunt > 0.05f) hurt.Add($"타박 {b.Blunt * 100:0}");
        if (b.Bleed > 0.01f) hurt.Add("피가 난다");
        if (b.BleedIn > 0.05f) hurt.Add("속에서 피가 난다");
        if (b.Barotrauma > 0.05f) hurt.Add($"압력 상처 {b.Barotrauma * 100:0}");
        if (b.Pain > 0.1f) hurt.Add($"아픔 {Math.Min(1, b.Pain) * 100:0}");
        if (b.Hypoxia > 0.1f) hurt.Add("숨이 모자랐다");
        rows.Add(new PanelRow("다침", hurt.Count > 0 ? string.Join(" · ", hurt) + (Cause(w, b.HurtCause) is { Length: > 0 } hc ? "\n" + hc : "") : "다친 데 없다", hurt.Count > 0, Key: DK(2)));
        var ill = new List<string>();
        if (b.Infect > 0.05f) ill.Add("상처가 곪는다" + (b.InfectCause > 0 ? $" ({WhyText(b.InfectCause)})" : ""));
        if (b.Ill == 2) ill.Add("옮는 병을 앓는다" + (b.IllCause > 0 ? $" ({WhyText(b.IllCause)})" : ""));
        else if (b.Ill == 3) ill.Add("옮는 병을 앓고 나았다");
        if (b.Gut == 2) ill.Add("배탈" + (b.GutCause > 0 ? $" ({WhyText(b.GutCause)})" : ""));
        if (b.Appendix > 0.05f) ill.Add("맹장염");
        if (ill.Count > 0) rows.Add(new PanelRow("병", string.Join("\n", ill), true, Key: DK(3)));
        var organs = new List<string>();
        if (b.Arrest != 0) organs.Add(b.Arrest == 1 ? "심장이 잔떨림 — 제세동이 듣는다" : "심장이 멎었다" + $" · 멎은 지 {b.NoFlow + b.LowFlow:0}분");
        if (b.Heart > 0.05f) organs.Add($"심장 손상 {b.Heart * 100:0}");
        if (b.Brain > 0.05f) organs.Add($"뇌 손상 {b.Brain * 100:0}");
        if (b.Liver > 0.05f) organs.Add($"간 손상 {b.Liver * 100:0}");
        if (organs.Count > 0) rows.Add(new PanelRow("장기 · 심장", string.Join(" · ", organs) + (b.Arrest != 0 && Cause(w, b.ArrestCause) is { Length: > 0 } ac ? "\n" + ac : ""), true, Key: DK(4)));
        var drugs = new List<string>();
        if (b.Opioid > 0.5f) drugs.Add("진통제가 돈다" + (b.OpioidDep > 0.3f ? " · 기대게 되었다" : b.OpioidTol > 0.3f ? " · 덜 듣는다" : ""));
        if (b.Apap > 50) drugs.Add("해열 진통제");
        if (b.Abx > 0) drugs.Add($"항생제 {b.Abx:0}시간 더");
        if (b.Antidote > 0) drugs.Add("해독제");
        if (b.Hypnotic > 0.5f) drugs.Add("수면제");
        if (drugs.Count > 0) rows.Add(new PanelRow("약", string.Join(" · ", drugs), false, Key: DK(5)));
        var space = new List<string>();
        if (b.BoneLoss > 0.002f) space.Add($"뼈 {b.BoneLoss * 100:0.0}% 잃었다");
        if (b.MuscleLoss > 0.002f) space.Add($"근육 {b.MuscleLoss * 100:0.0}% 잃었다");
        if (b.Nausea > 0.1f) space.Add("우주 멀미");
        if (b.RadGy > 0.01f) space.Add($"받은 방사선 {b.RadGy:0.00}그레이");
        if (space.Count > 0) rows.Add(new PanelRow("무중력 · 방사선", string.Join(" · ", space), b.RadGy > 1, Key: DK(6)));
        if (b.HearShift > 5) rows.Add(new PanelRow("귀", $"먹먹하다 — 잘 안 들린다{(Cause(w, b.HearCause) is { Length: > 0 } hr ? "\n" + hr : "")}", false, Key: DK(7)));
        var clean = new List<string>();
        if (b.Bladder * 0.018 > 0.3) clean.Add("화장실이 급하다");
        if (b.Bowel > 0.25) clean.Add("뒤가 급하다");
        if (b.Dirt > 0.5f) clean.Add("몸이 더럽다");
        if (b.Wet != 0) clean.Add("옷이 젖었다");
        if (b.HandGerm > 0.3f) clean.Add("손에 균이 묻었다");
        rows.Add(new PanelRow("위생", clean.Count > 0 ? string.Join(" · ", clean) : "깨끗하다", false, Key: DK(8)));
        var worn = new List<string>();
        if (b.Suit != 0) worn.Add($"우주복 (산소 {b.SuitO2:0}몫)");
        if (b.Mask != 0) worn.Add("산소 마스크");
        if (b.Dressed > 0) worn.Add($"붕대 {b.Dressed:0}시간 더");
        if (worn.Count > 0) rows.Add(new PanelRow("입은 것", string.Join(" · ", worn), false, Key: DK(9)));
        if (c.DxAil != Ail.None) rows.Add(new PanelRow("진료 기록", $"{Life.AilName(c.DxAil)} · {Time.Clock(c.DxAt)}{(c.DxBy >= 0 && World.Find<Mind>() is { } m && c.DxBy < m.Agents.Count ? " · 본 이 " + m.NameOf(c.DxBy) : "")}", false, Key: DK(10)));
    }

    /// <summary>마음: 속말 · 궁리 / 감정 · 바람 · 마음 건강 막대 (충격의 원인) / 사람됨 · 겪은 일로 달라진 것 / 몸의 시계 · 근무 / 지금 상태.</summary>
    private void MindRows(Sim w, Mind mind, Society? so, Agent a, in Body b, List<PanelRow> rows)
    {
        var c = a.C;
        string inner = b.Alive == 0 ? "" : b.Conscious == 0 ? "(의식이 없다)" : b.Asleep != 0 ? "(자는 중)" : Inner.Of(w, mind, a);
        if (inner.Length > 0) rows.Add(new PanelRow("속말", inner, false, Key: DK(0)));
        string pondered = b.Alive != 0 ? mind.RecentThought(a.Id, w.Tick, Time.Hours(1)) : "";
        if (pondered.StartsWith("생각: ")) pondered = pondered[4..];
        if (pondered.Length > 0) rows.Add(new PanelRow("궁리", TextLint.Clean(pondered), false, Key: DK(1)));
        var feel = new List<(string, float)> { ("두려움", c.Fear), ("슬픔", c.Sad), ("화", c.Anger), ("기쁨", c.Joy), ("긴장", c.Stress) }
            .Where(x => x.Item2 > 0.3f).OrderByDescending(x => x.Item2).Select(x => x.Item1).ToList();
        rows.Add(new PanelRow("느낌", feel.Count > 0 ? string.Join(" · ", feel) : "잔잔하다", false,
            Bars: new[] { Weight("두려움", c.Fear), Weight("슬픔", c.Sad), Weight("화", c.Anger), Bar("기쁨", c.Joy), Weight("긴장", c.Stress) }, Key: DK(2)));
        var want = new List<(string, float)> { ("배고픔", c.Hunger), ("목마름", c.Thirst), ("졸림", c.Sleepy), ("외로움", c.Lonely), ("심심함", c.Bored), ("몸 쓰고 싶음", c.Unfit) }
            .Where(x => x.Item2 > 0.5f).OrderByDescending(x => x.Item2).Select(x => x.Item1).ToList();
        rows.Add(new PanelRow("바람", want.Count > 0 ? string.Join(" · ", want) : "아쉬운 것이 없다", false,
            Bars: new[] { Weight("배고픔", c.Hunger), Weight("목마름", c.Thirst), Weight("졸림", c.Sleepy), Weight("외로움", c.Lonely), Weight("심심함", c.Bored) }, Key: DK(3)));
        var heart = new List<string>();
        if (c.Shock > 0.1f) heart.Add($"충격 — {ShockWord(c.ShockKind)}{(c.ShockCause > 0 ? $" ({WhyText(c.ShockCause)})" : "")}");
        if (c.Trauma > 0.2f) heart.Add(c.TraumaHigh != 0 ? "외상이 깊다" : "외상이 남았다");
        if (c.Depress > 0.3f) heart.Add("가라앉았다");
        if (c.Anxiety > 0.3f) heart.Add("불안하다");
        if (c.Weary > 0.3f) heart.Add("같은 얼굴 · 같은 하루에 지쳤다");
        if (c.NightmareAt > 0 && w.Tick - c.NightmareAt < Time.Hours(24)) heart.Add("간밤에 악몽을 꾸었다");
        if (c.AttackAt > 0 && w.Tick - c.AttackAt < Time.Hours(24)) heart.Add($"공황 발작 {Time.Clock(c.AttackAt)}");
        rows.Add(new PanelRow("마음 건강", heart.Count > 0 ? string.Join("\n", heart) : "괜찮다", heart.Count > 0,
            Bars: new[] { Weight("충격", c.Shock), Weight("외상", c.Trauma), Weight("우울", c.Depress), Weight("불안", c.Anxiety), Weight("지침", c.Weary) }, Key: DK(4)));
        var changed = new List<string>();
        if (c.Neuro - c.Neuro0 > 0.08f) changed.Add("처음보다 걱정이 많아졌다"); else if (c.Neuro0 - c.Neuro > 0.08f) changed.Add("처음보다 느긋해졌다");
        if (c.Brave - c.Brave0 > 0.08f) changed.Add("맞서며 단단해졌다"); else if (c.Brave0 - c.Brave > 0.08f) changed.Add("겁이 늘었다");
        if (c.Agree - c.Agree0 > 0.08f) changed.Add("다정해졌다"); else if (c.Agree0 - c.Agree > 0.08f) changed.Add("모가 났다");
        rows.Add(new PanelRow("사람됨", Persona(mind, so, a) + (changed.Count > 0 ? "\n겪은 일로 — " + string.Join(" · ", changed) : ""), false, Key: DK(5)));
        string clock = c.Chrono < -0.5f ? "아침형" : c.Chrono > 0.5f ? "저녁형" : "아침 저녁 가리지 않는다";
        rows.Add(new PanelRow("하루", $"몸의 시계 {clock} · 근무 {c.ShiftHour}시부터", false, Key: DK(6)));
        string mode = c.Mode switch { 1 => "도망치는 중", 2 => "공황에 빠졌다", 3 => "숨을 아낀다", _ => "" };
        if (mode.Length > 0) rows.Add(new PanelRow("지금 상태 — " + mode, c.ModeUntil > w.Tick ? $"{Math.Max(1, (c.ModeUntil - w.Tick) * Time.SecondsPerTick / 60):0}분 더" : "", true, Key: DK(7)));
    }

    private static string ShockWord(ShockKind k) => k switch
    {
        ShockKind.Fire => "불", ShockKind.Breach => "구멍", ShockKind.Air => "숨 막힘", ShockKind.Death => "누군가의 죽음", ShockKind.Hurt => "다침", ShockKind.NearDeath => "죽을 뻔했다", _ => "놀람",
    };

    /// <summary>하는 일: 지금 목표 · 몇 분째 · 목표의 원인 / 계획 걸음 (했다 · ▶ 하는 중 · 남았다) / 길 / 맡은 일 / 손 · 등 / 막힘 · 그만둔 까닭 / 제 궁리 · 피하는 곳 · 의심.</summary>
    private void DoingRows(Sim w, Mind mind, Agent a, bool dead, List<PanelRow> rows)
    {
        var c = a.C;
        double min = Math.Max(0, w.Tick - c.GoalSince) / (double)Time.Minutes(1);
        rows.Add(new PanelRow("지금 — " + (dead ? a.IsPerson ? "숨을 거두었다" : "멈췄다" : Mind.GoalName(c.Goal)), dead ? "" : $"{mind.Thought(a.Id).Replace(" — ", " ▸ ")} · {min:0}분째{(c.GoalCause > 0 ? "\n왜: " + WhyText(c.GoalCause) : "")}", false, Key: DK(0)));
        for (int i = 0; i < a.Plan.Count && i < 12; i++)
        {
            string t = mind.StepText(a, a.Plan[i]);
            if (t.Length == 0) continue;
            string where = a.Plan[i].Cell >= 0 ? " · " + RoomNear(a.Plan[i].Cell) : "";
            bool now = i == c.StepIndex;
            rows.Add(new PanelRow((now ? "▶ " : $"{i + 1}. ") + t + where, i < c.StepIndex ? "했다" : now ? "하는 중" : "남았다", now, Key: DK(1 + i)));
        }
        int left = Math.Max(0, a.Path.Count - a.PathIdx);
        if (left > 0) rows.Add(new PanelRow("길", $"남은 칸 {left} · 빠르기 {c.Speed:0.0}m/s{(c.WaitDoor >= 0 && c.WaitTicks > 0 ? " · 문을 기다린다" : "")}", false, Key: DK(20)));
        if (c.AssignedGoal != GoalKind.Idle && c.AssignedUntil > w.Tick)
            rows.Add(new PanelRow("맡은 일 — " + Mind.GoalName(c.AssignedGoal), $"{Math.Max(1, (c.AssignedUntil - w.Tick) * Time.SecondsPerTick / 60):0}분 안에 · 주컴퓨터 · 일감판이 맡겼다", false, Key: DK(21)));
        var hands = new List<string>();
        if (c.Holding != 255 && c.Holding < mind.ItemNames.Length) hands.Add("든 것: " + mind.ItemNames[c.Holding]);
        if (c.Carrying >= 0 && c.Carrying < mind.Agents.Count) hands.Add("업은 이: " + mind.NameOf(c.Carrying));
        if (c.CarriedBy >= 0 && c.CarriedBy < mind.Agents.Count) hands.Add("업혀 간다: " + mind.NameOf(c.CarriedBy));
        if (hands.Count > 0) rows.Add(new PanelRow("손 · 등", string.Join(" · ", hands), false, Key: DK(22)));
        if (c.Snag != SnagKind.None)
            rows.Add(new PanelRow("막힘 — " + Mind.SnagText(c.Snag), $"{(c.SnagCell >= 0 ? RoomNear(c.SnagCell) + " · " : "")}{Time.Clock(c.SnagTick)}{(c.Failed > 0 ? $" · 다시 세운 번 {c.Failed}" : "")}", true, Key: DK(23)));
        if (a.LastFail.Length > 0) rows.Add(new PanelRow("그만둔 까닭", TextLint.Clean(a.LastFail), false, Key: DK(24)));
        if (a.Own.Count > 0 && c.OwnUntil > w.Tick) rows.Add(new PanelRow("제 궁리", string.Join(" → ", a.Own.Take(4).Select(st => mind.StepText(a, st)).Where(t => t.Length > 0)), false, Key: DK(25)));
        var avoid = a.Avoid.Where(v => v.Until > w.Tick).Select(v => Mind.GoalName(v.Goal)).Where(t => t.Length > 0).Distinct().Take(4).ToList();
        if (avoid.Count > 0) rows.Add(new PanelRow("한동안 하지 않는 일", string.Join(" · ", avoid) + " — 막혔던 일을 되풀이하지 않는다", false, Key: DK(26)));
    }

    private static string HowKnown(BeliefSource s) => s switch
    {
        BeliefSource.Saw => "봤다", BeliefSource.Heard => "들었다", BeliefSource.Smelled => "냄새로", BeliefSource.Felt => "몸으로 느꼈다", BeliefSource.Told => "들은 말", _ => "화면으로",
    };

    /// <summary>믿음이 지금 실제와 맞나 (화면만 — 참을 읽어 맞대기만): 맞다 · 이미 아니다 · '' (지나간 소리 · 느낌 — 맞대지 않는다).</summary>
    private string Truth(Sim w, in Belief bf)
    {
        var f = w.Find<Field>(); var ch = w.Find<Chemistry>(); var st = w.Find<Structure>(); var net = w.Find<Network>(); var life = w.Find<Life>(); var mind = w.Find<Mind>();
        int c = bf.Cell;
        bool In(int x) => x >= 0 && x < w.Layout.N;
        switch (bf.Kind)
        {
            case BeliefKind.Fire: return ch != null && In(c) && ch.Burning[c] != 0 ? "맞다" : "이미 아니다";
            case BeliefKind.Breach: return f != null && In(c) && f.Hole[c] > 0 ? "맞다" : "이미 아니다";
            case BeliefKind.Vacuum: return f != null && In(c) && f.Pressure(c) < 50 ? "맞다" : "이미 아니다";
            case BeliefKind.Water: return f != null && In(c) && f.Stored(w.Tables.sWater, c) / 55.5 >= 0.5 ? "맞다" : "이미 아니다";
            case BeliefKind.Injured or BeliefKind.Dead when mind != null && life != null && bf.Subject >= 0 && bf.Subject < mind.Agents.Count && mind.Agents[bf.Subject].C.Body >= 0:
            {
                var body = life.Bodies[mind.Agents[bf.Subject].C.Body];
                return (bf.Kind == BeliefKind.Dead ? body.Alive == 0 : body.Conscious == 0 || body.Alive == 0) ? "맞다" : "이미 아니다";
            }
            case BeliefKind.Broken when net != null && bf.Subject >= 0 && bf.Subject < net.Devices.Count:
                return !net.Working(bf.Subject) ? "맞다" : "이미 아니다";
            case BeliefKind.StuckDoor when st != null && bf.Subject >= 0 && bf.Subject < w.Layout.Doors.Count:
                return st.DoorJam[bf.Subject] != 0 ? "맞다" : "이미 아니다";
            default: return "";
        }
    }

    /// <summary>
    /// '누가 어떻게 아나' (docs/UI2.md §2.6 표 — docs/OBSERVER_REVIEW.md §4 '아는 것' 탭에): 갈래마다 사람이 아는 길 · 주컴퓨터가 아는 길 · 이 사람이 지금 아나 (그 갈래의 믿음).
    /// </summary>
    private static readonly (string what, string person, string computer, BeliefKind[] kinds)[] WhoKnows =
    {
        ("불 · 폭발", "보거나 · 냄새 · 쾅 소리 · 경보로", "연기 · 열 센서 (전기가 없는 방은 못 본다)", new[] { BeliefKind.Fire, BeliefKind.Smoke, BeliefKind.SmokeSmell, BeliefKind.Bang }),
        ("외판 구멍 · 운석", "쾅 · 쉬익 · 바람 · 귀 먹먹 (곁에 있으면)", "기압 센서 · 운석은 레이더가 잡으면", new[] { BeliefKind.Breach, BeliefKind.Vacuum, BeliefKind.Leak }),
        ("전선 끊기", "불꽃 · 어두워짐으로", "차단기 · 장치 상태 · 전기가 끊긴 방은 못 본다", new[] { BeliefKind.Spark }),
        ("센서 선 끊기", "아무도 모른다 (선이 상한 것만)", "그 너머 방을 못 본다 → 로봇이 잇는다", Array.Empty<BeliefKind>()),
        ("설비 고장 · 중력", "기계 소리 · 몸이 뜨는 것으로", "장치 상태 · 봉함 샘은 기체 센서 · 중력은 방송", new[] { BeliefKind.Broken, BeliefKind.Grind, BeliefKind.GravityBack }),
        ("물관 · 냉각관 샘", "바닥 물 · 매운 냄새로", "물탱크가 준다 · 암모니아 센서", new[] { BeliefKind.Water, BeliefKind.BadAir }),
        ("문 끼임", "쾅 소리 · 지나려다", "문 상태", new[] { BeliefKind.StuckDoor, BeliefKind.Strain }),
        ("산소 센서 어긋남", "아무도 모른다", "제 방 센서와 견줘 알아챈다 — 늦으면 산소가 짙어진다", Array.Empty<BeliefKind>()),
        ("파편 구름 · 별 폭발", "방송으로", "센서 배열 · 엑스선 감시 → 대피 방송", new[] { BeliefKind.Burn }),
    };

    /// <summary>아는 것 — 믿음마다 (무엇 · 어디 · 어떻게 · 확신 · 확인한 때 · 지금 실제 — 사람 · 로봇이 같은 줄).</summary>
    private void BeliefRows(Sim w, Mind mind, Agent a, List<PanelRow> rows)
    {
        var beliefs = a.Beliefs.Select((bf, i) => (bf, i)).OrderByDescending(x => x.bf.Seen).ThenBy(x => x.i).Take(24).ToList();
        if (beliefs.Count == 0) rows.Add(new PanelRow("아는 일이 없다", "보거나 듣거나 냄새 맡거나 들은 말로만 안다", false, Key: DK(0)));
        foreach (var (bf, i) in beliefs)
        {
            string what = Mind.BeliefName(bf.Kind);
            if (what.Length == 0) continue;
            string subject = bf.Kind is BeliefKind.Injured or BeliefKind.Dead && bf.Subject >= 0 && bf.Subject < mind.Agents.Count ? " — " + mind.NameOf(bf.Subject) : "";
            string truth = Truth(w, bf);
            double ago = Math.Max(0, w.Tick - bf.Seen) * Time.SecondsPerTick / 60;
            rows.Add(new PanelRow($"{what}{subject} — {RoomNear(bf.Cell)}", $"{HowKnown(bf.Src)} · 확신 {bf.Conf * 100:0}% · {(ago < 1 ? "방금" : $"{ago:0}분 전")} 확인{(truth.Length > 0 ? " · 지금 실제: " + truth : "")}", truth == "이미 아니다",
                Bars: new[] { Bar("확신", bf.Conf) }, Key: DK(100 + i)));
        }
    }

    /// <summary>아는 것 (사람) — 누가 어떻게 아나 / 분야 앎 · 솜씨 / 교훈 · 조심.</summary>
    private void KnowsRows(Sim w, Mind mind, Agent a, List<PanelRow> rows)
    {
        var c = a.C;
        foreach (var (what, person, computer, kinds) in WhoKnows)
        {
            var known = a.Beliefs.Where(bf => kinds.Contains(bf.Kind)).OrderByDescending(bf => bf.Seen).ToList();
            string mine = known.Count > 0 ? $"이 사람은 안다 — {Mind.BeliefName(known[0].Kind)} · {RoomNear(known[0].Cell)} · {HowKnown(known[0].Src)}" : "이 사람은 모른다";
            rows.Add(new PanelRow($"누가 어떻게 아나 — {what}", $"사람: {person} · {mine}\n주컴퓨터: {computer}", false, Key: DK(200 + Array.FindIndex(WhoKnows, x => x.what == what))));
        }
        static string Know(float v) => v > 0.7f ? "잘 안다" : v > 0.35f ? "조금 안다" : "모른다";
        rows.Add(new PanelRow("분야 앎", $"기압 {Know(c.KnPressure)} · 전기 {Know(c.KnElectric)} · 화학 {Know(c.KnChem)} · 몸 {Know(c.KnBody)} · 구조 {Know(c.KnStruct)}", false,
            Bars: new[] { Bar("기압", c.KnPressure), Bar("전기", c.KnElectric), Bar("화학", c.KnChem), Bar("몸", c.KnBody), Bar("구조", c.KnStruct) }, Key: DK(300)));
        rows.Add(new PanelRow("솜씨 — 고치기 · 돌보기 · 불 · 막기", "", false, Bars: new[] { Bar("고치기", c.SkRepair), Bar("돌보기", c.SkMedic), Bar("불", c.SkFire), Bar("막기", c.SkSeal) }, Key: DK(301)));
        rows.Add(new PanelRow("솜씨 — 요리 · 조종 · 재배 · 과학", "", false, Bars: new[] { Bar("요리", c.SkCook), Bar("조종", c.SkPilot), Bar("재배", c.SkFarm), Bar("과학", c.SkScience) }, Key: DK(302)));
        var lesson = new List<string>();
        if (c.LessonFire > 0.1f) lesson.Add("불을 겪고 배웠다");
        if (c.LessonBreach > 0.1f) lesson.Add("구멍을 겪고 배웠다");
        if (c.LessonAir > 0.1f) lesson.Add("나쁜 공기를 겪고 배웠다");
        if (c.Careful > 0.1f) lesson.Add("실수 뒤로 조심한다");
        if (lesson.Count > 0) rows.Add(new PanelRow("교훈", string.Join(" · ", lesson), false, Key: DK(303)));
    }

    /// <summary>사람 사이: 짝 · 무리 · 별명 · 지난 일 / 가까운 이 다섯 (정 · 믿음 · 앙금 막대) / 앙금 / 기억 열둘 / 최근 한 말 다섯.</summary>
    private void PeopleRows(Sim w, Mind mind, Society? so, Agent a, List<PanelRow> rows)
    {
        var near = NearBits(mind, so, a.Id);
        rows.Add(new PanelRow("짝 · 무리 · 지난 일", near.Count > 0 ? string.Join("\n", near) : "아직 가까운 이가 없다", false, Key: DK(0)));
        if (so == null) return;
        var others = mind.Agents.Where(o => o.IsPerson && o.Id != a.Id).OrderByDescending(o => so.BondOf(a.Id, o.Id)).ThenBy(o => o.Id).Take(5).ToList();
        foreach (var o in others)
            rows.Add(new PanelRow(mind.NameOf(o.Id), so.GrudgeOf(a.Id, o.Id) > Society.GrudgeAt ? "앙금이 있다" : so.BondOf(a.Id, o.Id) > Society.CloseAt ? "아주 가깝다" : so.BondOf(a.Id, o.Id) > Society.FriendAt ? "친하다" : "",
                false, Bars: new[] { Bar("정", so.BondOf(a.Id, o.Id)), Bar("믿음", so.TrustIn(a.Id, o.Id)), Weight("앙금", so.GrudgeOf(a.Id, o.Id)) }, Key: DK(10 + o.Id)));
        var grudges = mind.Agents.Where(o => o.IsPerson && o.Id != a.Id && so.GrudgeOf(a.Id, o.Id) > Society.GrudgeAt).Select(o => mind.NameOf(o.Id)).ToList();
        if (grudges.Count > 0) rows.Add(new PanelRow("앙금", string.Join(" · ", grudges), true, Key: DK(1)));
        int k = 0;
        foreach (var m in so.Memories.Where(m => m.Owner == a.Id).OrderByDescending(m => m.Tick).Take(Society.MemoriesEach))
            rows.Add(new PanelRow($"기억 — {TalkTable.TopicName(m.Topic)}{(m.About >= 0 && m.About < mind.Agents.Count ? " · " + mind.NameOf(m.About) : "")}", $"{Time.Clock(m.Tick)} · 무게 {m.Weight * 100:0}%{(m.Cause > 0 ? " · " + WhyText(m.Cause) : "")}", false, Key: DK(200 + k++)));
        k = 0;
        foreach (var sy in so.Sayings.Where(x => x.Speaker == a.Id).OrderByDescending(x => x.Tick).Take(5))
            rows.Add(new PanelRow($"한 말 — {HourMin(sy.Tick)}", sy.Line > 0 ? TextLint.Clean(w.Causes.Text(sy.Line)) : TalkTable.TopicName(sy.Topic), false, Key: DK(300 + k++)));
    }

    /// <summary>주컴퓨터가 이 사람에게 한 부탁 · 말 (말 기록에서 그 이름이 든 부탁).</summary>
    private void NoteRowsFor(Sim w, string name, List<PanelRow> rows)
    {
        var notes = w.Find<ShipMind>()?.Notes;
        if (notes == null || name.Length == 0) return;
        for (int k = 0, n = 0; k < notes.Count && n < 8; k++)
        {
            var nt = notes.Newest(k);
            if (nt.Kind != NoteKind.Ask || !nt.Text.Contains(name)) continue;
            rows.Add(new PanelRow($"주컴퓨터의 부탁 — {HourMin(nt.Tick)}", TextLint.Clean(nt.Text), false, Key: DK((1L << 35) + notes.Added - 1 - k))); n++;   // 열쇠는 말의 차례 번호 (새 말에 밀리지 않게)
        }
    }

    /// <summary>로봇 · 드론 자세히 (docs/UI2.md §4.3 — U6): 몸 · 하는 일 · 아는 것 · 일지.</summary>
    private void RobotDetailRows(Sim w, Mind mind, Agent a, List<PanelRow> rows)
    {
        var net = w.Find<Network>();
        bool hasBody = net != null && a.C.Mech >= 0 && a.C.Mech < net.Robots.Count;
        switch (DetailSub)
        {
            case 0: RobotBodyRows(w, a, net, rows); break;
            case 1:
            {
                DoingRows(w, mind, a, hasBody && net!.Robots[a.C.Mech].Core >= 1, rows);
                RobotJobRows(w, mind, a, rows);
                break;
            }
            case 2: BeliefRows(w, mind, a, rows); break;
            default: JudgedRows(a, rows); RobotNoteRows(w, a, rows); break;   // 일지 · 주컴퓨터와 주고받은 것
        }
    }

    /// <summary>로봇 몸: 전지 (충전 중 · 독) / 다리 · 팔 · 눈 · 속 막대와 원인 · 그 몸이 못 하는 것 / 보는 거리 / 부품 주머니 / 떠 있음.</summary>
    private void RobotBodyRows(Sim w, Agent a, Network? net, List<PanelRow> rows)
    {
        if (net == null || a.C.Mech < 0 || a.C.Mech >= net.Robots.Count) { rows.Add(new PanelRow($"전지 {a.C.Battery * 100:0}% · 손상 {a.C.Damage * 100:0}%", "", false, Bars: new[] { Bar("전지", a.C.Battery) }, Key: DK(0))); return; }
        var rb = net.Robots[a.C.Mech];
        string dock = rb.Docked != 0 ? (rb.Battery < 0.99f ? "독에서 충전 중" : "독에서 다 찼다") : rb.Battery < 0.15f ? "전지가 바닥이다" : rb.Battery < 0.35f ? "전지가 적다" : "";
        rows.Add(new PanelRow($"전지 {rb.Battery * 100:0}%", dock, rb.Battery < 0.15f, Bars: new[] { Bar("전지", rb.Battery) }, Key: DK(0)));
        foreach (var (name, v, cause, cant, k) in new[]
        {
            ("다리", rb.Legs, rb.LegsCause, rb.Legs >= 1 ? "걷지 못한다" : rb.Legs > 0.5f ? "느리게 걷는다" : "", 1),
            ("팔", rb.Arms, rb.ArmsCause, !Network.RobotHands(rb) ? "손을 못 쓴다" : "", 2),
            ("눈", rb.Eyes, rb.EyesCause, rb.Eyes > 0.5f ? "흐리게 본다" : "", 3),
            ("속", rb.Core, rb.CoreCause, rb.Core >= 1 ? "멈췄다" : "", 4),
        })
        {
            var bits = new List<string>();
            if (cant.Length > 0) bits.Add(cant);
            if (cause > 0) bits.Add("왜: " + WhyText(cause));
            rows.Add(new PanelRow(v > 0.05f ? $"{name} — 상함 {Math.Min(1, v) * 100:0}" : $"{name} — 멀쩡하다", string.Join("\n", bits), v > 0.5f, Bars: new[] { Bar(name, 1 - v) }, Key: DK(k)));
        }
        rows.Add(new PanelRow("보는 거리", $"{12 * (1 - 0.8 * Math.Clamp(rb.Eyes, 0, 1)):0}칸 (눈이 상할수록 줄어든다)", false, Key: DK(5)));
        var spares = new List<string>();
        foreach (var p in new[] { Part.Electric, Part.Mechanical, Part.Seal, Part.Filter }) if ((a.C.Spares & (1 << (int)p)) != 0) spares.Add(Device.PartName(p));
        rows.Add(new PanelRow("부품 주머니", spares.Count > 0 ? string.Join(" · ", spares) : "비었다 — 고칠 때마다 그 갈래를 쓴다", false, Key: DK(6)));
        if (a.C.Floated != 0) rows.Add(new PanelRow("떠 있다", "무중력 — 손잡이를 잡고 간다", true, Key: DK(7)));
    }

    /// <summary>로봇 하는 일의 덧붙임: 맡은 사건 (방 · 갈래 · 몇 분째 · 막힘) · 맡은 일감 · 맡은 까닭 (로봇 카드와 같은 글).</summary>
    private void RobotJobRows(Sim w, Mind mind, Agent a, List<PanelRow> rows)
    {
        var cm = w.Find<ShipMind>();
        if (cm != null)
        {
            int k = 0;
            foreach (var x in cm.Incidents)
            {
                if (x.Active == 0 || x.RobotA != a.Id && x.RobotB != a.Id) continue;
                double min = Math.Max(0, w.Tick - x.Since) * Time.SecondsPerTick / 60;
                rows.Add(new PanelRow($"맡은 사건 — {cm.RoomName(x.Room)} {cm.IncidentName(x)}", $"{min:0}분째{(x.Snag != SnagKind.None ? " · 막힘: " + Mind.SnagText(x.Snag) : "")}{(x.Sealed != 0 ? " · 봉쇄했다" : "")}", x.Snag != SnagKind.None, Key: DK(40 + k++)));
            }
            k = 0;
            foreach (var j in cm.Jobs)
                if (j.Agent == a.Id) rows.Add(new PanelRow("맡은 일감 — " + Mind.GoalName(j.Kind), j.Until > w.Tick ? $"{Math.Max(1, (j.Until - w.Tick) * Time.SecondsPerTick / 60):0}분 안에" : "", false, Key: DK(60 + k++)));
        }
        if (AssignedWhy(w, a) is { Length: > 0 } why) rows.Add(new PanelRow("맡은 까닭", why, false, Key: DK(70)));   // 로봇 카드와 같은 글 (LiveRobots.AssignedWhy)
    }

    /// <summary>주컴퓨터와 주고받은 것 (말 기록에서 그 로봇 줄 — 맡김 · 미룸 · 놓아줌 · 막힘).</summary>
    private void RobotNoteRows(Sim w, Agent a, List<PanelRow> rows)
    {
        var notes = w.Find<ShipMind>()?.Notes;
        if (notes == null) return;
        for (int k = 0, n = 0; k < notes.Count && n < 12; k++)
        {
            var nt = notes.Newest(k);
            if (nt.Robot != a.Id) continue;
            string kind = nt.Kind switch { NoteKind.Assign => "맡김", NoteKind.Defer => "미룸", NoteKind.Release => "놓아줌", NoteKind.Block or NoteKind.Stuck => "막힘", _ => "말" };
            rows.Add(new PanelRow($"주컴퓨터 {kind} — {HourMin(nt.Tick)}", TextLint.Clean(nt.Text) + (nt.Reason != 0 && ShipMind.ReasonText(nt.Reason) is { Length: > 0 } why ? " · " + why : ""), false, Key: DK((1L << 35) + notes.Added - 1 - k))); n++;
        }
    }

    /// <summary>판단 일지 (새것 위 · 사람 · 로봇마다 32칸 — <see cref="Mind.JudgedKeep"/>).</summary>
    private void JudgedRows(Agent a, List<PanelRow> rows)
    {
        var jr = a.Judged;
        for (int k = 0; k < (jr?.Count ?? 0); k++)
        {
            ref var j = ref jr!.Newest(k);
            rows.Add(new PanelRow(JudgedLine(j), "", j.Out is JudgeOut.Blocked or JudgeOut.CantPlan, Key: DK(Math.Min(jr.Added - 1 - k, (1L << 35) - 1))));   // 열쇠는 그 판단의 차례 번호 (검토 고침 — 틱 × 32 + 칸 차례는 새 판단마다 밀려 고른 줄이 다른 판단으로 넘어갔다)
        }
        if (rows.Count == 0) rows.Add(new PanelRow("아직 적힌 판단이 없다", "목표를 세우거나 막히거나 그만두면 여기에 적힌다", false, Key: -1));
    }

    /// <summary>로봇 판 (G — docs/UI2.md §4.1 · 결함 4): 머리 줄 '로봇 n대 · 드론 m대' · 줄마다 전지 · 다리 · 팔 · 눈 막대와 로봇 카드 글.</summary>
    private void RobotRows(List<PanelRow> rows)
    {
        var w = World; var mind = w.Find<Mind>(); var net = w.Find<Network>();
        if (mind == null) return;
        var chips = RobotBar();
        if (chips.Length == 0) { rows.Add(new PanelRow("이 배에는 로봇이 없다", "", false, Key: -1)); return; }
        int drones = chips.Count(x => mind.Agents[x.Id].C.Kind == AgentKind.Drone), robots = chips.Length - drones;
        int stopped = chips.Count(x => x.Level == 3), stuck = chips.Count(x => x.Level == 2);
        rows.Add(new PanelRow($"로봇 {robots}대{(drones > 0 ? $" · 드론 {drones}대" : "")}", stopped + stuck > 0 ? $"멈춤 {stopped} · 못 움직임 · 전지 바닥 {stuck}" : "모두 움직인다", false, Key: -1));
        foreach (var chip in chips)
        {
            var ra = mind.Agents[chip.Id];
            float battery = ra.C.Battery, legs = 0, arms = 0, eyes = ra.C.Damage;
            if (net != null && ra.C.Mech >= 0 && ra.C.Mech < net.Robots.Count) { var rb = net.Robots[ra.C.Mech]; battery = rb.Battery; legs = rb.Legs; arms = rb.Arms; eyes = rb.Eyes; }
            var card = new List<string>();
            RobotCard(w, mind, ra, card);
            rows.Add(new PanelRow($"{mind.NameOf(chip.Id)} — {(chip.Level == 3 ? "멈췄다" : Mind.GoalName(ra.C.Goal))}", string.Join("\n", card), chip.Level >= 2,
                Bars: new[] { Bar("전지", battery), Bar("다리", 1 - legs), Bar("팔", 1 - arms), Bar("눈", 1 - eyes) }, Key: KeyPerson(chip.Id)));
        }
    }
}
