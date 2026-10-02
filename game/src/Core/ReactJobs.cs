using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.8 반응에서 이어지는 짧은 행동 (몇 분짜리): 창고 장비 가져와 켜기 · 담요 · 창가 · 화면 곁 · 히터/선풍기/친한 사람 곁 ·
//  제자리 뛰기 · 낯선 소리 확인 · 말 걸기 · 위로 · 새 소품 구경 · 더 나은 방으로 옮기기 · 전등 손보기 · 따뜻한 차/찬물.
//  끊기면(경보 · 위기) 들고 있던 장비는 그 자리에 내려놓고, 방법은 남는다 (다음에 둘러볼 때 다시 고른다).

public sealed partial class ReactSystem
{
    internal Job? MakeJob(Activity a, CrewMember c, ReactState s, ReactAct act, DistanceField dist)
    {
        var w = _w;
        var toils = new List<Toil>();
        string log = act.Label;
        Room? room = c.Room;
        bool StillBad(World world) => act.For switch
        {
            Stir.Dark => room != null && (room.Dark || PortableSystem.Unlit(room)),
            Stir.Cold => (c.Room?.Air.Temperature ?? 20f) < 18.5f,
            Stir.Heat => (c.Room?.Air.Temperature ?? 20f) > 25f,
            _ => true,
        };
        switch (act.Kind)
        {
            case ReactKind.Device:
            {
                var d = w.Portable.Devices.FirstOrDefault(x => x.Id == act.Target);
                if (d == null || !d.Stored || d.Lost || d.Broken || d.HeldBy != null || d.ClaimedBy >= 0 && d.ClaimedBy != c.Id || !dist.Reachable(d.At) || !dist.Reachable(act.To)) return null;
                var target = room!;
                string key = act.For switch { Stir.Dark => "dark", Stir.Cold => "cold", _ => "hot" } + $":{target.Id}";
                string why = act.For switch { Stir.Dark => target.Powered ? "조명이 나갔다" : "정전 — 캄캄하다", Stir.Cold => $"춥다 ({target.Air.Temperature:0}℃)", _ => $"덥다 ({target.Air.Temperature:0}℃)" };
                toils.Add(new GotoToil(d.At));
                toils.Add(new DoToil((cm, world) => world.Portable.PickUp(d, cm, null)));
                toils.Add(new GotoToil(act.To));
                toils.Add(new DoToil((cm, world) =>
                {
                    var need = new PortableNeed { Task = PortableTask.Setup, Kind = d.Kind, Room = target, Spot = act.To, Why = why, Urgency = 0.5f, Key = key, Aim = target.Center };
                    if (!world.Portable.Install(cm, need, d, null, null)) return false;
                    Stats.Devices++;
                    var st = Of(cm);
                    st.Way = act.Way == "heater_fetch" ? "heater" : act.Way == "fan_fetch" ? "fan" : act.Way;
                    st.WayFor = act.For;
                    st.WaySince = world.Tick;
                    Gest(st, act.For == Stir.Cold ? Gesture.RubHands : act.For == Stir.Heat ? Gesture.FanSelf : Gesture.Nod, Short);
                    return true;
                }));
                log = $"{target.Name} {why} — {Ko.EulReul(d.Name)} 가지러 간다";
                break;
            }
            case ReactKind.Blanket:
            {
                if (!dist.Reachable(act.To)) return null;
                toils.Add(new GotoToil(act.To));
                toils.Add(new DoToil((cm, world) => { Wrap(cm, Of(cm)); return Of(cm).Wrapped; }));
                break;
            }
            case ReactKind.Window or ReactKind.Screen or ReactKind.Near or ReactKind.Move:
            {
                if (!dist.Reachable(act.To)) return null;
                toils.Add(new GotoToil(act.To));
                var g = act.Way switch
                {
                    "window" => Gesture.Window, "screen" => Gesture.Screen, "huddle" => Gesture.Huddle, "heater" => Gesture.RubHands,
                    "fan" => Gesture.FanSelf, "follow" => Gesture.Look, "warm_room" => Gesture.RubHands, _ => Gesture.WipeBrow,
                };
                int target = act.Target;
                bool sit = act.Way is "huddle" or "warm_room" or "cool_room";
                toils.Add(new DoToil((cm, world) =>
                {
                    var st = Of(cm);
                    if (act.Way == "huddle") { Stats.Huddles++; st.LookCrew = target; }
                    Gest(st, g, SimTime.Minutes(25));
                    st.LookAt = act.Face == default ? null : act.Face;
                    if (act.Way is "warm_room" or "cool_room") { st.Way = act.Way; st.WayFor = act.For; st.WaySince = world.Tick; }
                    return true;
                }));
                toils.Add(new WaitToil(SimTime.Minutes(25), sit ? Pose.Sitting : Pose.Standing, act.Face == default ? null : act.Face, SimTime.Minutes(4))
                {
                    DoneWhen = (cm, world) => !StillBad(world) || act.Way == "huddle" && (Crew(target) is not CrewMember o || o.IsMoving || !o.CanAct),
                    EveryTick = (cm, world) => { var st = Of(cm); if (world.Tick + 2 > st.GUntil) st.GUntil = world.Tick + 3; },
                });
                break;
            }
            case ReactKind.Jog:
            {
                toils.Add(new DoToil((cm, world) => { Gest(Of(cm), Gesture.Jog, SimTime.Minutes(8)); return true; }));
                toils.Add(new WaitToil(SimTime.Minutes(8), Pose.Standing, null, SimTime.Minutes(5))
                {
                    EveryTick = (cm, world) => { cm.Fitness = MathF.Min(1f, cm.Fitness + 0.03f / SimTime.TicksPerHour); },
                    DoneWhen = (cm, world) => !StillBad(world),
                });
                break;
            }
            case ReactKind.Check:
            {
                var m = w.Ship.Machines.FirstOrDefault(x => x.Body.Id == act.Target);
                if (m == null || m.Body.Room is not Room mr) return null;
                var spot = m.Body.UseSpots.FirstOrDefault(x => dist.Reachable(x));
                if (!dist.Reachable(spot)) { if (FreeNear(mr, m.Body.Center) is Cell fs && dist.Reachable(fs)) spot = fs; else return null; }
                toils.Add(new GotoToil(spot));
                toils.Add(new DoToil((cm, world) => { var st = Of(cm); Gest(st, Gesture.Listen, SimTime.Minutes(3)); st.LookAt = m.Body.Center; return true; }));
                toils.Add(new WaitToil(SimTime.Minutes(3), Pose.Working, m.Body.Center, SimTime.Minutes(2)));
                toils.Add(new DoToil((cm, world) => { CheckDone(cm, m); return true; }));
                log = $"{m.Name}에서 이상한 소리 — 가서 귀를 대 본다";
                break;
            }
            case ReactKind.TalkTo or ReactKind.Comfort:
            {
                if (Crew(act.Target) is not CrewMember o || o.Dead || !o.CanAct || Beside(o) is not Cell bc || !dist.Reachable(bc)) return null;
                bool comfort = act.Kind == ReactKind.Comfort;
                toils.Add(new GotoToil(bc));
                toils.Add(new DoToil((cm, world) =>
                {
                    if ((o.Position - cm.Position).LengthSquared() > 6f) return false;
                    var st = Of(cm);
                    Gest(st, comfort ? Gesture.Comfort : Gesture.Talk, SimTime.Minutes(comfort ? 8 : 4));
                    st.LookAt = o.Position; st.LookCrew = o.Id;
                    if (comfort) Comfort(cm, o); else TalkTo(cm, o, act.Way);
                    return true;
                }));
                toils.Add(new WaitToil(SimTime.Minutes(comfort ? 8 : 4), comfort ? Pose.Sitting : Pose.Standing, null, SimTime.Minutes(2))
                {
                    EveryTick = (cm, world) => { var st = Of(cm); st.LookAt = o.Position; },
                });
                toils.Add(new DoToil((cm, world) => { if (comfort) ComfortEnd(cm, o); else TalkEnd(cm, o, act.Way); return true; }));
                log = comfort ? $"{Ko.IGa(o.Name)} 우는 걸 보고 곁으로 간다" : $"{Ko.IGa(o.Name)} {act.Way} — 무슨 일인지 물으러 간다";
                break;
            }
            case ReactKind.Admire:
            {
                if (!dist.Reachable(act.To)) { if (room != null && FreeNear(room, act.Face) is Cell fa && dist.Reachable(fa)) toils.Add(new GotoToil(fa)); else return null; }
                else toils.Add(new GotoToil(act.To));
                toils.Add(new DoToil((cm, world) => { var st = Of(cm); Gest(st, Gesture.Admire, SimTime.Minutes(4)); st.LookAt = act.Face; Stats.Admired++; return true; }));
                toils.Add(new WaitToil(SimTime.Minutes(4), Pose.Standing, act.Face, SimTime.Minutes(2)));
                break;
            }
            case ReactKind.Fix:
            {
                if (room == null || !dist.Reachable(act.To)) return null;
                var fixRoom = room;
                toils.Add(new GotoToil(act.To));
                toils.Add(new DoToil((cm, world) => { var st = Of(cm); Gest(st, Gesture.Point, SimTime.Minutes(15)); st.LookAt = fixRoom.Center; return true; }));
                toils.Add(new WorkToil(0.25f, Skill.Electrical, fixRoom.Center) { CanContinue = (cm, world) => fixRoom.LightsOut && fixRoom.Powered });
                toils.Add(new DoToil((cm, world) => { FixDone(cm, fixRoom); return true; }));
                log = $"{fixRoom.Name} 전등을 손본다";
                break;
            }
            case ReactKind.Cup:
            {
                if (!dist.Reachable(act.To)) return null;
                toils.Add(new GotoToil(act.To));
                toils.Add(new WaitToil(SimTime.Minutes(3), Pose.Standing, act.Face, SimTime.Minutes(2)));
                toils.Add(new DoToil((cm, world) =>
                {
                    var st = Of(cm);
                    st.CupUntil = world.Tick + SimTime.Minutes(40);
                    st.Way = "cup"; st.WayFor = act.For; st.WaySince = world.Tick;
                    Gest(st, Gesture.Cup, SimTime.Minutes(20));
                    Stats.Cups++;
                    cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.03f);
                    return true;
                }));
                break;
            }
            default: return null;
        }
        return new Job(a, act.Label, toils)
        {
            LogText = log,
            TargetRoom = room,
            InterruptMargin = 0.08f,
            OnFinished = (cm, world, status) =>
            {
                if (status == ToilStatus.Succeeded) Stats.ActsDone++;
                else foreach (var d in world.Portable.Devices.Where(x => x.HeldBy == cm).ToList()) world.Portable.Drop(d, cm);
            },
        };
    }

    /// <summary>귀를 대 본다: 기계 솜씨 · 성실 · 경력이 있으면 정비 전조를 찾는다 (못 찾으면 "모르겠다" — 그래도 컴퓨터는 그 설비를 눈여겨본다).</summary>
    private void CheckDone(CrewMember c, Machine m)
    {
        var w = _w;
        Stats.Checks++;
        if (m.Omen is not Omen o) { Speak(c, Of(c), Stir.Sound, new[] { "멎었네 — 뭐였지", "지금은 조용하다", "기분 탓이었나" }, c.Room, m.Name, quiet: true); return; }
        float p = 0.3f + 0.45f * c.SkillLevel(Skill.Mechanics) + 0.15f * c.Traits.Diligence + (c.Role is CrewRole.Engineer or CrewRole.Technician ? 0.15f : 0f);
        if (o.Known) p = 1f;
        if (R.Chance(Math.Clamp(p, 0.1f, 0.95f)))
        {
            bool was = o.Known;
            Prevention.Detect(w, m, o, "소리를 듣고 확인", c);
            if (!was) Stats.Found++;
            Gest(Of(c), Gesture.Point, Short);
            Speak(c, Of(c), Stir.Sound, FoundLines(c, m, o.Kind), c.Room, m.Name);
            w.Log.Add(w.Tick, LogKind.Work, $"{m.Name}에서 나던 소리를 따라가 봤다 — {Prevention.Name(o.Kind)}", c.Id);
        }
        else
        {
            Gest(Of(c), Gesture.Shrug, Short);
            Speak(c, Of(c), Stir.Sound, new[] { "귀를 대 봐도 모르겠네", "들렸다 안 들렸다 하네 — 나중에 다시 봐야겠다", $"{m.Name} 쪽이긴 한데…" }, c.Room, m.Name, quiet: true);
        }
    }

    private static string[] FoundLines(CrewMember c, Machine m, OmenKind k) => k switch
    {
        OmenKind.Vibration => new[] { $"{m.Name} 축이 떨린다 — 베어링 같아", "여기다, 손에 떨림이 온다", "이거 그냥 두면 안 되겠는데" },
        OmenKind.Heat => new[] { $"{m.Name} 덮개가 뜨겁다 — 선이 달았어", "여기 손대 봐, 이상하게 뜨거워", "타닥거리던 게 이거였네 — 적어 둬야지" },
        OmenKind.Pressure => new[] { $"{m.Name} 관에서 쉭쉭 새는 소리", "필터가 막혔나 — 숨 쉬는 소리가 달라", "이음매에서 샌다" },
        OmenKind.Drift => new[] { $"{m.Name} 계기 바늘이 혼자 떨려", "삑삑거리던 게 이 계기였네", "숫자가 왔다 갔다 해 — 센서 같아" },
        _ => new[] { $"{m.Name}였구나", "찾았다", "여기서 나는 소리였어" },
    };

    private void FixDone(CrewMember c, Room room)
    {
        var w = _w;
        if (!room.LightsOut) return;
        float p = 0.3f + 0.6f * c.SkillLevel(Skill.Electrical);
        var s = Of(c);
        if (R.Chance(Math.Clamp(p, 0.15f, 0.9f)))
        {
            room.LightsOut = false;
            Stats.Fixes++;
            foreach (var o in w.Board.Open.Where(o => o.Kind == WorkKind.FixLights && o.Target.Room == room).ToList()) w.Board.Close(o);
            MarkLog.Add(room.Marks, w.Tick, $"{Ko.IGa(c.Name)} 등을 다시 끼웠다");
            w.Log.Add(w.Tick, LogKind.Work, $"{room.Name} 전등 접속부가 헐거웠다 — 다시 끼우니 들어온다", c.Id);
            Gest(s, Gesture.Nod, Short);
            Speak(c, s, Stir.Dark, new[] { "됐다 — 접속부가 헐거웠어", "봐, 들어오지", "끼우기만 하면 되는 거였네" }, room, "fix");
        }
        else
        {
            Gest(s, Gesture.Shrug, Short);
            Speak(c, s, Stir.Dark, new[] { "안쪽 선이 탔네 — 케이블이 있어야겠다", "이건 손으로 안 되겠다", "등 문제가 아니야 — 선을 갈아야 해" }, room, "fix");
        }
    }

    // ───────────────────────────── 사람 사이 ─────────────────────────────

    private void Comfort(CrewMember c, CrewMember o)
    {
        Stats.Comforts++;
        Speak(c, Of(c), Stir.Cry, ComfortLines(c, o), c.Room, o.Name);
        Gest(Of(o), Gesture.Cry, SimTime.Minutes(8));
        Of(o).LookAt = c.Position; Of(o).LookCrew = c.Id;
    }

    private void ComfortEnd(CrewMember c, CrewMember o)
    {
        var w = _w;
        if (BrainSystem.Enabled) w.Brain2.Emotions.Feel(o, Feeling.Sadness, -0.12f - 0.1f * MathF.Max(0f, o.AffinityTo(c)), "");
        o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.06f);
        o.Needs.Social = MathF.Min(1f, o.Needs.Social + 0.15f);
        o.ChangeAffinity(c, 0.06f);
        c.ChangeAffinity(o, 0.03f);
        Gest(Of(o), Gesture.Nod, Short);
        Speak(o, Of(o), Stir.Cry, new[] { "…고마워", "괜찮아, 이제 좀 나아", $"{c.Name}, 와 줘서 고마워", "그냥 좀 그랬어" }, o.Room, c.Name, reply: true);
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(o.Name)} 우는 걸 보고 곁에 앉아 있었다", c.Id);
    }

    private void TalkTo(CrewMember c, CrewMember o, string what)
    {
        Stats.Talks++;
        Speak(c, Of(c), Stir.Odd, AskLines(c, o, what), c.Room, what);
    }

    private void TalkEnd(CrewMember c, CrewMember o, string what)
    {
        var w = _w;
        var os = Of(o);
        os.LookAt = c.Position; os.LookCrew = c.Id;
        // 몰래 하던 일이면 둘러댄다 (그 일의 그럴싸한 겉말)
        var sc = w.Schemes.LeadOf(o);
        string[] reply;
        if (sc != null && sc.Hiding && o.Job?.Activity is SchemeActivity)
        {
            string cover = sc.Spec.Legit.Length > 0 ? sc.Spec.Legit : "별거 아니야";
            reply = new[] { $"아, {cover}", $"응? {cover} — 신경 쓰지 마", $"{cover}. 왜?" };
            Gest(os, Gesture.Shrug, Short);
        }
        else reply = ReplyLines(o, c, what);
        Gest(os, os.G == Gesture.Jog || os.G == Gesture.Huddle ? os.G : Gesture.Talk, Short);
        Speak(o, os, Stir.Odd, reply, o.Room, what, reply: true);
        Stats.Replies++;
        c.ChangeAffinity(o, 0.02f);
        o.ChangeAffinity(c, 0.02f);
        c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.05f);
        o.Needs.Social = MathF.Min(1f, o.Needs.Social + 0.05f);
    }
}
