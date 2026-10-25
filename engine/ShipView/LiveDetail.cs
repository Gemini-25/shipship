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
        DetailKind.Person => PersonTabs, DetailKind.Robot => RobotTabs, DetailKind.Computer => ComputerTabs, _ => Array.Empty<string>(),
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
        if (Detail == DetailKind.Computer)
        {
            var sm = w.Find<ShipMind>();
            if (sm == null) return "이 배에는 주컴퓨터가 없다";
            var bits = new List<string> { sm.Online ? "켜짐" : "멈췄다" };
            if (sm.Throttled != 0) bits.Add("열 때문에 느려졌다");
            if (sm.IntakeK > 0) bits.Add($"빨아들이는 공기 {sm.IntakeK - 273.15:0}℃");
            int open = sm.Incidents.Count(x => x.Active != 0);
            bits.Add(open > 0 ? $"붙잡은 사건 {open}" : "붙잡은 사건 없음");
            return string.Join(" · ", bits);
        }
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
        if (Detail == DetailKind.Computer)
        {
            if (DetailSub == 0) { JournalRows(rows); return; }
            string soon = DetailSub switch
            {
                1 => "주컴퓨터가 한 예측과 실제 · 맞음 몫", 2 => "방마다 센서로 아는 값과 실제 · 모르는 일", 3 => "일감판 · 못 한 일감 · 막힌 곳 · 배급",
                4 => "주컴퓨터가 한 말 · 해 본 방법 · 지어 본 방법", _ => "제 몸 · 센서가 닿는 곳 · 함장 방침 · 이 항해의 셈",
            };
            rows.Add(new PanelRow("곧 — " + soon, "지금은 '지금' 탭에서 사건 · 배 형편 · 일지를 본다", false, Key: -1));
            return;
        }
        var a = DetailAgent();
        var w = World; var mind = w.Find<Mind>();
        if (a == null || mind == null) { rows.Add(new PanelRow("그 이를 찾을 수 없다", "목록으로 돌아간다 (오른쪽 누르기)", false, Key: -1)); return; }
        if (a.IsPerson) PersonRows(w, mind, a, rows); else RobotDetailRows(w, mind, a, rows);
    }

    private long DK(int n) => KeyDetail(DetailSub, n);

    /// <summary>사람 자세히: 몸 · 마음 · 하는 일 · 아는 것 · 사람 사이 · 일지 (카드 · 사람 판의 글을 나눠 싣는다).</summary>
    private void PersonRows(Sim w, Mind mind, Agent a, List<PanelRow> rows)
    {
        var life = w.Find<Life>(); var so = w.Find<Society>();
        if (life == null || a.C.Body < 0) return;
        var b = life.Bodies[a.C.Body];
        var c = a.C;
        switch (DetailSub)
        {
            case 0:   // 몸
            {
                if (b.Alive == 0) { rows.Add(new PanelRow("숨을 거두었다 — " + Time.Clock(b.DiedAt), b.DeathCause != 0 ? w.Causes.Story(b.DeathCause) : "", true, Key: DK(0))); break; }
                string state = b.Conscious == 0 ? "쓰러져 있다" : b.Asleep != 0 ? "자는 중" : "깨어 있다";
                rows.Add(new PanelRow($"숨 · 피 — {state}", $"핏속 산소 {b.SpO2 * 100:0}% · 체온 {b.CoreK - 273.15:0.0}℃{(b.Suit != 0 ? " · 우주복" : "")}", b.Conscious == 0 || b.SpO2 < 0.85f,
                    Bars: new[] { Bar("숨", (b.SpO2 - 0.7f) / 0.28f), Bar("배", 1 - c.Hunger), Bar("잠", 1 - c.Sleepy) }, Key: DK(0)));
                var hurt = new List<string>();
                if (b.Burn > 0.05f) hurt.Add($"화상 {b.Burn * 100:0}");
                if (b.Bleed > 0.01f) hurt.Add("피가 난다");
                if (b.Blunt > 0.05f) hurt.Add($"타박 {b.Blunt * 100:0}");
                if (b.LungDamage > 0.05f) hurt.Add($"폐 {b.LungDamage * 100:0}");
                if (b.COHb > 0.05f) hurt.Add($"일산화탄소 {b.COHb * 100:0}%");
                if (b.Hypoxia > 0.1f) hurt.Add("숨이 모자랐다");
                rows.Add(new PanelRow("다침", hurt.Count > 0 ? string.Join(" · ", hurt) : "다친 데 없다", hurt.Count > 0, Key: DK(1)));
                var ill = new List<string>();
                if (b.Infect > 0.05f) ill.Add("상처가 곪는다");
                if (b.Ill == 2) ill.Add("앓는다");
                if (ill.Count > 0) rows.Add(new PanelRow("병", string.Join(" · ", ill), true, Key: DK(2)));
                break;
            }
            case 1:   // 마음
            {
                string inner = b.Alive == 0 ? "" : b.Conscious == 0 ? "(의식이 없다)" : b.Asleep != 0 ? "(자는 중)" : Inner.Of(w, mind, a);
                if (inner.Length > 0) rows.Add(new PanelRow("속말", inner, false, Key: DK(0)));
                string pondered = b.Alive != 0 ? mind.RecentThought(a.Id, w.Tick, Time.Hours(1)) : "";
                if (pondered.StartsWith("생각: ")) pondered = pondered[4..];
                if (pondered.Length > 0) rows.Add(new PanelRow("궁리", TextLint.Clean(pondered), false, Key: DK(1)));
                var feel = new List<(string, float)> { ("두려움", c.Fear), ("슬픔", c.Sad), ("화", c.Anger), ("기쁨", c.Joy), ("긴장", c.Stress) }
                    .Where(x => x.Item2 > 0.3f).OrderByDescending(x => x.Item2).Select(x => x.Item1).ToList();
                rows.Add(new PanelRow("느낌", feel.Count > 0 ? string.Join(" · ", feel) : "잔잔하다", false,
                    Bars: new[] { Weight("두려움", c.Fear), Weight("슬픔", c.Sad), Weight("화", c.Anger), Weight("긴장", c.Stress) }, Key: DK(2)));
                rows.Add(new PanelRow("사람됨", Persona(mind, so, a), false, Key: DK(3)));
                break;
            }
            case 2:   // 하는 일
            {
                double min = Math.Max(0, w.Tick - c.GoalSince) / (double)Time.Minutes(1);
                rows.Add(new PanelRow("지금 — " + (b.Alive == 0 ? "숨을 거두었다" : Mind.GoalName(c.Goal)), b.Alive == 0 ? "" : $"{mind.Thought(a.Id).Replace(" — ", " ▸ ")} · {min:0}분째", false, Key: DK(0)));
                if (a.LastFail.Length > 0) rows.Add(new PanelRow("그만둔 까닭", TextLint.Clean(a.LastFail), false, Key: DK(1)));
                break;
            }
            case 3:   // 아는 것
                rows.Add(new PanelRow("곧 — 믿음마다 무엇 · 어디 · 어떻게 · 확신 · 지금 실제", "분야 앎 · 솜씨 · 교훈도 여기에", false, Key: -1));
                break;
            case 4:   // 사람 사이
            {
                var near = NearBits(mind, so, a.Id);
                rows.Add(new PanelRow("가까운 이 · 지난 일", near.Count > 0 ? string.Join("\n", near) : "아직 가까운 이가 없다", false, Key: DK(0)));
                break;
            }
            default: JudgedRows(a, rows); break;   // 일지
        }
    }

    /// <summary>로봇 · 드론 자세히: 몸 · 하는 일 · 아는 것 · 일지 (로봇 카드의 글을 나눠 싣는다).</summary>
    private void RobotDetailRows(Sim w, Mind mind, Agent a, List<PanelRow> rows)
    {
        var net = w.Find<Network>();
        switch (DetailSub)
        {
            case 0:   // 몸
            {
                if (net == null || a.C.Mech < 0 || a.C.Mech >= net.Robots.Count) { rows.Add(new PanelRow($"전지 {a.C.Battery * 100:0}% · 손상 {a.C.Damage * 100:0}%", "", false, Bars: new[] { Bar("전지", a.C.Battery) }, Key: DK(0))); break; }
                var rb = net.Robots[a.C.Mech];
                rows.Add(new PanelRow($"전지 {rb.Battery * 100:0}%", rb.Battery < 0.15f ? "전지가 바닥이다" : rb.Battery < 0.35f ? "전지가 적다" : "", rb.Battery < 0.15f, Bars: new[] { Bar("전지", rb.Battery) }, Key: DK(0)));
                var hurt = new List<string>();
                if (rb.Legs > 0.05f) hurt.Add($"다리 {rb.Legs * 100:0}{(rb.Legs >= 1 ? " (못 걷는다)" : "")}");
                if (rb.Arms > 0.05f) hurt.Add($"팔 {rb.Arms * 100:0}{(!Network.RobotHands(rb) ? " (손을 못 쓴다)" : "")}");
                if (rb.Eyes > 0.05f) hurt.Add($"센서 {rb.Eyes * 100:0}");
                if (rb.Core > 0.05f) hurt.Add($"속 {rb.Core * 100:0}{(rb.Core >= 1 ? " (멈췄다)" : "")}");
                int why = rb.Core >= 1 ? rb.CoreCause : rb.Legs >= rb.Arms && rb.Legs > 0.05f ? rb.LegsCause : rb.Arms > 0.05f ? rb.ArmsCause : rb.Eyes > 0.05f ? rb.EyesCause : 0;
                rows.Add(new PanelRow(hurt.Count > 0 ? "상함 — " + string.Join(" · ", hurt) : "몸 — 멀쩡하다", why != 0 ? "왜: " + w.Causes.Story(why) : "", hurt.Count > 0,
                    Bars: new[] { Bar("다리", 1 - rb.Legs), Bar("팔", 1 - rb.Arms), Bar("눈", 1 - rb.Eyes), Bar("속", 1 - rb.Core) }, Key: DK(1)));
                break;
            }
            case 1:   // 하는 일
            {
                var card = new List<string>();
                RobotCard(w, mind, a, card);
                for (int i = 0; i < card.Count; i++)
                {
                    int colon = card[i].IndexOf(": ", StringComparison.Ordinal);
                    rows.Add(colon > 0 ? new PanelRow(card[i][..colon], card[i][(colon + 2)..].Replace(" / ", "\n"), false, Key: DK(i)) : new PanelRow(card[i], "", false, Key: DK(i)));
                }
                break;
            }
            case 2:   // 아는 것
                rows.Add(new PanelRow("곧 — 믿음마다 무엇 · 어디 · 어떻게 · 확신 · 지금 실제", "", false, Key: -1));
                break;
            default: JudgedRows(a, rows); break;   // 일지
        }
    }

    /// <summary>판단 일지 (새것 위 · 지금은 사람마다 여덟 칸 — 32칸은 U5).</summary>
    private void JudgedRows(Agent a, List<PanelRow> rows)
    {
        var jr = a.Judged;
        for (int k = 0; k < (jr?.Count ?? 0); k++)
        {
            ref var j = ref jr!.Newest(k);
            rows.Add(new PanelRow(JudgedLine(j), "", j.Out is JudgeOut.Blocked or JudgeOut.CantPlan, Key: DK((int)Math.Min(j.Tick * 8 + (k & 7), (1L << 36) - 1))));
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
