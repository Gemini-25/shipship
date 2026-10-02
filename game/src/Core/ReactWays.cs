using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.8 같은 문제를 사람마다 다르게 푼다.
//  어두우면: 손전등(차고 다니는 사람) · 작업등(창고에서 끌어 온다) · 전등 고치기(전기 솜씨) · 차단기(두뇌의 정전 대처와 같은 판단) ·
//            창가 별빛 · 콘솔 화면 · 손목 단말 · 남의 불빛 곁 · 벽 짚기 · 가만히.
//  추우면: 내 담요 · 도는 히터 앞 · 창고 히터 · 제자리 뛰기 · 친한 사람 곁 · 따뜻한 차 · 따뜻한 방 · 팔짱.
//  더우면: 겉옷 벗기 · 도는 선풍기 앞 · 창고 선풍기 · 손부채 · 시원한 물 · 시원한 방 · 땀 닦기.
//  고르는 것: 성격 · 솜씨 · 가진 것(손전등 · 담요 · 창고 장비) · 해 본 것(여러 갈래 해법의 내 기록) · 방에서 남이 이미 고른 것(같은 걸 덜 고른다).
//  여러 갈래 해법 표(Ways)의 같은 이름(lamp · torch · screen · fix · feel · blanket · huddle · jog · heater)을 쓰고, 내 기록(Ways.Mine)이 고르는 데 들어간다.

public sealed partial class ReactSystem
{
    private void React(CrewMember c, ReactState s, Room room, Stir k, object? arg, List<CrewMember> ppl, bool busy)
    {
        var w = _w;
        s.Last[(int)k] = w.Tick;
        Stats.ByStir[(int)k]++;
        switch (k)
        {
            case Stir.Alarm:
                Gest(s, Life.Has(c, Habit.Worrier) || c.Traits.Calm < 0.3f ? Gesture.CoverEars : Gesture.Listen, Short);
                s.LookAt = room.Center;
                if (!busy) Speak(c, s, k, Pool(c, k, false), room, Crisis.Now(w).Top);
                break;
            case Stir.Dark: s.Ep[(int)k] = true; Cope(c, s, room, k, ppl); break;
            case Stir.Cold: s.Ep[(int)k] = true; Cope(c, s, room, k, ppl); break;
            case Stir.Heat: s.Ep[(int)k] = true; Cope(c, s, room, k, ppl); break;
            case Stir.Smoke:
                Gest(s, R.Chance(0.5f) ? Gesture.Cough : Gesture.CoverNose, Short);
                Speak(c, s, k, Pool(c, k, w.Fire.CountIn(room) > 0), room, "");
                break;
            case Stir.Shake:
                Gest(s, Gesture.Brace, Short);
                Speak(c, s, k, Pool(c, k, false), room, "");
                break;
            case Stir.Sound when arg is Machine m:
            {
                Gest(s, Gesture.Look, Short);
                s.LookAt = m.Body.Center;
                var kind = m.Omen?.Kind ?? OmenKind.Vibration;
                Speak(c, s, k, SoundLines(c, m, kind, m.Body.Room != room), room, m.Name);
                // 궁금하면 가서 귀를 대 본다: 성실함 · 기계 솜씨 · 구경 버릇 (걱정이 많으면 남에게 말한다)
                float go = 0.15f + 0.35f * c.Traits.Diligence + 0.3f * c.SkillLevel(Skill.Mechanics) + (Life.Has(c, Habit.Tinkerer) || Life.Has(c, Habit.Gazer) ? 0.2f : 0f) - (Life.Has(c, Habit.Procrastinator) ? 0.25f : 0f);
                if (R.Chance(Math.Clamp(go, 0.05f, 0.85f)))
                    Plan(c, s, new ReactAct { Kind = ReactKind.Check, For = k, Target = m.Body.Id, Label = "무슨 소리인지 가 본다", Way = "check", Score = 0.48f, Face = m.Body.Center, Until = w.Tick + SimTime.Minutes(30) });
                break;
            }
            case Stir.Smell when arg is SmellKind sk:
                Gest(s, sk is SmellKind.Burnt or SmellKind.Foul ? Gesture.CoverNose : Gesture.Sniff, Short);
                Speak(c, s, k, SmellLines(c, sk), room, SmellSystem.Name(sk));
                break;
            case Stir.Voice when arg is Broadcast b:
                s.LastHeard = b.Id;
                Gest(s, b.Priority >= 2 ? Gesture.Look : Gesture.Listen, Short);
                s.LookAt = room.Center;
                Speak(c, s, k, VoiceLines(c, b.Text, b.Priority), room, b.Text);
                break;
            case Stir.Voice when arg is Briefing bf:
                s.LastBrief = bf.Day;
                Gest(s, Gesture.Nod, Short);
                Speak(c, s, k, BriefLines(c, bf), room, "아침 브리핑");
                break;
            case Stir.Novel when arg is PlacedProp p:
            {
                Gest(s, Gesture.Admire, Long);
                s.LookAt = p.At.Center;
                bool likes = w.Props.Likes(c, p);
                var maker = p.Maker >= 0 ? Crew(p.Maker) : null;
                Speak(c, s, k, NovelLines(c, p.Spec.Name, maker, likes), room, p.Spec.Name);
                if (likes) w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.06f, $"{p.Spec.Name}이 걸렸다");
                if (maker != null && maker != c && likes) c.ChangeAffinity(maker, 0.03f);
                if ((p.At.Center - c.Position).LengthSquared() > 6f && R.Chance(0.4f + (likes ? 0.3f : 0f)))
                    Plan(c, s, new ReactAct { Kind = ReactKind.Admire, For = k, To = p.At, Face = p.At.Center, Label = $"{p.Spec.Name} 구경", Way = "admire", Score = 0.4f, Until = w.Tick + SimTime.Minutes(20) });
                break;
            }
            case Stir.Cry when arg is CrewMember o:
            {
                Gest(s, Gesture.Stare, Short);
                s.LookAt = o.Position; s.LookCrew = o.Id;
                float care = 0.2f + 0.5f * MathF.Max(0f, c.AffinityTo(o)) + 0.3f * c.Traits.Sociability + (c.Value == CrewValue.People ? 0.15f : 0f) - (Life.Has(c, Habit.Loner) ? 0.25f : 0f);
                if (R.Chance(Math.Clamp(care, 0.05f, 0.9f)))
                    Plan(c, s, new ReactAct { Kind = ReactKind.Comfort, For = k, Target = o.Id, Label = $"{o.Name} 곁으로", Way = "comfort", Score = 0.58f, Face = o.Position, Until = w.Tick + SimTime.Minutes(20) });
                else Speak(c, s, k, new[] { $"…{Ko.EunNeun(o.Name)} 혼자 있고 싶은가 봐", "모른 척해 주는 게 낫겠지", $"{o.Name} 우는 거 처음 봐" }, room, o.Name, quiet: true);
                Stats.Cries++;
                break;
            }
            case Stir.Odd when arg is ValueTuple<CrewMember, string> odd:
            {
                var (o, what) = odd;
                Stats.Odd++; Stats.Stares++;
                Gest(s, Gesture.Stare, Short);
                s.LookAt = o.Position; s.LookCrew = o.Id;
                float talk = 0.2f + 0.45f * c.Traits.Sociability + 0.3f * MathF.Max(0f, c.AffinityTo(o)) + (Life.Has(c, Habit.Talker) ? 0.2f : 0f) - (Life.Has(c, Habit.Loner) ? 0.2f : 0f);
                if (R.Chance(Math.Clamp(talk, 0.05f, 0.85f)))
                    Plan(c, s, new ReactAct { Kind = ReactKind.TalkTo, For = k, Target = o.Id, Label = $"{o.Name}에게 말을 건다", Way = what, Score = 0.46f, Face = o.Position, Until = w.Tick + SimTime.Minutes(15) });
                else Speak(c, s, k, OddMutter(c, o, what), room, what, quiet: true);
                break;
            }
        }
    }

    // ───────────────────────────── 더위 · 추위 · 어둠: 방법 고르기 ─────────────────────────────

    private void Cope(CrewMember c, ReactState s, Room room, Stir k, List<CrewMember> ppl)
    {
        var w = _w;
        var opts = k switch { Stir.Dark => DarkWays(c, s, room, ppl), Stir.Cold => ColdWays(c, s, room, ppl), _ => HeatWays(c, s, room, ppl) };
        if (opts.Count == 0) return;
        long key = room.Id * 32L + (int)k;
        if (!_taken.TryGetValue(key, out var tk) || w.Tick - tk.tick > SimTime.Hours(3)) _taken[key] = tk = (new List<string>(), w.Tick);
        // 남이 이미 고른 것은 덜 고른다 (장비 하나뿐인 것은 더) · 해 본 것은 더 (여러 갈래 해법의 내 기록) · 사람마다 조금씩
        for (int i = 0; i < opts.Count; i++)
        {
            var (id, sc, why, act) = opts[i];
            int same = tk.ways.Count(x => x == id);
            bool one = act is { Kind: ReactKind.Device or ReactKind.Fix or ReactKind.Window or ReactKind.Screen };
            if (same > 0) sc *= MathF.Pow(one ? 0.2f : 0.45f, same);
            var (ok, bad) = w.Ways.Mine(c, WaysId(id));
            sc *= 1f + 0.12f * Math.Min(3, ok) - 0.15f * Math.Min(2, bad);
            sc += 0.07f * (((c.Id * 31 + id.Length * 17 + (int)k * 7) % 11) / 10f);
            opts[i] = (id, sc, why, act);
        }
        int best = 0;
        for (int i = 1; i < opts.Count; i++) if (opts[i].s > opts[best].s || opts[i].s == opts[best].s && string.CompareOrdinal(opts[i].id, opts[best].id) < 0) best = i;
        var pick = opts[best];
        tk.ways.Add(pick.id);
        s.Way = pick.id;
        s.WayFor = k;
        s.WaySince = w.Tick;
        s.WayWhy = pick.why;
        Apply(c, s, room, k, pick.id, pick.act);
        Speak(c, s, k, OpenLines(c, k, room).Concat(Array.Empty<string>()).ToArray(), room, "", wayLine: WayLines(c, k, pick.id, pick.act));
        if (BrainSystem.Enabled && k == Stir.Dark && (c.Fears.Contains(Fear.Dark) || pick.id == "wait")) w.Brain2.Emotions.Feel(c, Feeling.Fear, 0.08f, "캄캄해졌다");
    }

    private static string WaysId(string id) => id switch { "heater_fetch" => "heater", "fan_fetch" => "fan", _ => id };

    private void Apply(CrewMember c, ReactState s, Room room, Stir k, string id, ReactAct? act)
    {
        switch (id)
        {
            case "torch": s.TorchOn = true; Stats.Torches++; Gest(s, Gesture.Torch, Long); break;
            case "wrist": Gest(s, Gesture.Screen, Long); break;
            case "feel": Gest(s, Gesture.FeelWall, Long); break;
            case "wait": Gest(s, Gesture.HugSelf, Long); break;
            case "breaker": Gest(s, Gesture.Point, Short); s.LookAt = room.Center; break;
            case "jacket": s.JacketOff = true; s.JacketSince = _w.Tick; Stats.Jackets++; Gest(s, Gesture.ShedJacket, Short); break;
            case "fanself": Gest(s, Gesture.FanSelf, Long); break;
            case "wipe": Gest(s, Gesture.WipeBrow, Short); break;
            case "hug": Gest(s, R.Chance(0.5f) ? Gesture.RubHands : Gesture.HugSelf, Long); break;
            case "jog": Stats.Jogs++; Gest(s, Gesture.Jog, SimTime.Minutes(8)); break;
            case "blanket" when act == null: Wrap(c, s); break;
            default: Gest(s, k == Stir.Heat ? Gesture.WipeBrow : k == Stir.Cold ? Gesture.HugSelf : Gesture.Look, Short); break;
        }
        if (act != null) Plan(c, s, act);
    }

    internal void Wrap(CrewMember c, ReactState s)
    {
        var b = MyBlanket(c);
        if (b == null) return;
        b.Holder = c.Id;
        b.At = null;
        s.Wrapped = true;
        s.Blanket = b.Id;
        Stats.Wraps++;
        Gest(s, Gesture.Wrap, Long);
    }

    private Belonging? MyBlanket(CrewMember c)
    {
        foreach (var b in _w.Belongings.All)
            if (b.Owner == c.Id && b.Kind == BelongingKind.Blanket && b.Usable && (b.Holder < 0 || b.Holder == c.Id) && b.BorrowedBy < 0) return b;
        return null;
    }

    private void Plan(CrewMember c, ReactState s, ReactAct act)
    {
        s.Pending = act;
        Stats.Acts++;
        c.NextThinkTick = Math.Min(c.NextThinkTick, _w.Tick + 1);
    }

    /// <summary>손전등을 차고 다니나: 기술직 · 공구 세트 · 꼼꼼한 버릇 · 몸 쓰는 일을 해 온 사람.</summary>
    public bool HasTorch(CrewMember c, out string why)
    {
        why = "";
        if (c.Role is CrewRole.Engineer or CrewRole.Electrician or CrewRole.Technician) { why = $"{CrewRoles.Name(c.Role)} 허리띠에 손전등"; return true; }
        if (c.Background is Background.Firefighter or Background.Miner or Background.SafetyInspector or Background.Climber or Background.Diver or Background.Soldier or Background.Police or Background.Lineworker)
        { why = $"{Life.Name(c.Background)} 시절 버릇 — 손전등은 늘 몸에"; return true; }
        if (Life.Has(c, Habit.Methodical) || Life.Has(c, Habit.Worrier) || Life.Has(c, Habit.Perfectionist)) { why = "주머니에 늘 작은 손전등"; return true; }
        foreach (var b in _w.Belongings.All)
            if (b.Owner == c.Id && b.Kind == BelongingKind.Toolset && b.Usable && (b.Holder == c.Id || b.At == null)) { why = "공구 세트에 든 손전등"; return true; }
        return false;
    }

    private PortableDevice? Stored(PortableKind k, CrewMember c)
    {
        PortableDevice? best = null;
        float bd = float.MaxValue;
        foreach (var d in _w.Portable.Devices)
        {
            if (d.Kind != k || !d.Stored || d.Lost || d.Broken || d.ClaimedBy >= 0 || d.HeldBy != null) continue;
            float dd = (d.At.Center - c.Position).LengthSquared();
            if (dd < bd) { bd = dd; best = d; }
        }
        return best;
    }

    private PortableDevice? Running(PortableKind k, Room room)
    {
        foreach (var d in _w.Portable.Devices)
            if (d.Kind == k && d.Placed && d.Running && _w.Ship.RoomAt(d.At) == room) return d;
        return null;
    }

    /// <summary>방 안 빈 바닥 (어떤 점에서 가까운 순).</summary>
    internal Cell? FreeNear(Room room, Vector2 at, int skip = 0)
    {
        Cell? best = null;
        float bd = float.MaxValue;
        foreach (var cell in room.Cells)
        {
            if (!_w.Ship.IsOpenFloor(cell) || _w.Portable.Occupied(cell)) continue;
            float d = (cell.Center - at).LengthSquared();
            if (d < 0.6f && skip > 0) continue;
            if (d < bd) { bd = d; best = cell; }
        }
        return best;
    }

    private Cell? WindowSpot(Room room, out Vector2 face)
    {
        face = default;
        foreach (var wb in _w.Body.WallList)
        {
            if (!wb.Window || wb.Shutter || wb.Room != room.Id) continue;
            foreach (var d in Cell.Dirs4)
            {
                var p = wb.Cell + d;
                if (_w.Ship.RoomAt(p) == room && _w.Ship.IsOpenFloor(p)) { face = wb.Cell.Center; return p; }
            }
        }
        return null;
    }

    private Room? Neighbor(Room room, bool warmer)
    {
        Room? best = null;
        float bt = room.Air.Temperature;
        foreach (var (nb, door) in _w.Ambience.Neighbors(room))
        {
            if (!door || nb.OffLimits || nb.Abandoned || nb.Air.Pressure < 70f || nb.Air.Smoke > 0.1f) continue;
            float t = nb.Air.Temperature;
            if (warmer ? t > bt + 3.5f && t < 27f : t < bt - 3.5f && t > 15f) { bt = t; best = nb; }
        }
        return best;
    }

    private CrewMember? Friend(CrewMember c, List<CrewMember> ppl, float min)
    {
        CrewMember? best = null;
        float ba = min;
        foreach (var o in ppl)
            if (o != c && o.CanAct && o.IsAwake && !o.IsMoving && c.AffinityTo(o) > ba) { ba = c.AffinityTo(o); best = o; }
        return best;
    }

    private Cell? Beside(CrewMember o)
    {
        foreach (var d in Cell.Dirs4)
        {
            var p = o.Cell + d;
            if (_w.Ship.IsWalkable(p) && _w.Ship.IsOpenFloor(p)) return p;
        }
        return null;
    }

    /// <summary>설비 곁 빈 자리 (길은 행동을 짤 때 다시 본다).</summary>
    private Cell? Spot(Furniture f, CrewMember c)
    {
        foreach (var u in f.UseSpots) if (_w.Ship.IsOpenFloor(u) && !_w.IsSpotTaken(u, c)) return u;
        return null;
    }

    private Furniture? Find(Room room, params FurnitureType[] ts)
    {
        foreach (var f in room.Furniture) if (!f.Stowed && Array.IndexOf(ts, f.Type) >= 0) return f;
        return null;
    }

    private List<(string id, float s, string why, ReactAct? act)> DarkWays(CrewMember c, ReactState s, Room room, List<CrewMember> ppl)
    {
        var w = _w;
        var t = c.Traits;
        float elec = c.SkillLevel(Skill.Electrical);
        long until = w.Tick + SimTime.Minutes(40);
        var o = new List<(string, float, string, ReactAct?)>();
        if (HasTorch(c, out var tw)) o.Add(("torch", 0.6f + 0.2f * t.Diligence, tw, null));
        if (Stored(PortableKind.WorkLamp, c) is PortableDevice lamp && Running(PortableKind.WorkLamp, room) == null && FreeNear(room, room.Center) is Cell ls)
            o.Add(("lamp", 0.3f + 0.3f * t.Diligence + 0.25f * elec + (c.Role is CrewRole.Electrician or CrewRole.Technician ? 0.12f : 0f), "창고에 작업등이 있다",
                new ReactAct { Kind = ReactKind.Device, Device = PortableKind.WorkLamp, Target = lamp.Id, To = ls, For = Stir.Dark, Label = "작업등을 가져온다", Way = "lamp", Score = 0.6f, Until = until }));
        if (room.LightsOut && room.Powered && elec >= 0.35f && FreeNear(room, room.Center) is Cell fs)
            o.Add(("fix", 0.35f + 0.6f * elec, "전등만 나갔다 — 고칠 수 있다",
                new ReactAct { Kind = ReactKind.Fix, To = fs, For = Stir.Dark, Face = room.Center, Label = "전등을 고친다", Way = "fix", Score = 0.62f, Until = until }));
        if (w.Brain2.Plans.Stances.TryGetValue(c.Id, out var st) && w.Tick - st.tick < SimTime.Minutes(15) && st.m is Method.ResetBreaker or Method.PowerRoom)
            o.Add(("breaker", 0.75f, st.why, null));
        if (WindowSpot(room, out var wf) is Cell wsp)
            o.Add(("window", 0.28f + (Life.Has(c, Habit.Gazer) ? 0.3f : 0f) + (c.Background is Background.Astronomer or Background.CargoPilot ? 0.18f : 0f), "창으로 바깥 빛이 든다",
                new ReactAct { Kind = ReactKind.Window, To = wsp, Face = wf, For = Stir.Dark, Label = "창가로 간다", Way = "window", Score = 0.5f, Until = until }));
        if (room.Powered && Find(room, FurnitureType.Console, FurnitureType.NavComputer) is Furniture con && Spot(con, c) is Cell cs)
            o.Add(("screen", 0.26f + (c.Background is Background.Programmer or Background.SysAdmin ? 0.3f : 0f) + (c.Role == CrewRole.Pilot ? 0.12f : 0f), "콘솔 화면은 켜져 있다",
                new ReactAct { Kind = ReactKind.Screen, To = cs, Face = con.Center, For = Stir.Dark, Label = "콘솔 화면 불빛 곁으로", Way = "screen", Score = 0.5f, Until = until }));
        o.Add(("wrist", 0.25f + (c.Background is Background.Programmer or Background.DroneRacer or Background.Reporter ? 0.15f : 0f), "손목 단말 불빛", null));
        foreach (var x in ppl)
        {
            if (x == c || !_st.TryGetValue(x.Id, out var xs) || !xs.TorchOn || Beside(x) is not Cell bx) continue;
            o.Add(("follow", 0.18f + 0.35f * t.Sociability + 0.35f * MathF.Max(0f, c.AffinityTo(x)), $"{x.Name}의 손전등 곁",
                new ReactAct { Kind = ReactKind.Near, Target = x.Id, To = bx, Face = x.Position, For = Stir.Dark, Label = $"{x.Name} 불빛 곁으로", Way = "follow", Score = 0.5f, Until = until }));
            break;
        }
        if (c.IsMoving) o.Add(("feel", 0.22f + 0.2f * t.Bravery, "벽을 짚어 간다", null));
        float fear = (BrainSystem.Enabled ? w.Brain2.Emotions.Get(c, Feeling.Fear) : 0f) + (c.Fears.Contains(Fear.Dark) ? 0.55f : 0f);
        o.Add(("wait", 0.1f + 0.6f * fear + 0.15f * (1f - t.Bravery), fear > 0.3f ? "어둠이 무섭다" : "부딪칠까 봐 가만히", null));
        return o;
    }

    private List<(string id, float s, string why, ReactAct? act)> ColdWays(CrewMember c, ReactState s, Room room, List<CrewMember> ppl)
    {
        var w = _w;
        var t = c.Traits;
        long until = w.Tick + SimTime.Minutes(40);
        var o = new List<(string, float, string, ReactAct?)>();
        if (!s.Wrapped && MyBlanket(c) is Belonging b)
        {
            bool home = c.Bed?.Room == room && b.At == null;
            float sc = 0.42f + (Life.Has(c, Habit.Homesick) ? 0.2f : 0f) + (b.Origin.Length > 0 ? 0.08f : 0f);
            if (home) o.Add(("blanket", sc + 0.15f, $"{b.Name} — 침대에 있다", null));
            else
            {
                Cell? at = b.At is Cell bc ? bc : c.Bed != null ? Spot(c.Bed, c) : null;
                if (at is Cell ac)
                    o.Add(("blanket", sc, $"{b.Name}을 가지러",
                        new ReactAct { Kind = ReactKind.Blanket, To = ac, Target = b.Id, For = Stir.Cold, Label = $"{b.Name}을 가지러 간다", Way = "blanket", Score = 0.5f, Until = until }));
            }
        }
        if (Running(PortableKind.Heater, room) is PortableDevice h && FreeNear(room, h.At.Center, 1) is Cell hs)
            o.Add(("heater", 0.55f, "히터가 돌고 있다", new ReactAct { Kind = ReactKind.Near, To = hs, Target = -1, Face = h.At.Center, For = Stir.Cold, Label = "히터 앞으로", Way = "heater", Score = 0.5f, Until = until }));
        else if (Stored(PortableKind.Heater, c) is PortableDevice sh && FreeNear(room, room.Center) is Cell hsp)
            o.Add(("heater_fetch", 0.24f + 0.3f * t.Diligence + 0.2f * c.SkillLevel(Skill.Electrical), "창고에 히터가 있다",
                new ReactAct { Kind = ReactKind.Device, Device = PortableKind.Heater, Target = sh.Id, To = hsp, For = Stir.Cold, Label = "히터를 가져온다", Way = "heater_fetch", Score = 0.56f, Until = until }));
        o.Add(("jog", 0.18f + (Life.Has(c, Habit.GymRat) ? 0.4f : 0f) + (c.Background == Background.Athlete ? 0.3f : 0f) + (Life.Has(c, Habit.Fidgety) ? 0.15f : 0f) + 0.1f * t.Bravery, "몸을 움직이면 데워진다",
            new ReactAct { Kind = ReactKind.Jog, For = Stir.Cold, Label = "제자리 뛰기", Way = "jog", Score = 0.45f, Until = until }));
        if (Friend(c, ppl, 0.2f) is CrewMember f && Beside(f) is Cell fc)
            o.Add(("huddle", 0.15f + 0.4f * t.Sociability + 0.4f * c.AffinityTo(f), $"{f.Name} 곁이 따뜻하다",
                new ReactAct { Kind = ReactKind.Near, Target = f.Id, To = fc, Face = f.Position, For = Stir.Cold, Label = $"{f.Name} 곁에 붙는다", Way = "huddle", Score = 0.48f, Until = until }));
        if ((Life.Has(c, Habit.TeaLover) || Life.Has(c, Habit.CoffeeAddict) || c.Role == CrewRole.Cook) && CupSpot(c) is (Cell cup, Vector2 cf))
            o.Add(("cup", 0.3f + 0.3f, Life.Has(c, Habit.CoffeeAddict) ? "뜨거운 커피" : "따뜻한 차",
                new ReactAct { Kind = ReactKind.Cup, To = cup, Face = cf, For = Stir.Cold, Label = Life.Has(c, Habit.CoffeeAddict) ? "뜨거운 커피 한 잔" : "따뜻한 차 한 잔", Way = "cup", Score = 0.46f, Until = until }));
        if (Neighbor(room, true) is Room warm && FreeNear(warm, warm.Center) is Cell wc)
            o.Add(("warm_room", 0.22f + (c.Value == CrewValue.Safety ? 0.15f : 0f), $"{warm.Name}이 더 따뜻하다",
                new ReactAct { Kind = ReactKind.Move, To = wc, Target = warm.Id, For = Stir.Cold, Label = $"{warm.Name}으로 옮긴다", Way = "warm_room", Score = 0.44f, Until = until }));
        o.Add(("hug", 0.2f, "팔짱을 낀다", null));
        return o;
    }

    private List<(string id, float s, string why, ReactAct? act)> HeatWays(CrewMember c, ReactState s, Room room, List<CrewMember> ppl)
    {
        var w = _w;
        var t = c.Traits;
        long until = w.Tick + SimTime.Minutes(40);
        var o = new List<(string, float, string, ReactAct?)>();
        if (!s.JacketOff && c.Suit == null) o.Add(("jacket", 0.42f + (c.Value == CrewValue.Freedom ? 0.1f : 0f) - (c.Value == CrewValue.Rules ? 0.12f : 0f), "겉옷을 벗는다", null));
        if (Running(PortableKind.Fan, room) is PortableDevice f && FreeNear(room, f.At.Center, 1) is Cell fs)
            o.Add(("fan", 0.55f, "선풍기가 돌고 있다", new ReactAct { Kind = ReactKind.Near, To = fs, Target = -1, Face = f.At.Center, For = Stir.Heat, Label = "선풍기 앞으로", Way = "fan", Score = 0.5f, Until = until }));
        else if (Stored(PortableKind.Fan, c) is PortableDevice sf && FreeNear(room, room.Center) is Cell fsp)
            o.Add(("fan_fetch", 0.24f + 0.3f * t.Diligence, "창고에 선풍기가 있다",
                new ReactAct { Kind = ReactKind.Device, Device = PortableKind.Fan, Target = sf.Id, To = fsp, For = Stir.Heat, Label = "선풍기를 가져온다", Way = "fan_fetch", Score = 0.55f, Until = until }));
        o.Add(("fanself", 0.24f + (Life.Has(c, Habit.Fidgety) ? 0.15f : 0f), "손부채", null));
        if (CupSpot(c) is (Cell cup, Vector2 cf))
            o.Add(("cup", 0.22f + (Life.Has(c, Habit.Snacker) ? 0.15f : 0f) + 0.1f * t.Diligence, "찬물 한 컵",
                new ReactAct { Kind = ReactKind.Cup, To = cup, Face = cf, For = Stir.Heat, Label = "찬물 한 컵", Way = "cup", Score = 0.44f, Until = until }));
        if (Neighbor(room, false) is Room cool && FreeNear(cool, cool.Center) is Cell cc)
            o.Add(("cool_room", 0.22f + (c.Value == CrewValue.Safety ? 0.15f : 0f), $"{cool.Name}이 시원하다",
                new ReactAct { Kind = ReactKind.Move, To = cc, Target = cool.Id, For = Stir.Heat, Label = $"{cool.Name}으로 옮긴다", Way = "cool_room", Score = 0.44f, Until = until }));
        o.Add(("wipe", 0.18f, "땀을 닦는다", null));
        return o;
    }

    /// <summary>차 · 물을 받을 곳: 커피 기계 · 배식기 · 화구 (가까운 식당 · 주방).</summary>
    private (Cell, Vector2)? CupSpot(CrewMember c)
    {
        var w = _w;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r.Kind is not (RoomType.Galley or RoomType.Mess or RoomType.Lounge) || r.OffLimits || !r.Powered) continue;
            if (Find(r, FurnitureType.CoffeeMachine, FurnitureType.MealDispenser, FurnitureType.WaterRecycler) is Furniture f && Spot(f, c) is Cell sp) return (sp, f.Center);
        }
        return null;
    }
}

/// <summary>v17.8 반응에서 이어지는 짧은 행동: 장비 가져오기 · 담요 · 창가 · 화면 곁 · 히터/선풍기/사람 곁 · 제자리 뛰기 · 소리 확인 · 말 걸기 · 위로 · 구경 · 방 옮기기 · 전등 고치기 · 잔.</summary>
public sealed class ReactActivity : Activity
{
    public override string Id => "react";
    public override string Label => "반응";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (ReactSystem.Off || w.React.Peek(c)?.Pending is not ReactAct a) return (0f, "—");
        if (w.Tick > a.Until || !c.CanAct || c.Outside || c.Suit != null) { w.React.Peek(c)!.Pending = null; return (0f, "—"); }
        if (Crisis.Acting(w) && a.For is not (Stir.Dark or Stir.Cold or Stir.Heat)) return (0f, "위기 중");
        if (c.Needs.Rest < 0.12f || c.Needs.Food < 0.12f) return (0f, "지쳤다");
        return (a.Score, a.Label);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var rs = w.React;
        var s = rs.Peek(c);
        if (s?.Pending is not ReactAct a) return null;
        s.Pending = null;
        return rs.MakeJob(this, c, s, a, dist);
    }
}
