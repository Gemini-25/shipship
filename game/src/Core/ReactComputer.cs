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
    }

    private bool Due(int key)
    {
        if (_mindAt.TryGetValue(key, out var t) && _w.Tick - t < SimTime.Hours(2)) return false;
        _mindAt[key] = _w.Tick;
        return true;
    }
}
