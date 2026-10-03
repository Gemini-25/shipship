using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v17.2 듣기: 승무원 귀 · 사라진 소리 · 주 컴퓨터의 진동 마이크.
//  · 물방울(팬이 멎어 들림) → 어디 새나 (믿음 · 귀 기울임) · 옆방 발소리 → 아는 사람이면 누군지 안다 · 선체 삐걱 → 걱정 많은 사람은 움찔
//  · 낡은 베어링 소리를 솜씨 있는 사람이 들으면 설비 기록에 남기고 말한다 · 거슬리는 소리는 잠 · 기분을 깎는다 (익숙해지면 덜)
//  · 거슬리던 소리가 수리로 사라지면 들어 온 사람이 알아챈다 · 팬 소리가 멎으면 정전인 줄 안다.

public sealed partial class HearingSystem
{
    private long _nextPc;

    /// <summary>3) 사람마다 듣는다.</summary>
    private void Listen()
    {
        var w = _w;
        long now = w.Tick;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.Down || c.Room is not Room r || r.Id >= _room.Length) continue;
            var ear = Ear(c);
            float annoy = 0f;
            foreach (var h in _room[r.Id])
            {
                var s = Sources[h.Src];
                if (s.Owner == c.Id || h.Masked) continue;
                float lv = h.Level * Sense(c, s.Kind);
                if (lv < Audible) { if (h.Muffled && h.Level >= Audible * 0.4f) Stats.Muffled++; continue; }
                Stats.Heard++;
                Stats.HeardKind[(int)s.Kind]++;
                ear.Last = s.Kind; ear.LastAt = now;
                if (s.Steady)
                {
                    int mins = ear.Minutes.TryGetValue(s.Key, out var mm) ? mm + 1 : 1;
                    ear.Minutes[s.Key] = mins;
                    if (mins == HabitMinutes) Stats.Habituated++;
                    if (mins == 20 && _steady.TryGetValue(s.Key, out var st) && !st.Heard.Contains(c.Id)) st.Heard.Add(c.Id);
                    if (s.Kind is Noise.Bearing or Noise.Rattle or Noise.Hiss or Noise.Crackle && mins < HabitMinutes) annoy += lv * (h.Muffled ? 0.5f : 1f);
                }
                if (!c.IsAwake) continue;
                switch (s.Kind)
                {
                    case Noise.Drip when Fresh(ear, s.Key, SimTime.Hours(3)):
                        w.Brain2.Beliefs.Learn(c, Topic.Water, s.Room, 1, BeliefSource.Guess, 0.45f);
                        Turn(c, s, Gesture.Listen);
                        Line(c, ear, Noise.Drip, s.Room, w.Ship.Rooms[s.Room].Name, _w.Ship.Rooms[s.Room].Powered
                            ? new[] { "어디서 물 떨어지는 소리가…", "똑, 똑 — 어디 새나?", "물방울 소리 들려?" }
                            : new[] { "팬이 멎으니까 물 떨어지는 소리가 다 들리네", "조용하니 똑똑 소리가…", "어디서 물이 새는 거지?" });
                        Stats.Drips++;
                        break;
                    case Noise.Step when s.Room != r.Id && now >= ear.NextStep && Person(s.Owner) is CrewMember o && !h.Muffled && (c.AffinityTo(o) >= 0.2f || o.Role == c.Role || o.Partner == c.Id):
                        ear.NextStep = now + SimTime.Minutes(15);
                        w.Brain2.Beliefs.Learn(c, Topic.Person, o.Id, s.Room, BeliefSource.Guess, 0.45f);
                        Stats.StepsKnown++;
                        if (R.Chance(0.15f)) Line(c, ear, Noise.Step, s.Room, o.Name, new[] { $"{o.Name} 발소리네", $"저 걸음은 {o.Name}이야", $"{Ko.IGa(o.Name)} 오나 보다" });
                        else Note(c, Noise.Step, s.Room, o.Name, "");
                        break;
                    case Noise.Creak when Fresh(ear, s.Key, SimTime.Hours(6)):
                        Stats.Creaks++;
                        bool worry = Life.Has(c, Habit.Worrier) || w.Relations.IsNewcomer(c) || c.Traits.Bravery < 0.3f;
                        if (worry) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.02f);
                        Turn(c, s, worry ? Gesture.Look : Gesture.Listen);
                        Line(c, ear, Noise.Creak, s.Room, "", worry
                            ? new[] { "방금 그 소리 뭐야? 선체가…", "벽이 우는 것 같아", "괜찮은 거 맞지?" }
                            : new[] { s.Sub == 1 ? "식느라 우는 소리야" : "데워지느라 늘어나는 소리야", "외판이 숨 쉬는 거야", "늘 저래" });
                        break;
                    case Noise.Bearing or Noise.Rattle or Noise.Hiss or Noise.Crackle when !h.Muffled && MachineOf(s.Owner) is Machine m && m.Omen == null
                                                                                          && c.SkillLevel(Skill.Mechanics) >= 0.4f && Fresh(ear, s.Key, SimTime.Hours(20)):
                        // 낡은 부품 소리 (전조가 아니라 수명) — 솜씨 있는 사람이 알아듣는다
                        w.Brain2.Beliefs.Learn(c, Topic.Omen, m.Body.Id, 1, BeliefSource.Guess, 0.4f);
                        MarkLog.Add(m.Marks, now, $"{c.Name}: 소리가 거칠다");
                        Turn(c, s, Gesture.Listen);
                        Line(c, ear, s.Kind, s.Room, m.Name, new[] { $"{m.Name} 베어링 소리가 거칠어 — 갈 때가 됐나", $"{m.Name}, 일정하게 끼익거리네", "저건 닳는 소리야" });
                        break;
                    case Noise.DoorShut when c.Pose == Pose.Sleeping:
                        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.003f);
                        break;
                }
            }
            // 거슬리는 소리: 잠을 설치고 짜증 (익숙해지면 덜)
            ear.Annoy = ear.Annoy * 0.95f + annoy * 0.05f;
            if (annoy > 0.12f)
            {
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.0012f * annoy * (c.Pose == Pose.Sleeping ? 2f : 1f));
                if (Fresh(ear, -7, SimTime.Hours(8)) && c.IsAwake)
                {
                    Stats.Annoyed++;
                    Line(c, ear, Noise.Bearing, r.Id, "", new[] { "저 소리 때문에 신경 쓰여", "또 끼익거리네", "저거 언제 고치나" });
                }
            }
        }
    }

    /// <summary>4) 사라진 소리: 수리로 조용해짐 · 팬이 멎음.</summary>
    private void Vanished()
    {
        var w = _w;
        long now = w.Tick;
        _gone.Clear();
        foreach (var (k, st) in _steady) if (st.Seen < now) _gone.Add(k);
        if (_gone.Count == 0) return;
        _gone.Sort();
        foreach (var key in _gone)
        {
            var st = _steady[key];
            _steady.Remove(key);
            if (st.Room < 0 || st.Room >= w.Ship.Rooms.Count) continue;
            var room = w.Ship.Rooms[st.Room];
            switch (st.Kind)
            {
                case Noise.Fan:
                    if (room.Powered && !room.BreakerOff) break;
                    foreach (var c in w.Crew)
                    {
                        if (c.Room != room || !c.IsAwake || c.Down) continue;
                        Stats.FanStops++;
                        w.Brain2.Beliefs.Learn(c, Topic.Outage, room.Id, (int)OutageCause.Power, BeliefSource.Guess, 0.6f);
                        var rs = w.React; var s = rs.Of(c);
                        rs.Gest(s, Gesture.Look, SimTime.Minutes(1));
                        Line(c, Ear(c), Noise.Fan, room.Id, room.Name, new[] { "팬 소리가 멎었다 — 정전인가", "…갑자기 조용해졌네", "환기가 꺼졌어" });
                    }
                    break;
                case Noise.Bearing or Noise.Rattle or Noise.Hiss or Noise.Crackle:
                    var m = st.Owner >= 0 ? w.Ship.Machines.FirstOrDefault(x => x.Body.Id == st.Owner) : null;
                    bool fixedNow = m != null ? m.Powered && m.Active && m.Omen == null && !m.Faults.Any(f => f.Kind == FaultKind.BearingWear) : room.Powered;
                    if (!fixedNow) break;
                    foreach (int id in st.Heard.OrderBy(x => x))
                    {
                        if (Person(id) is not CrewMember c || !c.IsAwake || c.Down || c.Room is not Room cr) continue;
                        if (cr != room && Pass(room, cr) < 0.05f) continue;
                        Ear(c).Minutes.Remove(key);
                        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.03f);
                        Stats.Quieted++;
                        string what = m?.Name ?? "패널";
                        Line(c, Ear(c), st.Kind, room.Id, what, new[] { $"이제 조용하네 — {what} 소리가 없어졌어", "고치고 나니 살 것 같다", "그 끼익 소리가 안 나니까 낯설다", "드디어 조용하다" }, force: true);
                    }
                    break;
            }
        }
    }

    /// <summary>5) 주 컴퓨터의 진동 마이크 (데이터선이 닿는 방): 오래가는 주기 잡음을 정비 권고로.</summary>
    private void Computer()
    {
        var w = _w;
        if (w.Tick < _nextPc) return;
        _nextPc = w.Tick + SimTime.Minutes(15);
        var a = w.Automation;
        if (!a.Present || !a.CoreOnline) return;
        foreach (var key in _steady.Keys.OrderBy(k => k).ToList())
        {
            var st = _steady[key];
            if (st.Advised || st.Kind is not (Noise.Bearing or Noise.Rattle) || w.Tick - st.Since < SimTime.Hours(3)) continue;
            if (st.Room < 0 || st.Room >= w.Ship.Rooms.Count || w.Ship.Rooms[st.Room] is not Room room || !room.DataLinked || MachineOf(st.Owner) is not Machine m) continue;
            if (m.Omen is { Known: true }) continue;
            st.Advised = true;
            string snd = st.Kind == Noise.Rattle ? "덜컹거리는" : "끼익 긁히는";
            long k2 = key;
            if (a.Book.Add(ActKind.Advice, room, $"{m.Name} 진동 마이크 — 일정한 간격으로 {snd} 소리", st.Kind == Noise.Rattle ? "고정이 풀린 소리다 — 두면 떨어져 나간다" : "베어링이 닳는 소리다 — 두면 멈춘다",
                    "정비 권고", "가서 귀를 대 보고 손봐 주세요", $"hear:{m.Body.Id}", SimTime.Hours(8), 60f,
                    (world, act) => _steady.ContainsKey(k2) ? null : (1, "조용해졌다")) != null)
            {
                a.Speak.Announce($"{room.Name} {m.Name}에서 일정한 간격으로 {snd} 소리가 잡힙니다 — 살펴봐 주세요", room, 0);
                Stats.Advice++;
                foreach (var c in w.Crew)
                    if (c.Room == room && c.IsAwake) w.Brain2.Beliefs.Learn(c, Topic.Omen, m.Body.Id, 1, BeliefSource.Computer, 0.5f);
            }
        }
    }

    // ───────────────────────────── 도움 ─────────────────────────────

    private CrewMember? Person(int id)
    {
        if (id < 0) return null;
        foreach (var c in _w.Crew) if (c.Id == id) return c;
        return null;
    }

    private bool Fresh(EarState e, long key, long gap)
    {
        if (e.Noticed.TryGetValue(key, out var t) && _w.Tick - t < gap) return false;
        e.Noticed[key] = _w.Tick;
        return true;
    }

    private void Turn(CrewMember c, NoiseSource s, Gesture g)
    {
        var rs = _w.React;
        if (ReactSystem.Off) return;
        var st = rs.Of(c);
        if (_w.Tick < st.GUntil && st.G != Gesture.None) return;
        rs.Gest(st, g, SimTime.Minutes(2));
        st.LookAt = s.At; st.LookCrew = -1;
    }

    private void Line(CrewMember c, EarState e, Noise k, int room, string what, string[] pool, bool force = false)
    {
        string line = pool[R.Range(0, pool.Length)];
        bool say = c.IsAwake && (force || _w.Tick >= e.NextLine) && c.SaidUntil < _w.Tick + SimTime.Minutes(2);
        if (say)
        {
            c.Say(_w, Persona.Say(c, line));
            e.NextLine = _w.Tick + SimTime.Minutes(20);
        }
        Note(c, k, room, what, say ? line : "");
    }

    private void Note(CrewMember c, Noise k, int room, string what, string line)
    {
        Notes.Add(new HearNote { Tick = _w.Tick, Crew = c.Id, Kind = k, Room = room, What = what, Line = line });
        if (Notes.Count > 400) Notes.RemoveRange(0, 100);
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Stats.Heard); I(Stats.Strange); I(Stats.Quieted); I(Stats.FanStops); I(Stats.Drips); I(Stats.StepsKnown); I(Stats.Creaks); I(Stats.Advice);
        I(Sources.Count); I(_steady.Count);
    }
}
