using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.17 잡담 · 캠프의 밤.
// 잡담: 복도 · 방에서 스쳐 지나가며 두 사람이 나누는 짧은 말 (자리 잡고 하는 수다 = v17.8 ReactTalk과 다르다).
//   재료: 최근 사건 · 연애 소문 · 털어놓은 이야기 · 출신(고향 · 집안 · 세대 · 믿음) · 그 방 · 둘 사이.
//   같은 사건이라도 누가 누구에게 말하느냐에 따라 말이 달라진다 (고향마다 비유 · 세대마다 태도 · 믿음마다 해석 · 집안 차이의 가시 · 사이의 온도).
// 캠프의 밤: 하루 끝, 근무가 없는 사람들이 식당(없으면 휴게실)에 모인다. 그날 쌓인 일(사고 · 다툼 · 털어놓지 못한 이야기 · 연애)로
//   대화 · 고백 · 다툼 · 화해 장면이 이어진다. 모인 사람만 겪는다.

public sealed class CampNight
{
    public int Day { get; init; }
    public int RoomId { get; init; }
    public long Start { get; init; }
    public long End { get; init; }
    public long NextScene { get; set; }
    public List<int> Came { get; } = new();
    public List<(long t, string kind, string text)> Scenes { get; } = new();
    public HashSet<string> Used { get; } = new();
    public bool Closed { get; set; }
}

public sealed partial class StorySystem
{
    public List<CampNight> Camps { get; } = new();
    public CampNight? Camp => Camps.Count > 0 && !Camps[^1].Closed ? Camps[^1] : null;
    /// <summary>캠프의 밤이 열리는 시각 (배 시간).</summary>
    public float CampHour { get; set; } = 20.5f;
    private int _campDay = -1;
    private long _lastCamp = 0;
    public List<(long t, int a, int b, string la, string lb, string topic)> Chatter { get; } = new();
    private readonly SortedDictionary<long, long> _pairTalk = new();
    private readonly SortedDictionary<int, long> _personTalk = new();

    // ───────────────────────────── 잡담 ─────────────────────────────

    private bool Passable(CrewMember c) =>
        Adult(c) && c.CanAct && c.IsAwake && c.Room != null && c.Job?.Activity is not (ChatActivity or StoryActivity) && c.Job?.Urgent != true && !c.Mind.Panicking(_w.Tick)
        && !(_personTalk.TryGetValue(c.Id, out long t) && _w.Tick < t);

    private void SmallTalkTick()
    {
        var w = _w;
        if (Crisis.Acting(w) || Camp != null) return;
        int made = 0;
        var crew = w.Crew;
        for (int i = 0; i < crew.Count && made < 2; i++)
        {
            var a = crew[i];
            if (!Passable(a)) continue;
            for (int j = i + 1; j < crew.Count; j++)
            {
                var b = crew[j];
                if (b.Room != a.Room || !Passable(b) || (a.Position - b.Position).LengthSquared() > 5f) continue;
                long key = Math.Min(a.Id, b.Id) * 4096L + Math.Max(a.Id, b.Id);
                if (_pairTalk.TryGetValue(key, out long nx) && w.Tick < nx) continue;
                float p = 0.18f + 0.2f * (a.Traits.Sociability + b.Traits.Sociability) * 0.5f - (a.Habits.Contains(Habit.Loner) || b.Habits.Contains(Habit.Loner) ? 0.1f : 0f);
                _pairTalk[key] = w.Tick + SimTime.Hours(6);
                if (!R.Chance(p)) continue;
                var (first, second) = a.Traits.Sociability >= b.Traits.Sociability ? (a, b) : (b, a);
                var (la, lb, topic) = Passing(first, second, null);
                first.Say(w, Persona.Say(first, la));
                second.Say(w, Persona.Say(second, lb));
                Chatter.Add((w.Tick, first.Id, second.Id, la, lb, topic));
                if (Chatter.Count > 60) Chatter.RemoveAt(0);
                Stats.SmallTalks++;
                _personTalk[a.Id] = _personTalk[b.Id] = w.Tick + SimTime.Hours(1);
                first.Needs.Social = MathF.Min(1f, first.Needs.Social + 0.03f);
                second.Needs.Social = MathF.Min(1f, second.Needs.Social + 0.03f);
                var ra = RootsOf(first); var rb = RootsOf(second);
                float warm = first.AffinityTo(second) < -0.2f ? -0.01f : ra.Home == rb.Home ? 0.02f : (ra.Class == 5) != (rb.Class == 5) && (ra.Class <= 1 || rb.Class <= 1) ? -0.005f : 0.008f;
                first.ChangeAffinity(second, warm); second.ChangeAffinity(first, warm);
                made++;
                break;
            }
        }
        if (_pairTalk.Count > 1500) _pairTalk.Clear();
    }

    /// <summary>스쳐 지나가는 말 한 쌍. topic을 주면 그 사건으로 (시험 · 캠프의 밤).</summary>
    public (string a, string b, string topic) Passing(CrewMember a, CrewMember b, string? topic)
    {
        var w = _w;
        var ra = RootsOf(a); var rb = RootsOf(b);
        float rel = MathF.Min(a.AffinityTo(b), b.AffinityTo(a));
        int temp = rel >= 0.3f ? 2 : rel <= -0.2f ? 0 : 1;
        int h = (a.Id * 31 + b.Id * 17 + SimTime.Day(w.Tick)) & 0x7fffffff;
        if (topic == null)
        {
            // 소문 · 걱정 · 최근 사건 · 출신 · 그 방 — 둘에 따라 먼저 떠오르는 게 다르다
            var cands = new List<Func<(string, string, string)?>>
            {
                () => Gossip(a, b, temp),
                () => Worry(a, b),
                () => RecentEvent() is string ev ? EventLines(a, b, ra, rb, temp, ev, h) : ((string, string, string)?)null,
                () => OriginLines(a, b, ra, rb, temp, h),
                () => PlaceLines(a, b, ra, temp),
            };
            for (int k = 0; k < cands.Count; k++)
            {
                var got = cands[(k + h % cands.Count) % cands.Count]();
                if (got.HasValue) return got.Value;
            }
            return PlaceLines(a, b, ra, temp)!.Value;
        }
        return EventLines(a, b, ra, rb, temp, topic, h);
    }

    private string? RecentEvent()
    {
        var ev = _w.History.Events;
        for (int i = ev.Count - 1; i >= 0 && _w.Tick - ev[i].Tick < SimTime.Hours(18); i--)
            if (ev[i].Kind is HistoryKind.Incident or HistoryKind.Damage or HistoryKind.Death or HistoryKind.Casualty)
                return (ev[i].Kind == HistoryKind.Death ? "†" : "") + Short(ev[i].Text);
        return null;
    }

    private static string Short(string t) { int i = t.IndexOf(" — ", StringComparison.Ordinal); var s = i > 0 ? t[..i] : t; return s.Length > 26 ? s[..26] + "…" : s; }

    private static readonly string[] HomeLook =
    {
        "바다 폭풍 지나간 다음 날 같더라", "화성 굴에선 이 정도면 그냥 화요일이야", "갱도 무너질 때 나던 소리랑 똑같았어", "정거장이었으면 벌써 대피 방송 나왔지",
        "타이탄 추위에 비하면 별것도 아니야", "구름 도시에선 바닥 흔들리는 게 일상이라", "얼음 갈라지는 소리 같았어", "고리에선 공기 새는 게 제일 무서웠는데",
    };
    private static readonly string[] FaithGrief = { "별길 따라 잘 갔을 거야", "조상님들 곁으로 갔겠지", "그냥… 보고 싶다", "배도 같이 우는 것 같아", "숨 한 번 같이 쉬자" };
    private static readonly string[] GenLook = { "난 이런 거 처음이라 아직 손이 떨려", "이럴 때일수록 순서대로 하면 돼", "이런 거 한두 번 겪은 게 아니야", "난 배 밖을 모르니까 이게 다 우리 집 일이야" };
    private static readonly string[] FaithLook = { "별길이 지켜 준 거야", "할머니가 지켜 주셨나 봐", "운이 좋았지 뭐", "우리 늙은 배가 버텨 준 거야", "일단 숨부터 고르자" };

    private (string, string, string) EventLines(CrewMember a, CrewMember b, Roots ra, Roots rb, int temp, string ev, int h)
    {
        bool death = ev.StartsWith('†');
        if (death)
        {
            ev = ev[1..];
            // 죽음 앞에서는 믿음대로 말한다 — 같은 믿음이면 맞장구, 다르면 제 식으로
            string ga = temp == 0 ? $"{ev}… 너도 그 사람 좋아했잖아" : $"{ev}… {FaithGrief[ra.Faith]}";
            string gb = temp == 0 ? "지금 그 얘기 하고 싶지 않아" : ra.Faith == rb.Faith ? $"응. 우리 식으로 보내 주자" : FaithGrief[rb.Faith];
            return (ga, gb, "사건:" + ev);
        }
        string flavor = (h % 3) switch { 0 => HomeLook[ra.Home], 1 => GenLook[ra.Gen], _ => FaithLook[ra.Faith] };
        string la = temp switch
        {
            2 => $"{ev}… {b.Name}, 괜찮았어? {flavor}",
            0 => $"{ev}… {flavor}. 너야 상관없겠지만",
            _ => $"{ev}, 들었어? {flavor}",
        };
        string lb;
        if (temp == 0) lb = "그래. 할 말 끝났으면 지나갈게";
        else if (ra.Home == rb.Home) lb = $"역시 {HomeShort[rb.Home]} 사람이네. 나도 딱 그 생각 했어";
        else if (ra.Faith != rb.Faith && (h % 3) == 2) lb = rb.Faith == 2 ? "난 그런 거 안 믿어. 정비가 잘된 거지" : $"난 좀 다르게 봐 — {FaithLook[rb.Faith]}";
        else if (ra.Gen != rb.Gen && (h % 3) == 1) lb = rb.Gen switch { 2 => "젊을 땐 다 그래. 금방 익숙해져", 0 => "고참들은 다 그렇게 말하더라", 3 => GenLook[3], _ => "다들 놀랐지. 그래도 순서대로 하면 돼" };
        else if ((ra.Class == 5) != (rb.Class == 5) && (ra.Class <= 1 || rb.Class <= 1)) lb = rb.Class == 5 ? "다들 너무 호들갑이야" : "넌 이런 일 걱정 안 하고 컸잖아";
        else lb = temp == 2 ? $"너 없었으면 더 무서웠을 거야. {HomeLook[rb.Home]}" : HomeLook[rb.Home];
        return (la, lb, "사건:" + ev);
    }

    private (string, string, string)? OriginLines(CrewMember a, CrewMember b, Roots ra, Roots rb, int temp, int h)
    {
        if (ra.Home == rb.Home)
            return ($"{HomeShort[ra.Home]} 사람끼리 하는 말인데, 거기 밥이 그립다", temp == 0 ? "고향 같다고 다 친한 건 아니야" : "말도 마. 내릴 때 같이 가자", "고향:" + HomeShort[ra.Home]);
        if (ra.Gen != rb.Gen && (h & 1) == 0)
            return (ra.Gen == 2 ? $"요즘 애들은 수동 밸브도 안 만져 봤다며" : ra.Gen == 3 ? "배 밖에선 비가 하늘에서 떨어진다며? 진짜야?" : "고참들은 왜 다 손으로 적어?",
                rb.Gen == 2 ? "화면은 꺼져도 종이는 안 꺼지거든" : rb.Gen == 3 ? "난 이 복도가 고향이야" : "다 이유가 있겠지", "세대");
        if (ra.Class != rb.Class)
            return ($"{Classes[ra.Class]}에서 크면 이런 배도 넓어 보여", ra.Class == 5 && rb.Class <= 1 ? "넓어 보이겠지. 넌 방이 있었으니까" : rb.Class == 5 ? "난 처음엔 좁아서 숨 막혔어" : "우린 다 비슷하게 컸네", "집안");
        if (ra.Faith != rb.Faith && temp > 0)
            return ($"오늘 {(ra.Faith == 0 ? "별길 기도" : ra.Faith == 1 ? "제사 날" : ra.Faith == 4 ? "명상" : "배 이름 부르기")} 할 건데 같이 할래?", rb.Faith == 2 ? "난 됐어. 대신 차 끓여 둘게" : "좋아, 나도 우리 식으로 할게", "믿음");
        return null;
    }

    private (string, string, string)? PlaceLines(CrewMember a, CrewMember b, Roots ra, int temp)
    {
        var room = a.Room;
        if (room == null) return null;
        string la = room.Kind switch
        {
            RoomType.Corridor => "이 복도는 왜 늘 바람이 불지",
            RoomType.Hydroponics or RoomType.Garden => $"흙 냄새 맡으니까 {HomeShort[ra.Home]} 생각난다",
            RoomType.Engine or RoomType.Reactor => "기관 소리가 오늘은 좀 고르네",
            RoomType.Galley or RoomType.Mess => "오늘 국 간이 딱 맞더라",
            RoomType.Observatory or RoomType.Bridge => "저쪽 별은 어제보다 조금 밝네",
            RoomType.Medbay => "여긴 올 때마다 소독약 냄새에 긴장돼",
            RoomType.Gym => "어제 무리했더니 다리가 후들거려",
            _ => $"{Ko.EunNeun(room.Name)} 언제 와도 좀 춥다",
        };
        string lb = temp switch { 2 => "너랑 있으니까 그런 것도 괜찮아", 0 => "…그러네", _ => "그러게, 나도 그 생각 했어" };
        return (la, lb, "장소:" + room.Name);
    }

    private (string, string, string)? Gossip(CrewMember a, CrewMember b, int temp)
    {
        if (temp == 0) return null;
        foreach (var l in Loves)
        {
            if (!l.Together || l.Has(a.Id) || l.Has(b.Id) || _w.Tick - l.Since > SimTime.TicksPerDay * 3) continue;
            if (l.Secret && !l.Public) continue;
            Stats.Gossip++;
            return ($"{Ko.WaGwa(Name(l.A))} {Name(l.B)} 요즘 둘이 붙어 다니는 거 봤어?", a.Habits.Contains(Habit.Talker) || b.Habits.Contains(Habit.Talker) ? "봤지! 식당에서 같은 숟가락 쓰더라" : "남 일엔 신경 끄자. 좋아 보이던데", "소문");
        }
        return null;
    }

    private (string, string, string)? Worry(CrewMember a, CrewMember b)
    {
        // a가 털어놓음을 들은 사람이면 b에게 그 사람 걱정을 한다 (비밀은 지킨다 — 이름만)
        foreach (var arc in Arcs)
        {
            if (!arc.Active || arc.Friend != a.Id || !arc.Confided || arc.Who == b.Id) continue;
            var who = P(arc.Who);
            if (who == null || b.AffinityTo(who) < 0.2f) continue;
            return ($"요즘 {who.Name} 안색이 안 좋더라. 좀 챙겨 줘", "그래? 이따 밥이라도 같이 먹자고 할게", "걱정:" + who.Name);
        }
        return null;
    }

    // ───────────────────────────── 캠프의 밤 ─────────────────────────────

    /// <summary>모일 방: 식당 → 조리실 → 휴게실 → 아무 큰 방.</summary>
    public Room? CampRoom()
    {
        foreach (var k in new[] { RoomType.Mess, RoomType.Lounge, RoomType.Galley })
            foreach (var r in _w.Ship.Rooms) if (r.Kind == k && r.Cells.Count >= 6) return r;
        return _w.Ship.Rooms.Where(r => r.Kind != RoomType.Corridor).OrderByDescending(r => r.Cells.Count).ThenBy(r => r.Id).FirstOrDefault();
    }

    private void CampTick()
    {
        var w = _w;
        int day = SimTime.Day(w.Tick);
        float hour = SimTime.HourOfDay(w.Tick);
        var camp = Camp;
        if (camp == null)
        {
            if (day == _campDay || !SimTime.InWindow(hour, CampHour, 0.25f) || Crisis.Acting(w)) return;
            _campDay = day;
            if (CampRoom() is not Room cr) return;
            camp = new CampNight { Day = day, RoomId = cr.Id, Start = w.Tick, End = w.Tick + SimTime.Hours(1.6f), NextScene = w.Tick + SimTime.Minutes(25) };
            Camps.Add(camp);
            if (Camps.Count > 20) Camps.RemoveAt(0);
            Stats.Camps++;
            return;
        }
        if (w.Tick >= camp.End || Crisis.Acting(w)) { CloseCamp(camp); return; }
        if (w.Tick < camp.NextScene) return;
        camp.NextScene = w.Tick + SimTime.Minutes(20);
        var room = w.Ship.Rooms.FirstOrDefault(r => r.Id == camp.RoomId);
        var here = w.Crew.Where(c => Adult(c) && c.CanAct && c.IsAwake && c.Room == room).ToList();
        foreach (var c in here) if (!camp.Came.Contains(c.Id)) camp.Came.Add(c.Id);
        if (here.Count >= 2) CampScene(camp, here);
    }

    private void CloseCamp(CampNight camp)
    {
        var w = _w;
        camp.Closed = true;
        _lastCamp = w.Tick;
        if (camp.Scenes.Count == 0) return;
        var people = camp.Came.Select(P).Where(c => c != null).Cast<CrewMember>().ToList();
        var room = w.Ship.Rooms.FirstOrDefault(r => r.Id == camp.RoomId);
        w.History.Add(w, HistoryKind.Memory, $"밤 모임 ({room?.Name}, {people.Count}명) — {camp.Scenes[0].text}" + (camp.Scenes.Count > 1 ? $" 외 {camp.Scenes.Count - 1}" : ""), room, people);
        foreach (var c in people) c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.15f);
    }

    private void CampScene(CampNight camp, List<CrewMember> here)
    {
        var w = _w;
        string? kind = null, text = null;
        // 1) 그날 다툰 둘이 마주 앉았다 — 다시 붙거나 화해한다
        if (!camp.Used.Contains("fight"))
            for (int i = 0; i < here.Count && kind == null; i++)
                for (int j = i + 1; j < here.Count && kind == null; j++)
                {
                    var a = here[i]; var b = here[j];
                    if (!(a.Quarrel > _lastCamp && a.Quarrel == b.Quarrel) && MathF.Min(a.AffinityTo(b), b.AffinityTo(a)) > -0.3f) continue;
                    if (a.AffinityTo(b) + b.AffinityTo(a) > -0.2f || a.Habits.Contains(Habit.Patient) || b.Habits.Contains(Habit.Patient))
                    {
                        a.ChangeAffinity(b, 0.12f); b.ChangeAffinity(a, 0.12f);
                        w.Relations.Remember(b, a, RelationReason.Apologized, "밤 모임에서 먼저 잔을 내밀었다");
                        a.Say(w, Persona.Say(a, $"{b.Name}, 아까는 내가 심했다"));
                        b.Say(w, Persona.Say(b, "나도. 한 잔 받아"));
                        Stats.Makeups++;
                        kind = "fight"; text = $"{Ko.WaGwa(a.Name)} {Ko.IGa(b.Name)} 낮의 다툼을 풀었다";
                    }
                    else
                    {
                        a.ChangeAffinity(b, -0.06f); b.ChangeAffinity(a, -0.06f);
                        MindSystem.Anger(a, 0.1f); MindSystem.Anger(b, 0.1f);
                        a.Say(w, Persona.Say(a, "넌 아까 그 말 사과 안 해?"));
                        b.Say(w, Persona.Say(b, "여기서까지 이래야 돼?"));
                        Stats.Fights++;
                        kind = "fight"; text = $"{Ko.WaGwa(a.Name)} {Ko.IGa(b.Name)} 식탁에서 다시 언성을 높였다";
                        var med = here.Where(o => o != a && o != b && Talking(o.Id) == null).OrderByDescending(o => MathF.Min(o.AffinityTo(a), o.AffinityTo(b)) + o.Traits.Sociability * 0.3f).ThenBy(o => o.Id).FirstOrDefault();
                        if (med != null) { var k = Hold(CardKind.Mediate, med, a, -1, b.Id); text += $" — {k.Outcome}"; }
                    }
                }
        // 2) 털어놓지 못한 이야기를 모두 앞에서
        if (kind == null && !camp.Used.Contains("confess"))
            foreach (var c in here)
            {
                if (ArcOf(c) is not Arc a || a.Confided || a.Step + 1 >= a.Spec.Steps.Length || a.Spec.Steps[a.Step + 1].Gate is not (ArcGate.Confide or ArcGate.Strain)) continue;
                if (a.Spec.Theme == ArcTheme.Secret) continue; // 비밀은 여럿 앞에서 꺼내지 않는다
                if (here.Where(o => o != c).OrderByDescending(o => o.AffinityTo(c)).ThenBy(o => o.Id).FirstOrDefault() is not CrewMember f) continue; // 통합7 혼자 남은 밤엔 들어 줄 사람이 없다 (빈 목록 First → 게임이 멈췄다)
                a.Confided = true; a.Friend = f.Id; a.Support += 0.25f;
                foreach (var o in here) if (o != c) { o.ChangeAffinity(c, 0.03f); w.Brain2.Emotions.Feel(o, Feeling.Sadness, 0.06f, a.Title, c); }
                w.Relations.Remember(c, f, RelationReason.Comforted, $"밤 모임에서 {a.Title} 이야기를 들어 줬다");
                c.Say(w, Persona.Say(c, $"사실 요즘… {a.Title} 때문에 잠을 못 자"));
                f.Say(w, Persona.Say(f, "말해 줘서 고마워. 우리가 있잖아"));
                Stats.Confessions++;
                kind = "confess"; text = $"{Ko.IGa(c.Name)} '{a.Title}' 이야기를 꺼냈다 — 다들 말없이 들었다";
                break;
            }
        // 3) 연애: 마음이 간 사람이 같은 식탁에 — 놀림 · 고백
        if (kind == null && !camp.Used.Contains("love"))
            foreach (var l in Loves)
            {
                var a = P(l.A); var b = P(l.B);
                if (a == null || b == null || !here.Contains(a) || !here.Contains(b)) continue;
                if (l.Stage == LoveStage.Crush && a.AffinityTo(b) >= 0.5f && Talking(a.Id) == null)
                {
                    var k = Hold(CardKind.Confess, a, b, l.Id);
                    kind = "love"; text = $"{Ko.IGa(a.Name)} 모두가 보는 앞에서 {b.Name}에게 마음을 전했다 — {k.Outcome}";
                    foreach (var o in here) if (o != a && o != b) w.Brain2.Emotions.Feel(o, k.Success ? Feeling.Joy : Feeling.Sadness, 0.08f, "밤 모임의 고백", a);
                    break;
                }
                if (l.Together && l.Public && here.Where(o => o != a && o != b).OrderByDescending(o => o.Habits.Contains(Habit.Joker) ? 1 : 0).ThenBy(o => o.Id).FirstOrDefault() is CrewMember teaser) // 통합7 둘만 남은 밤엔 놀릴 사람이 없다 (빈 목록 First → 게임이 멈췄다)
                {
                    teaser.Say(w, Persona.Say(teaser, $"{Ko.WaGwa(a.Name)} {Ko.EunNeun(b.Name)} 오늘도 같이 앉았네~"));
                    Stats.Cheers++;
                    kind = "love"; text = $"{Ko.WaGwa(a.Name)} {Ko.IGa(b.Name)} 놀림을 받으며 웃었다";
                    break;
                }
            }
        // 4) 오늘의 큰일을 두고 저마다 다른 말
        if (kind == null && !camp.Used.Contains("talk") && RecentEvent() is string evRaw)
        {
            string ev = evRaw.TrimStart('†');
            var lines = new List<string>();
            for (int i = 0; i + 1 < here.Count && lines.Count < 3; i += 2)
            {
                var (la, lb, _) = Passing(here[i], here[i + 1], evRaw);
                here[i].Say(w, Persona.Say(here[i], la)); here[i + 1].Say(w, Persona.Say(here[i + 1], lb));
                lines.Add($"{here[i].Name}: “{la}”");
            }
            for (int i = 0; i < here.Count; i++) for (int j = i + 1; j < here.Count; j++) { here[i].ChangeAffinity(here[j], 0.01f); here[j].ChangeAffinity(here[i], 0.01f); }
            kind = "talk"; text = $"{ev} 이야기로 밤이 길어졌다 — {lines[0]}";
        }
        // 5) 고향 이야기
        if (kind == null && !camp.Used.Contains("home"))
        {
            var teller = here.OrderByDescending(c => c.Traits.Sociability).ThenBy(c => c.Id).FirstOrDefault();
            if (teller == null) return; // 통합7 아무도 없으면 이야기도 없다
            var r = RootsOf(teller);
            int same = 0;
            foreach (var o in here) if (o != teller && RootsOf(o).Home == r.Home) { o.ChangeAffinity(teller, 0.04f); teller.ChangeAffinity(o, 0.04f); same++; }
            teller.Say(w, Persona.Say(teller, $"{Homes[r.Home]}에선 이맘때 다들 지붕에 올라가 별을 봤어"));
            kind = "home"; text = $"{Ko.IGa(teller.Name)} {Homes[r.Home]} 이야기를 했다" + (same > 0 ? $" — 같은 고향 {same}명이 맞장구쳤다" : "");
        }
        if (kind == null || text == null) return;
        camp.Used.Add(kind);
        camp.Scenes.Add((w.Tick, kind, text));
        Stats.CampScenes++;
        w.Log.Add(w.Tick, LogKind.Life, $"밤 모임 — {text}", here[0].Id);
        foreach (var c in here) if (c.Id % 3 == camp.Scenes.Count % 3) Life.Diary(w, c, Persona.Say(c, $"밤 모임: {text}."));
    }
}
