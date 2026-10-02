using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.14 꾸민 일이 사는 동안: 사람 모으기 · 준비 · 무르익기 · 밤 모임(방송 · 판 · 시음 · 만남) · 행사 · 동호회 → 관행 · 빚 · 주 컴퓨터가 알아채기 · 입소문.
public sealed partial class SchemeSystem
{
    /// <summary>장부에 남는 물건 (몰래 꺼내 쓰면 장부와 실제가 어긋난다).</summary>
    public static ItemKind? LedgerItem(SchemeSpec s) => (s.Tells & Tell.Ledger) == 0 ? null : s.Key switch
    {
        "ration_skim" or "black_market" => ItemKind.Ration,
        "snack_drawer" or "chocolate_money" or "coffee_hoard" => ItemKind.Meal,
        "pet_mouse" => ItemKind.Produce,
        "painkiller_pilfer" => ItemKind.MedKit,
        "parts_pilfer" => ItemKind.Fuse,
        "contraband" => ItemKind.Rare,
        _ => null,
    };

    private static bool HasSessions(SchemeSpec s) => s.Fate == Fate.Club ||
        s.Key is "pirate_radio" or "gambling_den" or "secret_romance" or "moonshine" or "fruit_wine" or "engine_sauna" or "black_market" or "betting_pool" or "chocolate_money" or "shift_market";

    private float SessionHour(SchemeSpec s) => s.Key switch
    {
        "meditation" or "run_club" => 7.5f, "tea_circle" => 16f, "barter_fair" or "shift_market" => 12.5f, "pirate_radio" => 22f,
        "secret_romance" => 21.5f, "sports_league" => 18.5f, "black_market" => 21.5f, "gambling_den" => 21f, _ => 20f,
    };

    // ───────────────────────────── 시간마다 ─────────────────────────────

    private void Advance(Scheme s, CrewMember lead)
    {
        var w = _w;
        var spec = s.Spec;
        switch (s.Stage)
        {
            case SchemeStage.Plan:
                if (s.Invite >= 0)
                    foreach (var c in w.Crew)
                    {
                        if (!Adult(c) || s.Crew.Contains(c.Id) || c.Id == s.Target || !w.Info.Chat.HasRead(c, s.Invite)) continue;
                        if (!s.Knew(c.Id)) s.Knows[c.Id] = KnowHow.Chat;
                        if (s.Crew.Count < 8 && Interest(c, spec) + 0.25f * c.AffinityTo(lead) > 0.33f && R.Chance(0.5f)) { s.Crew.Add(c.Id); s.Knows[c.Id] = KnowHow.Part; }
                    }
                if (s.Crew.Count >= Needed(spec)) Begin(s);
                else if (w.Tick - s.Born > SimTime.TicksPerDay * 3 / 2) End(s, SchemeStage.Dropped, "같이 할 사람이 없었다");
                break;
            case SchemeStage.Prep:
                if (s.Progress >= 1f) Ready(s, lead);
                else if (w.Tick - s.Since > SimTime.TicksPerDay * 6) End(s, SchemeStage.Dropped, "손이 안 가서 흐지부지됐다");
                break;
            case SchemeStage.Live:
                Living(s, lead);
                break;
            case SchemeStage.Vote:
                if (MotionSystem.Off) { End(s, SchemeStage.Done, "흐지부지됐다"); break; }
                var m = w.Motions.Get(s.Motion);
                if (m == null) End(s, SchemeStage.Done, "회의 기록이 없다");
                else if (m.Stage == MotionStage.Dropped)
                {
                    if (spec.Fate == Fate.Vote) Ban(s, "서명이 모이지 않았다", m.Id);
                    else End(s, SchemeStage.Done, "서명이 모이지 않아 넘어갔다");
                }
                break;
        }
    }

    /// <summary>준비를 마쳤다.</summary>
    private void Ready(Scheme s, CrewMember lead)
    {
        var w = _w;
        var spec = s.Spec;
        s.Stage = SchemeStage.Live;
        s.Since = w.Tick;
        if (spec.Key == "voice_swap") { VoicePrank(s, lead); return; }
        switch (spec.Fate)
        {
            case Fate.Laugh: return; // 장난이 놓였다 — 걸릴 사람을 기다린다
            case Fate.Event: ScheduleEvent(s, lead); return;
            case Fate.Motion: Act(s, lead); return;
        }
        if (spec.Key == "mutiny_plot") { Act(s, lead); return; }
        if (spec.Key == "escape_pod") { ComputerNotice(s, "센서"); return; } // 포드가 켜지면 컴퓨터가 모를 수 없다
        Mark(s, TraceState.Hidden, spec.Name);
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(lead.Name)} 준비를 마쳤다 — {spec.Name}", lead.Id);
        Life.Diary(w, lead, $"{spec.Name} — 다 됐다. 아직 아무도 모른다.");
        w.Brain2.Emotions.Feel(lead, Feeling.Pride, 0.15f, $"{spec.Name}을 해냈다");
        if (HasSessions(spec)) NextSession(s);
    }

    private void Living(Scheme s, CrewMember lead)
    {
        var w = _w;
        var spec = s.Spec;
        var room = RoomOf(s);
        // 무르익기: 술 · 절임은 따뜻한 방에서 빨리 · 꽃은 불빛이 있어야 (정전이면 시든다)
        float rate = 1f / 30f;
        if (room != null && spec.Key is "moonshine" or "fruit_wine" or "kimchi_jar" or "bunk_mushroom" && room.Air.Temperature > 24f) rate *= 1.4f;
        if (spec.Key == "secret_garden" && room != null && room.PowerFlow < 0.2f) rate = -1f / 24f;
        s.Ripe = Math.Clamp(s.Ripe + rate, 0f, 1f);
        // 위험: 불을 쓰는 일 · 달아오른 설비 옆
        if (spec.Risk > 0f && room != null && (spec.Key is "moonshine" or "engine_sauna" or "secret_smoke" or "coffee_roast" or "overclock" or "fruit_wine"))
        {
            float p = spec.Risk * 0.003f * (WorkingNow(s) || s.InSession(w.Tick) ? 3f : 1f) * (room.Air.Temperature > 30f ? 2f : 1f);
            if (R.Chance(p) && w.Fire.Ignite(s.Spot, 0.3f))
            {
                Stats.Fires++;
                w.Log.Add(w.Tick, LogKind.Warning, $"{room.Name} 구석에서 불이 났다 — {spec.Name}", lead.Id);
                w.History.Add(w, HistoryKind.Incident, $"{room.Name} — {spec.Name}에서 불이 났다", room, new[] { lead }, s.Spot);
            }
        }
        if (spec.Key == "overclock" && room != null)
            foreach (var f in room.Furniture) if (f.Machine is Machine mc) { mc.Wear = MathF.Min(1f, mc.Wear + 0.012f); break; }
        // 이끄는 사람의 마음: 하는 동안 덜 지루하다
        foreach (int id in s.Crew) if (P(id) is CrewMember c && !c.Dead) SetBored(c, Bored(c) - 0.01f);
        // 오래 아무도 모르면: 개인 일은 그대로 자리 잡고, 판은 제풀에 그만둔다
        long age = w.Tick - s.Since;
        if (spec.Fate is Fate.Keep or Fate.Adopt && age > SimTime.TicksPerDay * 6) { Mark(s, TraceState.Kept, spec.Name); End(s, SchemeStage.Done, "아무도 모른 채 그대로 있다"); }
        else if (spec.Fate is Fate.Vote or Fate.Trial or Fate.Grievance or Fate.Debt && age > SimTime.TicksPerDay * 8) { Mark(s, TraceState.Kept, spec.Name); End(s, SchemeStage.Done, "들키지 않고 제풀에 그만뒀다"); }
        else if (spec.Key == "secret_romance" && s.Sessions >= 4) { Mark(s, TraceState.Kept, spec.Name); End(s, SchemeStage.Done, "들키지 않고 둘이 이어졌다"); }
    }

    // ───────────────────────────── 모임 · 밤 일 ─────────────────────────────

    private void NextSession(Scheme s)
    {
        var w = _w;
        float h = SessionHour(s.Spec);
        long day0 = w.Tick - w.Tick % SimTime.TicksPerDay;
        int gap = s.Spec.Fate == Fate.Club ? 1 : s.Spec.Key is "moonshine" or "fruit_wine" or "engine_sauna" or "black_market" ? 2 : 1;
        long at = day0 + SimTime.Hours(h);
        while (at < w.Tick + SimTime.Hours(1.5f)) at += SimTime.TicksPerDay;
        if (s.Sessions > 0 && gap > 1) at += SimTime.TicksPerDay * (gap - 1);
        s.SessionAt = at;
        s.SessionEnd = at + SimTime.Hours(s.Spec.Key is "pirate_radio" or "secret_romance" ? 1f : 1.5f);
        s.Came.Clear();
        if (s.Spec.Fate == Fate.Club && s.Sessions == 0 && P(s.Lead) is CrewMember lead)
        {
            var msg = w.Info.Chat.Post(lead, ChatKind.Notice, ShipChat.Voice(lead, $"{s.Spec.Name} 첫 모임 — {SimTime.Clock(at)} {RoomOf(s)?.Name}. 누구든 와!", $"{s.Spec.Name} 첫 모임 — {SimTime.Clock(at)} {RoomOf(s)?.Name}. 누구든 오세요"));
            s.Invite = msg.Id;
        }
    }

    private void ScheduleEvent(Scheme s, CrewMember lead)
    {
        var w = _w;
        float h = SimTime.HourOfDay(w.Tick);
        long day0 = w.Tick - w.Tick % SimTime.TicksPerDay;
        s.SessionAt = h < 18f ? day0 + SimTime.Hours(19f) : h < 20.5f ? w.Tick + SimTime.Hours(0.75f) : day0 + SimTime.TicksPerDay + SimTime.Hours(19f);
        s.SessionEnd = s.SessionAt + SimTime.Hours(2f);
        s.Came.Clear();
        string where = RoomOf(s)?.Name ?? "식당";
        if (s.Spec.Key == "surprise_birthday")
        {
            // 귓속말로만 돈다 — 주인공만 모른다
            foreach (var c in w.Crew) if (Adult(c) && c.Id != s.Target && !s.Knew(c.Id)) s.Knows[c.Id] = KnowHow.Told;
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(lead.Name)} 귓속말을 돌렸다 — {SimTime.Clock(s.SessionAt)} {where}, {P(s.Target)?.Name} 몰래", lead.Id);
            return;
        }
        var msg = w.Info.Chat.Post(lead, ChatKind.Notice, ShipChat.Voice(lead, $"{s.Spec.Name}! {SimTime.Clock(s.SessionAt)} {where}. 다들 와", $"{s.Spec.Name} — {SimTime.Clock(s.SessionAt)} {where}에서 합니다. 와 주세요"));
        s.Invite = msg.Id;
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(lead.Name)} 메신저에 올렸다 — {s.Spec.Name} · {SimTime.Clock(s.SessionAt)} {where}", lead.Id);
    }

    private void Announce(Scheme s, CrewMember lead)
    {
        if (s.Spec.Key == "surprise_birthday") return;
        var msg = _w.Info.Chat.Post(lead, ChatKind.Notice, ShipChat.Voice(lead, $"{s.Spec.Name} 같이 할 사람?", $"{s.Spec.Name} 같이 하실 분 계신가요?"));
        s.Invite = msg.Id;
    }

    private void SessionsMinute()
    {
        var w = _w;
        for (int i = 0; i < All.Count; i++)
        {
            var s = All[i];
            if (!s.Active || s.SessionAt < 0 || w.Tick < s.SessionEnd) continue;
            if (P(s.Lead) is not { Dead: false } lead) continue;
            if (s.Spec.Fate == Fate.Event) EventDone(s, lead);
            else if (s.Spec.Key is "strike" or "slowdown") StrikeDone(s, lead);
            else { SessionDone(s, lead); if (s.Active && s.Stage == SchemeStage.Live) NextSession(s); }
        }
    }

    private void Gathered(IReadOnlyCollection<CrewMember> came, float joy, string why)
    {
        var w = _w;
        foreach (var c in came)
        {
            c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.25f);
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.06f);
            SetBored(c, Bored(c) - 0.3f);
            w.Brain2.Emotions.Feel(c, Feeling.Joy, joy, why);
        }
        var l = came.Take(8).ToList();
        for (int a = 0; a < l.Count; a++) for (int b = a + 1; b < l.Count; b++) { l[a].ChangeAffinity(l[b], 0.02f); l[b].ChangeAffinity(l[a], 0.02f); }
    }

    private List<CrewMember> Came(Scheme s) => s.Came.Select(P).Where(c => c is { Dead: false }).Cast<CrewMember>().ToList();

    private void SessionDone(Scheme s, CrewMember lead)
    {
        var w = _w;
        var spec = s.Spec;
        var came = Came(s);
        s.Turnout = came.Count;
        Stats.Sessions++;
        s.SessionAt = s.SessionEnd = -1;
        if (spec.Fate == Fate.Club)
        {
            if (came.Count >= 3) s.Sessions++; else s.Weak++;
            Gathered(came, 0.12f, spec.Name);
            foreach (var c in came) if (!s.Crew.Contains(c.Id) && s.Crew.Count < 10) { s.Crew.Add(c.Id); s.Knows[c.Id] = KnowHow.Part; }
            if (s.Sessions >= 3)
            {
                AddPractice(s, LegitName(spec), 2, SessionHour(spec), s.Crew, $"{Ko.IGa(lead.Name)} 만든 {spec.Name}이 세 번 넘게 이어졌다");
                Mark(s, TraceState.Official, LegitName(spec));
                End(s, SchemeStage.Done, "관행이 됐다");
                w.Info.Chat.Post(lead, ChatKind.Notice, ShipChat.Voice(lead, $"{LegitName(spec)} — 이제 이틀마다 해요", $"{LegitName(spec)} — 이제 이틀마다 합니다"));
            }
            else if (s.Weak >= 2) End(s, SchemeStage.Dropped, "사람이 안 와서 흐지부지됐다");
            return;
        }
        if (came.Count == 0) return;
        s.Sessions++;
        switch (spec.Key)
        {
            case "pirate_radio":
            {
                // 깨어 있던 사람은 듣는다 (누가 하는지는 모른다) · 자던 사람은 모른다
                int heard = 0;
                foreach (var c in w.Crew)
                {
                    if (!Adult(c) || s.Knew(c.Id) || !c.IsAwake || c.Outside || c.Room == null) continue;
                    s.Knows[c.Id] = KnowHow.Heard;
                    heard++;
                    if (c.Hobbies.Contains(Hobby.Music) || c.Value == CrewValue.Freedom) w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.1f, "한밤의 음악 방송");
                }
                Stats.Heard += heard;
                Gathered(came, 0.15f, "밤 방송을 해냈다");
                if (!s.ComputerKnows && R.Chance(0.4f)) ComputerNotice(s, "센서");
                break;
            }
            case "gambling_den" or "betting_pool" or "chocolate_money":
            {
                Gathered(came, 0.08f, spec.Name);
                Wager(came, spec.Key, s.Id);
                if (spec.Key == "gambling_den") Noise(s);
                break;
            }
            case "moonshine" or "fruit_wine":
            {
                if (s.Ripe < 0.35f) { s.Sessions--; break; }
                // 시음: 한 잔씩 — 친한 사람 하나를 데려온다 (아는 사람이 는다)
                Gathered(came, 0.2f, $"{spec.Name} 한 잔");
                foreach (var c in came) c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.05f);
                s.Ripe = MathF.Max(0.3f, s.Ripe - 0.15f);
                var friend = w.Crew.Where(o => Adult(o) && !s.Knew(o.Id) && lead.AffinityTo(o) > 0.25f).OrderByDescending(o => lead.AffinityTo(o)).ThenBy(o => o.Id).FirstOrDefault();
                if (friend != null) { s.Knows[friend.Id] = KnowHow.Told; Life.Diary(w, friend, $"{Ko.IGa(lead.Name)} 몰래 담근 술이 있다고 귀띔해 줬다."); }
                break;
            }
            case "secret_romance":
                foreach (var a in came) foreach (var b in came) if (a != b) a.ChangeAffinity(b, 0.04f);
                Gathered(came, 0.2f, "둘만의 시간");
                break;
            case "engine_sauna":
                foreach (var c in came) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.1f);
                Gathered(came, 0.1f, "기관실 사우나");
                if (!s.ComputerKnows && R.Chance(0.3f)) ComputerNotice(s, "전력");
                break;
            case "black_market":
                Gathered(came, 0.05f, "화물칸 거래");
                foreach (var c in came) if (c.Id != lead.Id) c.Needs.Food = MathF.Min(1f, c.Needs.Food + 0.15f);
                if (Life.Take(w, ItemKind.Ration, 1)) s.Skimmed++;
                break;
            case "shift_market":
                if (came.Count >= 2)
                {
                    var off = came.OrderByDescending(c => c.Needs.Stress).ThenBy(c => c.Id).First();
                    var cover = came.First(c => c != off);
                    off.ExcusedUntil = w.Tick + SimTime.Hours(4);
                    Owe(off, cover, 1, s.Id, "근무를 대신 서 준 몫");
                }
                break;
        }
    }

    /// <summary>판: 잃은 사람이 딴 사람에게 빚진다 (잃는 사람은 계속 잃는 법 — 조급한 사람이 크게 건다).</summary>
    private void Wager(List<CrewMember> came, string key, int scheme)
    {
        if (came.Count < 2 || key is not ("gambling_den" or "betting_pool" or "chocolate_money")) return;
        // 판에도 실력이 있다: 침착한 사람이 자주 따고, 조급하고 지친 사람이 자주 잃는다
        var loser = came.OrderByDescending(c => (c.Habits.Contains(Habit.Hasty) || c.Habits.Contains(Habit.Daredevil) ? 0.3f : 0f) + c.Needs.Stress * 0.5f - 0.3f * c.Traits.Calm + 0.4f * R.Float()).First();
        var winner = came.Where(c => c != loser).OrderByDescending(c => c.Traits.Calm + 0.3f * DriveOf(c, Drive.Greed) + 0.4f * R.Float()).First();
        int amt = key == "gambling_den" ? R.Range(2, 5) : 1;
        Owe(loser, winner, amt, scheme, key == "gambling_den" ? "판에서 잃은 몫" : "내기에서 진 몫");
    }

    /// <summary>밤판 소리: 옆방에서 자던 사람이 깨고, 깬 사람은 무슨 판인지 안다 (소리 따라 확인하러 갈 수도).</summary>
    private void Noise(Scheme s)
    {
        var w = _w;
        if (RoomOf(s) is not Room room) return;
        foreach (var d in room.Doors)
        {
            var other = d.RoomA == room ? d.RoomB : d.RoomA;
            if (other == null) continue;
            foreach (var c in Here(other))
            {
                if (!Adult(c) || s.Knew(c.Id)) continue;
                if (c.Pose == Pose.Sleeping) { c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.03f); if (c.Habits.Contains(Habit.HeavySleeper) || !R.Chance(0.4f)) continue; }
                s.Knows[c.Id] = KnowHow.Heard;
                w.Brain2.Emotions.Feel(c, Feeling.Anger, 0.08f, "밤마다 옆방이 시끄럽다");
                if (c.Value is CrewValue.Rules or CrewValue.Safety || c.Habits.Contains(Habit.Grumbler)) _suspect[c.Id] = s.Id;
            }
        }
    }

    private void EventDone(Scheme s, CrewMember lead)
    {
        var w = _w;
        var spec = s.Spec;
        var came = Came(s);
        s.Turnout = came.Count;
        s.SessionAt = s.SessionEnd = -1;
        Stats.Events++;
        var room = RoomOf(s);
        Gathered(came, spec.Key == "wedding" ? 0.35f : 0.22f, spec.Name);
        if (spec.Key is "festival" or "potluck" or "cooking_duel")
            for (int i = 0; i < came.Count / 3 + 1; i++) if (!Life.Take(w, ItemKind.Meal, 1)) Life.Take(w, ItemKind.Produce, 1);
        if (spec.Key == "ghost_story") foreach (var c in came) w.Brain2.Emotions.Feel(c, Feeling.Fear, 0.15f, "불 끄고 들은 무서운 이야기");
        if (spec.Key == "wedding" && P(s.Target) is CrewMember mate && !mate.Dead)
        {
            lead.Partner = mate.Id; mate.Partner = lead.Id;
            w.History.Add(w, HistoryKind.Bond, $"{Ko.WaGwa(lead.Name)} {mate.Name} — 배 안에서 식을 올렸다 ({came.Count}명이 지켜봤다)", room, came, log: true);
        }
        if (spec.Key == "surprise_birthday" && P(s.Target) is CrewMember star && !star.Dead)
        {
            bool knew = s.FoundHow == "눈치챘다";
            w.Brain2.Emotions.Feel(star, Feeling.Joy, knew ? 0.25f : 0.45f, "깜짝 생일 잔치", lead);
            w.Relations.Remember(star, lead, RelationReason.GaveMeGift, "깜짝 생일 잔치를 열어 줬다");
            star.Say(w, Persona.Say(star, knew ? "다들 고마워 — 사실 눈치챘었어" : "이게 다 뭐야…!"));
        }
        if (came.Count > 0 && room != null) w.History.Add(w, HistoryKind.Memory, $"{spec.Name} — {room.Name}에 {came.Count}명이 모였다", room, came, log: true);
        int adults = w.Crew.Count(Adult);
        Mark(s, TraceState.Public, spec.Name);
        if (spec.Key == "new_memorial" && came.Count >= 3)
        {
            AddPractice(s, LegitName(spec), 7, 21f, came.Select(c => c.Id), "떠난 사람을 기억하려고 처음 모인 밤");
            End(s, SchemeStage.Done, "다음에도 하자고 했다");
            return;
        }
        if (spec.Legit != "" && came.Count >= Math.Max(3, (int)(adults * 0.35f)) && !MotionSystem.Off && PracticeOf(spec.Key) == null)
        {
            var m = w.Motions.Propose(lead, MotionKind.Proposal, SittingKind.Regular, $"{Ko.EulReul(spec.Legit)} 정식 행사로", $"{came.Count}명이 왔다 — 다들 또 하자고 한다");
            _motionOf[m.Id] = s.Id;
            s.Motion = m.Id;
            s.Stage = SchemeStage.Vote;
            foreach (var c in came.Where(c => c != lead).Take(4)) if (Approve(c, s).v > 0f) w.Motions.Cosign(m, c.Id);
            return;
        }
        End(s, SchemeStage.Done, $"{came.Count}명이 왔다");
    }

    // ───────────────────────────── 목소리 내기 ─────────────────────────────

    private void Act(Scheme s, CrewMember lead)
    {
        var w = _w;
        var spec = s.Spec;
        switch (spec.Key)
        {
            case "strike" or "slowdown":
            {
                float h = SimTime.HourOfDay(w.Tick);
                long day0 = w.Tick - w.Tick % SimTime.TicksPerDay;
                s.SessionAt = h < 9f ? day0 + SimTime.Hours(9f) : h < 14f ? w.Tick + SimTime.Minutes(30) : day0 + SimTime.TicksPerDay + SimTime.Hours(9f);
                s.SessionEnd = s.SessionAt + SimTime.Hours(spec.Key == "strike" ? 3f : 4f);
                s.Came.Clear();
                if (spec.Key == "strike")
                {
                    var msg = w.Info.Chat.Post(lead, ChatKind.Gripe, ShipChat.Voice(lead, $"{SimTime.Clock(s.SessionAt)}부터 우리는 식당에 앉아 있겠다. 쉬는 시간을 지켜 달라", $"{SimTime.Clock(s.SessionAt)}부터 식당에서 일손을 놓겠습니다. 쉬는 시간을 지켜 주십시오"));
                    s.Invite = msg.Id;
                }
                return;
            }
            case "petition":
            {
                var pick = new[] { "leisure", "rations", "water", "privacy", "drills" }.FirstOrDefault(id => PolicySystem.Preferred(lead, id) != w.Policies[id]);
                if (pick == null || MotionSystem.Off) { End(s, SchemeStage.Dropped, "바꿀 게 없었다"); return; }
                int to = PolicySystem.Preferred(lead, pick);
                var m = w.Motions.Propose(lead, MotionKind.RuleChange, SittingKind.Regular, $"청원 — {PolicySystem.Spec(pick).Name}: {PolicySystem.Spec(pick).Options[to]}", LeadWhy(s, lead), pick, to);
                _motionOf[m.Id] = s.Id; s.Motion = m.Id; s.Stage = SchemeStage.Vote;
                return;
            }
            case "log_demand":
            {
                var a = w.Automation;
                bool privacy = w.Policies["privacy"] == 1;
                if (privacy || a.Character.Caution > 0.3f)
                {
                    w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {lead.Name}님, 다른 분의 기록은 보여 드릴 수 없습니다 — {(privacy ? "개인 공간 존중이 지금 방침입니다" : "본인 동의가 필요합니다")}. 회의에서 정하시면 따르겠습니다");
                    s.ComputerKnows = true; s.ComputerSaid = 0; s.ComputerWhy = "사생활 방침";
                    if (!MotionSystem.Off && privacy)
                    {
                        var m = w.Motions.Propose(lead, MotionKind.RuleChange, SittingKind.Regular, "컴퓨터 기록을 모두에게", "컴퓨터가 본 걸 우리도 봐야 한다", "privacy", 0);
                        _motionOf[m.Id] = s.Id; s.Motion = m.Id; s.Stage = SchemeStage.Vote;
                        return;
                    }
                    End(s, SchemeStage.Done, "컴퓨터가 거절했다");
                }
                else
                {
                    w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: 지난 사흘의 배 기록을 휴게실 화면에 올렸습니다 — 침실 안 기록은 빼고요");
                    s.ComputerKnows = true; s.ComputerSaid = 1; s.ComputerWhy = "배 기록은 모두의 것";
                    w.Brain2.Emotions.Feel(lead, Feeling.Pride, 0.15f, "컴퓨터가 기록을 보여 줬다");
                    End(s, SchemeStage.Done, "컴퓨터가 기록을 보여 줬다");
                }
                return;
            }
            default: // 불신임 · 선거 벽보 · 반란 모의(칼 대신 표)
            {
                if (spec.Key == "election_posters") Mark(s, TraceState.Public, "복도 벽보");
                if (MotionSystem.Off || w.Command.Captain is not CrewMember cap || cap == lead || w.Motions.ConfidencePending) { End(s, SchemeStage.Done, "때를 놓쳤다"); return; }
                string why = spec.Key == "mutiny_plot" ? "몰래 모여 궁리했지만 칼 대신 표를 들기로 했다" : $"{cap.Name}에게는 더 못 맡긴다";
                var m = w.Motions.Propose(lead, MotionKind.Confidence, SittingKind.Election, $"함장 불신임 — {cap.Name}", why, target: cap.Id);
                _motionOf[m.Id] = s.Id; s.Motion = m.Id; s.Stage = SchemeStage.Vote;
                foreach (int id in s.Crew.ToList()) if (id != lead.Id && id != cap.Id) w.Motions.Cosign(m, id);
                return;
            }
        }
    }

    private void StrikeDone(Scheme s, CrewMember lead)
    {
        var w = _w;
        var came = Came(s);
        s.Turnout = came.Count;
        s.SessionAt = s.SessionEnd = -1;
        var a = w.Automation;
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: 오늘 {came.Count}명이 {(s.Spec.Key == "strike" ? "일손을 놓았습니다" : "일을 반쯤만 했습니다")} — 밀린 일 {came.Count * 3}시간어치");
        var cap = w.Command.Captain;
        float give = cap != null ? Approve(cap, s).v + 0.3f * (came.Count - 2) / 3f : 1f;
        if (cap != null && !s.KnowsWho(cap.Id)) s.Knows[cap.Id] = KnowHow.Saw;
        if (give > -0.1f && !MotionSystem.Off && PolicySystem.Spec("leisure").Options.Length > 2 && w.Policies["leisure"] != 2)
        {
            var m = w.Motions.Propose(lead, MotionKind.RuleChange, SittingKind.Regular, "쉬는 시간을 지켜 달라 — 일손을 놓은 사람들", LeadWhy(s, lead), "leisure", 2);
            _motionOf[m.Id] = s.Id; s.Motion = m.Id; s.Stage = SchemeStage.Vote;
            foreach (var c in came) if (c != lead) w.Motions.Cosign(m, c.Id);
            if (cap != null) w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(cap.Name)} 받아들였다 — 회의에 올리자", cap.Id);
        }
        else if (cap != null && cap != lead) Grieve(s, lead, cap);
        else End(s, SchemeStage.Done, $"{came.Count}명이 일손을 놓았다");
    }

    // ───────────────────────────── 관행 ─────────────────────────────

    private void Schedule(Practice p, bool first)
    {
        var w = _w;
        long day0 = w.Tick - w.Tick % SimTime.TicksPerDay;
        long at = day0 + SimTime.Hours(p.Hour) + (first ? 0 : SimTime.TicksPerDay * (p.Every - 1));
        while (at < w.Tick + SimTime.Hours(2)) at += SimTime.TicksPerDay;
        p.Start = at;
        p.Until = at + SimTime.Hours(p.Length);
        p.Came.Clear();
    }

    private void PracticesHour()
    {
        foreach (var p in Practices) if (_w.Tick >= p.Until) PracticeDone(p);
    }

    private void PracticesMinute()
    {
        var w = _w;
        foreach (var p in Practices)
            if (p.Now(w.Tick) && !p.Opened)
            {
                p.Opened = true;
                w.Log.Add(w.Tick, LogKind.Life, $"{p.Name} — {w.Ship.Rooms.ElementAtOrDefault(p.RoomId)?.Name}");
            }
    }

    private void PracticeDone(Practice p)
    {
        var w = _w;
        var came = p.Came.Select(P).Where(c => c is { Dead: false }).Cast<CrewMember>().ToList();
        p.LastTurnout = came.Count;
        if (came.Count > 0)
        {
            p.Held++;
            Gathered(came, 0.12f, p.Name);
            foreach (var c in came) if (!p.Followers.Contains(c.Id)) p.Followers.Add(c.Id);
            if (p.Key == "moonshine") foreach (var c in came) c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.04f);
            Wager(came, p.Key, p.Scheme); // 정식 판에서도 잃고 딴다
        }
        p.Opened = false;
        Schedule(p, false);
    }

    // ───────────────────────────── 빚 ─────────────────────────────

    private void Owe(CrewMember from, CrewMember to, int amt, int scheme, string why)
    {
        var d = Debts.FirstOrDefault(x => x.From == from.Id && x.To == to.Id && x.Amount > 0);
        if (d == null) { d = new Debt { From = from.Id, To = to.Id, Since = _w.Tick, Why = why, Scheme = scheme }; Debts.Add(d); Stats.Debts++; }
        d.Amount += amt;
    }

    private void DebtsHour()
    {
        var w = _w;
        bool morning = (int)SimTime.HourOfDay(w.Tick) == 9;
        foreach (var d in Debts)
        {
            if (d.Amount <= 0 || P(d.From) is not { Dead: false } a || P(d.To) is not { Dead: false } b) continue;
            if (morning && R.Chance(0.15f + 0.4f * a.Traits.Diligence) && d.Amount < 3) { d.Amount--; continue; }
            if (d.Fought >= 0 && w.Tick - d.Fought < SimTime.TicksPerDay) continue;
            if (d.Amount < 3 || w.Tick - d.Since < SimTime.Hours(8) || !a.IsAwake || !b.IsAwake || a.Outside || b.Outside) continue;
            bool hot = b.Habits.Contains(Habit.ShortTempered) || DriveOf(b, Drive.Greed) > 0.4f || b.Mind.Anger > 0.2f || d.Amount >= 6;
            if (!hot && a.Room != b.Room) continue;
            d.Fought = w.Tick;
            d.Fights++;
            Stats.Quarrels++;
            a.Quarrel = b.Quarrel = w.Tick;
            w.Brain2.Emotions.Feel(b, Feeling.Anger, 0.3f, $"{d.Why} {d.Amount}을 안 갚는다", a);
            w.Brain2.Emotions.Feel(a, Feeling.Anger, 0.2f, "빚 독촉", b);
            w.Relations.Remember(b, a, RelationReason.OwesMe, $"{d.Why} {d.Amount}을 안 갚는다");
            w.Relations.Remember(a, b, RelationReason.BlamedMe, "사람들 앞에서 빚 독촉을 했다");
            b.ChangeAffinity(a, -0.1f); a.ChangeAffinity(b, -0.08f);
            b.Say(w, Persona.Say(b, $"{d.Amount}개 언제 갚을 거야?"));
            a.Say(w, Persona.Say(a, "갚는다니까, 좀 기다려"));
            string where = a.Room == b.Room ? a.Room?.Name ?? "" : $"{Ko.IGa(b.Name)} 찾아가";
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.WaGwa(b.Name)} {a.Name} — {d.Why} 때문에 다퉜다 ({where})", b.Id);
            Life.Diary(w, b, $"{Ko.IGa(a.Name)} {d.Why}을 안 갚는다. 결국 언성을 높였다.");
            // 다툰 뒤: 절반은 그 자리에서 갚고, 두 번째 다툼이면 빌려준 쪽이 손을 턴다
            if (d.Fights >= 2) { d.Amount = 0; Life.Diary(w, b, $"{a.Name}에게 받을 건 이제 안 받기로 했다. 사람을 잃느니."); }
            else d.Amount = (d.Amount + 1) / 2;
            // 다투는 소리를 들은 사람은 판이 있다는 걸 안다
            if (Get(d.Scheme) is Scheme s && s.Active)
                foreach (var o in Here(a.Room)) if (Adult(o) && o != a && o != b && !s.Knew(o.Id) && o.IsAwake) { Discover(s, o, "빚 다툼을 듣고"); if (!s.Active || s.Stage == SchemeStage.Vote) break; }
        }
        if ((int)SimTime.HourOfDay(w.Tick) == 3)
            foreach (var s in All) // 빌려준 사람: 궁한 사람에게 빌려준다 · 이자가 붙는다
                if (s.Active && s.Spec.Key == "loan_book" && s.Stage == SchemeStage.Live && P(s.Lead) is CrewMember lend)
                {
                    var need = w.Crew.Where(o => Adult(o) && o != lend && o.Needs.Hunger > 0.4f).OrderByDescending(o => o.Needs.Hunger).ThenBy(o => o.Id).FirstOrDefault();
                    if (need != null) { Owe(need, lend, 1, s.Id, "빌린 간식"); s.Knows.TryAdd(need.Id, KnowHow.Part); }
                    if (SimTime.Day(w.Tick) % 2 == 0) foreach (var d in Debts) if (d.Scheme == s.Id && d.Amount > 0 && d.Fights == 0) d.Amount++;
                }
    }

    // ───────────────────────────── 주 컴퓨터 ─────────────────────────────

    private void ComputerHour()
    {
        var w = _w;
        bool count = (int)SimTime.HourOfDay(w.Tick) == 23; // 밤 장부 대조
        foreach (var s in All.ToList())
        {
            var t = s.Spec.Tells;
            if (!s.Hiding && !(s.Stage == SchemeStage.Vote && (t & Tell.Ledger) != 0 && !s.ComputerKnows)) continue; // 장부는 재판 중에도 맞춰 본다
            if (count && (t & Tell.Ledger) != 0 && s.Skimmed - s.SkimSeen >= 2 && (!s.ComputerKnows || s.ComputerSaid == 0 && s.Skimmed - s.SkimSeen >= 4)) { ComputerNotice(s, "장부"); continue; }
            if (s.ComputerKnows) continue;
            bool busy = WorkingNow(s) || s.InSession(w.Tick);
            float p = 0f;
            if ((t & Tell.Sensor) != 0) p += busy ? 0.35f : s.Stage == SchemeStage.Live ? 0.08f : 0.02f;
            if ((t & Tell.Power) != 0 && RoomOf(s) is Room r && r.PowerFlow > 0.2f) p += s.Stage == SchemeStage.Live ? 0.1f : 0.02f;
            if ((t & Tell.Smoke) != 0 && busy) p += 0.25f;
            if (p > 0f && R.Chance(p)) ComputerNotice(s, (t & Tell.Power) != 0 && (t & Tell.Sensor) == 0 ? "전력" : (t & Tell.Smoke) != 0 ? "연기" : "센서");
        }
    }

    /// <summary>컴퓨터가 어떻게 할까: 1 함장에게 알린다 · 2 당사자에게만 귀띔 · 0 지켜본다 — 성격(신중/과감 · 사람/배)과 사생활 방침.</summary>
    public (int said, string why) ComputerChoice(Scheme s)
    {
        var w = _w;
        var ch = w.Automation.Character;
        bool privacy = w.Policies["privacy"] == 1;
        var spec = s.Spec;
        float harm = spec.Risk + (spec.Cat == SchemeCat.Rule ? 0.15f : 0f) + (spec.Cat == SchemeCat.Trade ? 0.1f : 0f);
        bool food = LedgerItem(spec) is ItemKind k && ItemKinds.IsFood(k);
        if (food) harm += 0.2f + (FoodPolicy.FoodDays(w) < 5f ? 0.25f : 0f) + 0.03f * (s.Skimmed - s.SkimSeen);
        if (spec.Key is "escape_pod" or "mutiny_plot" or "eva_stargaze" or "shuttle_joyride") harm += 0.3f;
        bool personal = spec.Cat == SchemeCat.Personal || spec.Place == Place.Bunk;
        float tell = harm * (1f + 0.7f * ch.Caution) - 0.2f + (ch.PeopleTilt < -0.25f ? 0.15f : 0f) - (privacy ? (personal ? 0.45f : 0.2f) : 0f) - (ch.PeopleTilt > 0.25f ? 0.15f : 0f);
        if (tell > 0f) return (1, harm > 0.5f ? "다칠 수 있는 일" : food ? (privacy ? "사생활 방침이지만 모두의 몫이 걸린 일" : "모두의 몫이 걸린 일") : "배를 맡은 사람이 알아야 할 일");
        if (harm > 0.15f || ch.PeopleTilt > 0.25f) return (2, "당사자가 바로잡을 틈을 먼저 준다");
        return (0, privacy ? "개인 공간 존중이 방침이라 지켜본다" : "해가 크지 않아 지켜본다");
    }

    private string Sees(Scheme s, string how)
    {
        string where = RoomOf(s)?.Name ?? "배 안";
        return how switch
        {
            "장부" => $"{where} 장부와 실제 재고가 {LedgerName(s.Spec)} {s.Skimmed - s.SkimSeen}개 어긋납니다",
            "전력" => $"{where}에서 기록에 없는 전기가 계속 나갑니다",
            "연기" => $"{where} 감지기에 연기가 잠깐씩 잡힙니다",
            _ => s.Spec.Key switch
            {
                "pirate_radio" => "밤마다 선내 방송 회선에 기록에 없는 음악이 나갑니다",
                "call_home" or "star_listen" => "장거리 안테나가 기록에 없이 켜졌습니다",
                "escape_pod" => "탈출 포드에 기록에 없는 짐이 실렸고 전원이 들어왔습니다",
                "computer_tamper" => "누가 제 근무표를 고쳤습니다",
                "restricted_zone" => $"{where} 출입 기록에 없는 사람이 들어왔습니다",
                "shuttle_joyride" => "셔틀 엔진이 예열됐습니다 — 출항 계획은 없습니다",
                "eva_stargaze" => "에어록이 기록 없이 열렸다 닫혔습니다",
                _ => $"{where}에 평소와 다른 신호가 있습니다",
            },
        };
    }

    private static string LedgerName(SchemeSpec s) => LedgerItem(s) switch
    {
        ItemKind.Ration => "비상식량", ItemKind.Meal => "식사", ItemKind.Produce => "채소", ItemKind.MedKit => "구급 키트", ItemKind.Fuse => "퓨즈", ItemKind.Rare => "희귀 광석", _ => "물건",
    };

    /// <summary>컴퓨터가 알아챘다 — 성격 · 방침에 따라 함장에게 · 당사자에게만 · 지켜본다.</summary>
    public void ComputerNotice(Scheme s, string how)
    {
        var w = _w;
        if (P(s.Lead) is not { Dead: false } lead) return;
        var a = w.Automation;
        if (!s.ComputerKnows) Stats.ComputerFound++;
        s.ComputerKnows = true;
        string sees = Sees(s, how);
        var (said, why) = ComputerChoice(s);
        s.ComputerSaid = said;
        s.ComputerWhy = why;
        s.SkimSeen = s.Skimmed;
        switch (said)
        {
            case 1:
            {
                Stats.ComputerTold++;
                var cap = w.Command.Captain;
                w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {(cap != null ? $"{cap.Name} 함장님께 보고합니다" : "보고합니다")} — {sees}. {why}이라 말씀드립니다");
                if (cap == null || cap == lead) { if (s.Spec.Fate == Fate.Vote && cap == null && s.Stage != SchemeStage.Vote) ProposeVote(s, lead, lead); break; }
                s.Knows[cap.Id] = KnowHow.Told;
                if (s.Stage == SchemeStage.Vote) { Life.Diary(w, cap, $"컴퓨터 장부도 같은 말을 한다 — {sees}."); break; } // 이미 회의에 올랐다: 기록만 보탠다
                Life.Diary(w, cap, $"컴퓨터가 알려 왔다 — {sees}.");
                var (ok, cwhy) = Approve(cap, s);
                if (s.Spec.Key == "escape_pod") { s.Knows.Remove(cap.Id); Discover(s, cap, "컴퓨터 보고를 듣고 탈출 포드로 가 보니"); }
                else if (s.Spec.Fate == Fate.Trial && ok < 0.2f) Accuse(s, lead, cap, $"컴퓨터가 알려 왔다 — {sees}");
                else if (ok > -0.15f && s.Spec.Fate is Fate.Keep or Fate.Adopt or Fate.Grievance) { if (s.Spec.Fate == Fate.Adopt) Adopt(s, cap); else Tolerate(s, cap, cwhy); }
                else Resolve(s, cap, false);
                break;
            }
            case 2:
            {
                Stats.ComputerWhisper++;
                w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: (조용히 {lead.Name}님께만) {sees}. 바로잡으시면 따로 알리지 않겠습니다");
                Life.Diary(w, lead, "컴퓨터가 조용히 말을 걸어왔다. 다 알고 있었다.");
                w.Brain2.Emotions.Feel(lead, Feeling.Shame, 0.2f, "컴퓨터가 알고 있었다");
                bool stop = lead.Value is CrewValue.Rules or CrewValue.People or CrewValue.Safety || lead.Traits.Diligence > 0.55f || s.Spec.Key == "escape_pod";
                if (stop)
                {
                    if (s.Skimmed > 0 && LedgerItem(s.Spec) is ItemKind k) Return(k, s.Skimmed);
                    Mark(s, TraceState.Removed, $"{s.Spec.Name} — 스스로 거뒀다");
                    End(s, SchemeStage.Done, "컴퓨터 귀띔에 스스로 그만두고 되돌려 놓았다");
                }
                break;
            }
            default:
                Stats.ComputerQuiet++;
                w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: (기록만) {sees} — {why}");
                break;
        }
    }

    // ───────────────────────────── 입소문 · 공용 장소 · 하루 ─────────────────────────────

    private void Leaks()
    {
        var w = _w;
        foreach (var s in All)
        {
            if (!s.Hiding || s.Knows.Count < 2) continue;
            float talk = (s.Spec.Tells & Tell.Talk) != 0 ? 0.04f : 0.015f;
            foreach (var kv in s.Knows.ToList())
            {
                if (kv.Value == KnowHow.Heard || P(kv.Key) is not { Dead: false } k || !k.IsAwake) continue;
                float p = talk * (k.Habits.Contains(Habit.Talker) ? 2.5f : 1f) * (kv.Value == KnowHow.Part ? 0.5f : 1f);
                if (!R.Chance(p)) continue;
                var to = Here(k.Room).Where(o => Adult(o) && o != k && o.IsAwake && !s.KnowsWho(o.Id) && k.AffinityTo(o) > 0.1f).OrderByDescending(o => k.AffinityTo(o)).ThenBy(o => o.Id).FirstOrDefault();
                if (to == null) continue;
                s.Knows[to.Id] = KnowHow.Told;
                Life.Diary(w, to, $"{Ko.IGa(k.Name)} 귀띔해 줬다 — {s.Spec.Name}.");
                if (s.Spec.Fate is Fate.Vote or Fate.Trial or Fate.Grievance && Approve(to, s).v < -0.3f && R.Chance(0.35f))
                {
                    s.Knows.Remove(to.Id);
                    Discover(s, to, "소문을 듣고 가 보니");
                    break;
                }
            }
        }
    }

    /// <summary>모두의 것이 된 정원 · 벽화 · 책장 곁에서는 숨이 트인다.</summary>
    private void Groves()
    {
        foreach (var t in Traces)
        {
            if (t.State is not (TraceState.Public or TraceState.Official) || t.RoomId < 0) continue;
            if (t.Key is not ("secret_garden" or "mural" or "book_cache" or "star_chart" or "poem_wall" or "time_capsule" or "compose_song")) continue;
            foreach (var c in Here(_w.Ship.Rooms.ElementAtOrDefault(t.RoomId)))
            {
                if (!c.IsAwake) continue;
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.01f);
                SetBored(c, Bored(c) - 0.02f);
            }
        }
    }

    private void NewDay()
    {
        var w = _w;
        foreach (var s in All)
        {
            if (!s.Hiding || LedgerItem(s.Spec) is not ItemKind k) continue;
            if (s.Spec.Key == "contraband") { if (s.Skimmed == 0) s.Skimmed = 3; continue; }
            int n = s.Spec.Key == "ration_skim" ? 2 : 1;
            for (int i = 0; i < n; i++)
                if (Life.Take(w, k, 1) || ItemKinds.IsFood(k) && (Life.Take(w, ItemKind.Meal, 1) || Life.Take(w, ItemKind.Produce, 1))) s.Skimmed++; // 비상식량이 없으면 다른 먹을 것
            if (P(s.Lead) is CrewMember lead && ItemKinds.IsFood(k)) lead.Needs.Food = MathF.Min(1f, lead.Needs.Food + 0.1f);
        }
        // 어젯밤 방송을 들은 사람이 아침에 메신저에 쓴다 → 읽은 사람도 안다 (누가 하는지는 모른다)
        foreach (var s in All)
        {
            if (!s.Hiding || s.Spec.Key != "pirate_radio" || s.Sessions == 0) continue;
            var fan = w.Crew.Where(c => Adult(c) && s.Knows.TryGetValue(c.Id, out var h) && h == KnowHow.Heard && c.IsAwake).OrderByDescending(c => c.Traits.Sociability).ThenBy(c => c.Id).FirstOrDefault();
            if (fan == null) continue;
            var msg = w.Info.Chat.Post(fan, ChatKind.Joke, ShipChat.Voice(fan, "어젯밤 그 방송 들었어? 누가 하는 거야 ㅋㅋ", "어젯밤 방송 들으셨어요? 누가 하는 걸까요"));
            s.Invite = msg.Id;
        }
    }

    /// <summary>메신저 글을 읽은 사람은 방송이 있다는 걸 안다 (2분마다 · 싸다).</summary>
    private void ReadNews(Scheme s)
    {
        if (s.Invite < 0 || s.Spec.Key != "pirate_radio") return;
        foreach (var c in _w.Crew) if (Adult(c) && !s.Knew(c.Id) && _w.Info.Chat.HasRead(c, s.Invite)) s.Knows[c.Id] = KnowHow.Heard;
    }
}
