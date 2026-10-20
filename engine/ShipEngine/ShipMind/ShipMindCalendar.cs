namespace ShipEngine;

/// <summary>
/// ⑩ 주컴퓨터 — 항해 달력 (⑫ 긴 줄기 (나) · docs/ENGINE.md ⑤ '긴 줄기'): 전이 궤도에 오르면 별계 지도로 남은 길의 구간 · 태양 합을 내다보고 달력을 짠다
/// (닿은 뒤에는 앞으로 400일의 태양 합). 사흘 앞이면 방송 · 들 때 · 나올 때 방송 (태양 합의 들고 남은 통신이 알린다) · 아침 방송에 이레 안의 일.
/// <para>준비 (행동): 파편 구간을 지나는 동안 배 안 칸막이 문을 닫아 둔다 (10분마다 — 지나는 이는 열고 지나 닫는다 · 구멍이 나도 그 방만).</para>
/// </summary>
public sealed partial class ShipMind
{
    public struct CalEvent { public long From, To; public int Zone; public byte Told; }   // Zone: 구간 번호 · -1 태양 합 · Told 비트 1 예고 · 2 들었다 · 4 나왔다
    public readonly List<CalEvent> Calendar = new();
    public const double NoticeDays = 3, MorningDays = 7, ConjAheadDays = 400;
    public long CalBuilt, Notices, ZoneDoorsShut;
    private long _calKey = long.MinValue;

    private string EventName(in CalEvent e) => e.Zone >= 0 ? T.Space.Zones[e.Zone].Name : "태양 합";
    private static string Days(long ticks) { double d = ticks / (double)Time.TicksPerDay; return d < 1 ? $"{ticks / (double)Time.TicksPerHour:0}시간" : $"{d:0}일"; }

    private void CalendarStep(Sim sim)
    {
        var o = _out;
        if (o == null || o.Route <= 0 || o.Places.Count == 0) return;
        long t = sim.Tick;
        // 달력 짜기: 새 전이 궤도 (떠난 때가 바뀌었다) · 닿아 머문 뒤 한 번
        long key = o.VoyageTicks > 0 ? o.VoyageStart : -1 - o.ShipThetaTick;
        if (key != _calKey && t % Time.Minutes(10) == 7) BuildCalendar(sim, key);
        if (t % Time.Minutes(10) != 7) return;
        for (int i = 0; i < Calendar.Count; i++)
        {
            var e = Calendar[i];
            string name = EventName(e);
            bool zone = e.Zone >= 0;
            if ((e.Told & 1) == 0 && t < e.From && e.From - t <= Time.Days(NoticeDays) && e.From - t < Time.Hours(1)) e.Told |= 1;   // 곧 든다 — 예고는 건너뛰고 들 때 알린다
            if ((e.Told & 1) == 0 && t < e.From && e.From - t <= Time.Days(NoticeDays))
            {
                e.Told |= 1; Notices++;
                string line = zone ? $"{Days(e.From - t)} 뒤 {Ko.Euro(name)} 듭니다 — {Days(e.To - e.From)} 동안 · 파편에 대비합니다"
                                   : $"{Days(e.From - t)} 뒤 태양 합 — 고향과 통신이 {Days(e.To - e.From)} 끊깁니다 (편지 · 원격 진료는 그 전에)";
                Announce(sim, line, (float)((e.From - t) / (double)Time.TicksPerDay), zone ? 0 : 1);
            }
            if (zone && (e.Told & 2) == 0 && t >= e.From && t < e.To)
            {
                e.Told |= 2;
                Announce(sim, $"{Ko.Eul(name)} 지나기 시작했습니다 — {Days(e.To - t)} 동안 · 칸막이 문을 닫아 둡니다", 0, 0);
            }
            if (zone && (e.Told & 4) == 0 && (e.Told & 2) != 0 && t >= e.To)
            {
                e.Told |= 4;
                Announce(sim, $"{Ko.Eul(name)} 빠져나왔습니다", -1, 0);
            }
            if (!zone && (e.Told & 4) == 0 && t >= e.To) { e.Told |= 4; sim.Facts.Emit(t, FactKind.Ahead, -1, -1, 0, -1, 1); }   // 합이 끝났다 (방송은 통신이 한다)
            Calendar[i] = e;
            // 준비: 파편 구간 안이면 칸막이 문을 닫아 둔다
            if (zone && t >= e.From && t < e.To)
            {
                long before = ShelterDoorsShut;
                ShutInnerDoors();
                ZoneDoorsShut += ShelterDoorsShut - before;
            }
        }
    }

    private void Announce(Sim sim, string line, float size, int kind)
    {
        int c = sim.Causes.Add(CauseKind.Computer, "항해 달력: " + line, sim.Tick);
        sim.Facts.Emit(sim.Tick, FactKind.Ahead, -1, size, c, -1, kind);
        Broadcast(sim, line);   // 소리 단계 S6: 자막 + 알림음
    }

    private void BuildCalendar(Sim sim, long key)
    {
        var o = _out!;
        long t = sim.Tick;
        _calKey = key; CalBuilt++;
        var old = Calendar.ToList();
        Calendar.Clear();
        // 다시 짜도 같은 일(같은 구간 · 하루 안의 때)은 이미 알린 것을 잇는다 (두 번 알리지 않게)
        byte Kept(int zone, long from, long to) { foreach (var e in old) if (e.Zone == zone && e.From < to + Time.Days(1) && from < e.To + Time.Days(1)) return e.Told; return 0; }   // 겹치는 같은 일
        foreach (var s in o.ZoneSpans(t)) Calendar.Add(new CalEvent { From = s.From, To = s.To, Zone = s.Zone, Told = (byte)((s.From <= t ? 1 : 0) | Kept(s.Zone, s.From, s.To)) });
        long horizon = t + (long)(ConjAheadDays * Time.TicksPerDay);
        foreach (var s in o.ConjSpans(t, horizon)) Calendar.Add(new CalEvent { From = s.From, To = s.To, Zone = -1, Told = (byte)((s.From <= t ? 1 : 0) | Kept(-1, s.From, s.To)) });
        Calendar.Sort((a, b) => a.From.CompareTo(b.From));
    }

    /// <summary>아침 방송 — 이레 안에 다가오는 일 (구간은 지나고 있지 않을 때).</summary>
    private string AheadLine(long t)
    {
        foreach (var e in Calendar)
        {
            if (e.From <= t || e.From - t > Time.Days(MorningDays)) continue;
            return e.Zone >= 0 ? $"{Days(e.From - t)} 뒤 {Ko.Euro(EventName(e))} 듭니다" : $"{Days(e.From - t)} 뒤 태양 합으로 고향과 통신이 끊깁니다";
        }
        return "";
    }

    private void SaveCalendar(StateWriter w)
    {
        w.I32(Calendar.Count); foreach (var e in Calendar) { w.I64(e.From); w.I64(e.To); w.I32(e.Zone); w.Byte(e.Told); }
        w.I64(_calKey); w.I64(CalBuilt); w.I64(Notices); w.I64(ZoneDoorsShut);
    }
    private void LoadCalendar(StateReader r)
    {
        Calendar.Clear(); int n = r.I32();
        for (int i = 0; i < n; i++) Calendar.Add(new CalEvent { From = r.I64(), To = r.I64(), Zone = r.I32(), Told = r.Byte() });
        _calKey = r.I64(); CalBuilt = r.I64(); Notices = r.I64(); ZoneDoorsShut = r.I64();
    }
}
