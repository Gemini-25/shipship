using System;
using System.Collections.Generic;

namespace ShipSim.Core;

// v17.8 주 컴퓨터가 사람들의 몸짓을 읽는다 (데이터선이 닿는 방의 카메라 · 생체 신호):
//  · 떠는 사람이 둘 넘으면: 기온을 알리고 창고 히터 · 담요를 권한다 (따뜻해지면 맞혔다).
//  · 땀 흘리는 사람이 둘 넘으면: 물을 마시라 · 선풍기 · 시원한 방을 권한다.
//  · 캄캄한 방에서 꼼짝 못 하는 사람: 가까운 불빛(콘솔 화면 · 창)과 문 쪽을 알려 준다.
//  · 유리를 피해 돌아가는 사람이 잇따르면: 그 방 바닥을 치워 달라고 한다.
//  · 낯선 소리를 확인하고도 못 찾았으면: 그 방 감지기를 한 번 더 훑는다.

public sealed partial class ReactSystem
{
    private long _nextMind;
    private readonly Dictionary<int, long> _mindAt = new();

    private void Mind()
    {
        var w = _w;
        if (w.Tick < _nextMind) return;
        _nextMind = w.Tick + SimTime.Minutes(15);
        var a = w.Automation;
        if (!a.Present || !a.CoreOnline) return;
        var ship = w.Ship;
        for (int i = 0; i < ship.Rooms.Count; i++)
        {
            var room = ship.Rooms[i];
            if (room.Detached || !room.DataLinked || !_here.TryGetValue(i, out var ppl) || ppl.Count == 0) continue;
            int shiver = 0, sweat = 0, stuck = 0;
            foreach (var c in ppl)
            {
                if (!_st.TryGetValue(c.Id, out var s)) continue;
                if (s.Shiver >= 1 && !s.Wrapped) shiver++;
                if (s.Sweat >= 2) sweat++;
                if (s.Way == "wait" && s.WayFor == Stir.Dark && i < _dark.Length && _dark[i]) stuck++;
            }
            float t = room.Air.Temperature;
            if (shiver >= 2 && Due(i * 4))
            {
                string sug = Stored(PortableKind.Heater, ppl[0]) != null ? "창고에 히터가 있습니다" : "담요를 챙기세요";
                if (a.Book.Add(ActKind.Advice, room, $"{room.Name} {t:0}℃ — 떠는 사람 {shiver}명", "체온이 떨어진다", "기온 안내 방송", sug, $"react:cold:{i}", SimTime.Hours(3), 40f,
                        (world, act) => room.Air.Temperature > 18f ? (1, "따뜻해졌다") : null) != null)
                {
                    a.Speak.Announce($"{room.Name} 기온 {t:0}도입니다 — {sug}", room, 0);
                    Stats.Advice++;
                }
            }
            if (sweat >= 2 && Due(i * 4 + 1))
            {
                string sug = Stored(PortableKind.Fan, ppl[0]) != null ? "물을 드시고, 창고 선풍기를 쓰세요" : Neighbor(room, false) is Room cool ? $"물을 드시고, {cool.Name}이 더 시원합니다" : "물을 자주 드세요";
                if (a.Book.Add(ActKind.Advice, room, $"{room.Name} {t:0}℃ — 땀 흘리는 사람 {sweat}명", "열이 찬다 — 오래 있으면 쓰러진다", "기온 안내 방송", sug, $"react:hot:{i}", SimTime.Hours(3), 40f,
                        (world, act) => room.Air.Temperature < 27f ? (1, "식었다") : null) != null)
                {
                    a.Speak.Announce($"{room.Name} 기온 {t:0}도입니다 — {sug}", room, 0);
                    Stats.Advice++;
                }
            }
            if (stuck >= 1 && Due(i * 4 + 2))
            {
                string near = room.Powered && Find(room, FurnitureType.Console, FurnitureType.NavComputer) != null ? "콘솔 화면 쪽이 밝습니다" : WindowSpot(room, out _) != null ? "창 쪽에 별빛이 듭니다" : "벽을 따라 문 쪽으로 오세요";
                if (a.Book.Add(ActKind.Advice, room, $"{room.Name} 캄캄함 — 멈춰 선 사람 {stuck}명", "어둠 속에서 움직이지 못한다", "길 안내 방송", near, $"react:dark:{i}", SimTime.Hours(1), 20f) != null)
                {
                    a.Speak.Announce($"{room.Name} 조명이 나갔습니다 — {near}", room, 0);
                    Stats.Advice++;
                }
            }
        }
        // 유리를 피해 돌아간 사람이 한 시간에 둘 넘은 방
        Dictionary<int, int>? glass = null;
        for (int k = Notes.Count - 1; k >= 0; k--)
        {
            var n = Notes[k];
            if (w.Tick - n.Tick > SimTime.Hours(1)) break;
            if (n.Stir != Stir.Glass || n.Room < 0) continue;
            glass ??= new();
            glass[n.Room] = glass.GetValueOrDefault(n.Room) + 1;
        }
        if (glass != null)
            for (int i = 0; i < ship.Rooms.Count; i++)
            {
                if (glass.GetValueOrDefault(i) < 2 || !ship.Rooms[i].DataLinked || !Due(i * 4 + 3)) continue;
                var room = ship.Rooms[i];
                if (a.Book.Add(ActKind.Advice, room, $"{room.Name} — 유리 조각을 피해 돌아가는 사람 {glass[i]}명", "누가 밟으면 다친다", "치워 달라는 방송", "바닥 유리 치우기", $"react:glass:{i}", SimTime.Hours(4), 60f) != null)
                {
                    a.Speak.Announce($"{room.Name} 바닥에 유리 조각이 있습니다 — 지나가는 분은 돌아가시고, 손 비는 분이 치워 주세요", room, 0);
                    Stats.Advice++;
                }
            }
        SmokeMind();
    }

    /// <summary>기침하는 사람이 여럿인데 그 방엔 불이 없다: 연기가 어디서 오는지 옆방을 짚고 (감지기가 꺼진 방이면) 가 볼 사람을 부른다.</summary>
    private readonly Dictionary<int, int> _smokeCalls = new();

    /// <summary>"가 봐 주세요" 방송을 들은 사람: 대담하거나 성실한 사람 하나가 그 방으로 간다 (나머지는 들은 척만).</summary>
    private bool AnswerCall(CrewMember c, ReactState s, Broadcast b)
    {
        if (!_smokeCalls.TryGetValue(b.Id, out int rid) || rid < 0 || rid >= _w.Ship.Rooms.Count) return false;
        var src = _w.Ship.Rooms[rid];
        if (c.Room == src || c.Traits.Bravery + c.Traits.Diligence < 0.9f || Life.Has(c, Habit.Procrastinator) || FreeNear(src, src.Center) is not Cell sc) return false;
        _smokeCalls[b.Id] = -1;
        Plan(c, s, new ReactAct { Kind = ReactKind.Move, For = Stir.Smoke, To = sc, Face = src.Center, Target = src.Id, Label = $"방송 듣고 {src.Name}에 가 본다", Way = "smoke_seek", Score = 0.6f, Until = _w.Tick + SimTime.Minutes(30) });
        return true;
    }

    private void SmokeMind()
    {
        var w = _w;
        var a = w.Automation;
        Dictionary<int, int>? cough = null;
        for (int k = Notes.Count - 1; k >= 0; k--)
        {
            var n = Notes[k];
            if (w.Tick - n.Tick > SimTime.Minutes(20)) break;
            if (n.Stir != Stir.Smoke || n.Room < 0) continue;
            cough ??= new();
            cough[n.Room] = cough.GetValueOrDefault(n.Room) + 1;
        }
        if (cough == null) return;
        var ship = w.Ship;
        for (int i = 0; i < ship.Rooms.Count; i++)
        {
            int nc = cough.GetValueOrDefault(i);
            var room = ship.Rooms[i];
            if (nc < 2 || !room.DataLinked || w.Fire.CountIn(room) > 0 || !Due(100000 + i)) continue;
            var src = Smokier(room);
            bool blind = src != null && (!src.Powered || !src.DataLinked);
            string where = src != null ? $"{src.Name} 쪽에서 연기가 들어옵니다" : "연기가 어디서 오는지 아직 모릅니다";
            string ask = blind ? $"{src!.Name} 감지기가 꺼져 있습니다 — 손 비는 분이 가 봐 주세요" : "문을 닫고 입을 가리세요";
            if (a.Book.Add(ActKind.Advice, room, $"{room.Name} — 기침하는 사람 {nc}명", "어딘가 타고 있을 수 있다", "연기 안내 방송", src?.Name ?? "환기", $"react:smoke:{i}", SimTime.Hours(1), 50f) != null)
            {
                var b = a.Speak.Announce($"{room.Name}에 {where} — {ask}", room, blind ? 1 : 0);
                if (b != null && blind) _smokeCalls[b.Id] = src!.Id; // 들은 사람 하나가 가 본다
                Stats.Advice++;
                Stats.SmokeAdvice++;
            }
        }
    }

    private bool Due(int key)
    {
        if (_mindAt.TryGetValue(key, out var t) && _w.Tick - t < SimTime.Hours(2)) return false;
        _mindAt[key] = _w.Tick;
        return true;
    }
}
