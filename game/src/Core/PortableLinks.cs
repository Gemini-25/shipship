using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.7 이동식 장비 × 주 컴퓨터 · 냄새 · 배 본체 · 음식 — 규칙이 맞물리는 곳.
// 주 컴퓨터는 배전반 계측으로 회로(콘센트) 부하만 안다 — 무엇이 꽂혔는지는 모른다:
//   과부하를 예측해 방송하고(들은 사람이 차단기 전에 뽑는다) · 문 감지기로 빈 방인데 콘센트 부하가 오래 나가면 켜 두고 간 히터로 짚고(가서 끈다) ·
//   창고 충전대의 빈 자리로 오래 안 돌아온 장비를 알린다(잊은 사람이 떠올리고 돌려놓는다). 배터리로 돌리는 장비 · 데이터선이 끊긴 방은 컴퓨터가 못 본다.
// 냄새: 창고에 오래 둔 히터를 켜면 열선 먼지가 타는 냄새 · 과부하로 달아오른 콘센트는 피복 타는 냄새 — 냄새를 따라온 사람이 까닭을 알아채고 다르게 한다.
// 배 본체: 양수기 호스 끝은 바닥을 적시고(미끄럽다 · 그 칸 케이블엔 물이 스민다) · 히터가 데운 방은 서리가 녹아 물웅덩이(→ 케이블) ·
//   카트 바퀴는 바닥을 닳게 한다 · 카펫 · 고무 바닥의 히터는 불이 잘 붙는다(Update).
// 음식: 정전된 식당에서 식은 접시는 켜 둔 히터 곁에 대 데워 먹는다 (히터가 데운 방은 냄비 · 접시도 덜 식는다 — Cooking이 방 온도를 읽는다).
public sealed partial class PortableSystem
{
    private readonly long[] _warnAt = Enumerable.Repeat(-1L, PowerGrid.CircuitCount).ToArray();
    private readonly long[] _tripAt = Enumerable.Repeat(-1L, PowerGrid.CircuitCount).ToArray();
    private readonly string[] _warnWhy = Enumerable.Repeat("", PowerGrid.CircuitCount).ToArray();
    private readonly Dictionary<int, long> _alone = new();
    private readonly HashSet<int> _flagged = new(), _heaterOff = new(), _recalled = new();
    private long _nextInventory = SimTime.Hours(6);

    /// <summary>회로 과부하를 사람들이 안다 (주 컴퓨터 방송을 들었거나 · 냄새를 따라와 뜨거운 콘센트를 만져 봤다) — 한 시간 동안.</summary>
    public bool Warned(int circuit) => circuit >= 0 && circuit < _warnAt.Length && _warnAt[circuit] >= 0 && _w.Tick - _warnAt[circuit] < SimTime.Hours(1);

    /// <summary>주 컴퓨터가 끄라고 짚은 히터 (빈 방 · 콘센트 부하).</summary>
    public bool Flagged(PortableDevice d) => _flagged.Contains(d.Id);

    // v16.20 같은 원인으로 또 떨어진 회로: 주 컴퓨터가 차단기를 붙잡고, 가까운 사람 하나를 콕 집어 뽑아 달라고 했다 (한 시간).
    private readonly Dictionary<int, (int crewId, long tick)> _askedUnplug = new();

    /// <summary>주 컴퓨터 지시: 이 회로 콘센트에서 장비를 빼 달라 (그 사람이 먼저 · 뛰어간다).</summary>
    public void AskUnplug(int circuit, CrewMember who)
    {
        if (circuit < 0 || circuit >= PowerGrid.CircuitCount) return;
        _askedUnplug[circuit] = (who.Id, _w.Tick);
        _nextScan = 0;
    }

    /// <summary>그 회로를 뽑아 달라고 콕 집힌 사람 (한 시간 안 · 살아 있으면).</summary>
    public CrewMember? AskedUnplug(int circuit)
    {
        if (!_askedUnplug.TryGetValue(circuit, out var a) || _w.Tick - a.tick > SimTime.Hours(1)) return null;
        foreach (var c in _w.Crew) if (c.Id == a.crewId) return c.Dead ? null : c;
        return null;
    }

    /// <summary>빛을 내는 장비 (다음 단계의 2D 조명 · 화면이 읽는다): 위치 LightPos · 반경 LightRadius · 세기 LightIntensity · 색 LightColor · 방향 Aim.</summary>
    public IEnumerable<PortableDevice> Lights => Devices.Where(d => !d.Lost && d.LightIntensity > 0.01f);

    /// <summary>그 회로에서 이동식 장비 부하가 가장 많이 걸린 콘센트의 방.</summary>
    public Room? OutletRoom(int circuit)
    {
        Room? best = null;
        float bk = 0f;
        foreach (var d in Devices)
        {
            if (!d.Placed || !d.On || d.Plug != PortablePlug.Outlet || d.Outlet is not Room o || o.Circuit != circuit) continue;
            float k = 0f;
            foreach (var x in Devices) if (x.Placed && x.On && x.Plug == PortablePlug.Outlet && x.Outlet == o) k += x.Spec.Kw;
            if (k > bk || k == bk && best != null && o.Id < best.Id) { bk = k; best = o; }
        }
        return best;
    }

    // ───────────────────────── 시스템 틱 ─────────────────────────

    private void Links(float dt)
    {
        var w = _w;
        // 히터 먼지: 창고에 두면 쌓이고 (이틀이면 가득) · 켜면 25분쯤 타며 냄새를 낸다 (Smell이 묻는다)
        foreach (var d in Devices)
        {
            if (d.Kind != PortableKind.Heater) continue;
            if (d.Stored) d.Dust = MathF.Min(1f, d.Dust + dt / 48f);
            else if (d.Running) d.Dust = MathF.Max(0f, d.Dust - dt / 0.45f);
        }
        BodyLinks();
        if (w.Automation.Present && w.Automation.MainOnline) ComputerWatch();
        HotOutletGlance(dt);
    }

    /// <summary>지금 부하로 차단기가 떨어질 때까지 (분) — 주 컴퓨터의 예측 · 화면.</summary>
    public float TripMinutes(int circuit)
    {
        if (circuit < 0 || circuit >= _over.Length) return float.PositiveInfinity;
        float r = CircuitLoad[circuit] / OutletCapKw - 1f;
        return r <= 0f ? float.PositiveInfinity : (1f - _over[circuit]) / (r * r * BreakerHeat) * 60f;
    }

    /// <summary>멀티탭 · 플러그가 달아오른 정도 (0~1.5) — 화면이 그린다.</summary>
    public float OutletHeat(int circuit) => circuit >= 0 && circuit < _hot.Length ? _hot[circuit] : 0f;

    /// <summary>냄새 × 승무원 (같은 방): 탄내를 맡은 사람은 밥을 먹다가도 둘러본다 — 달아오른 멀티탭을 보면 일어나 뽑고, 먼지 타는 히터면 까닭을 안다.</summary>
    private void HotOutletGlance(float dt)
    {
        var w = _w;
        for (int i = 0; i < PowerGrid.CircuitCount; i++)
        {
            if (CircuitLoad[i] <= OutletCapKw || _hot[i] < 0.2f || ProjectedKw(i) <= OutletCapKw || OutletRoom(i) is not Room o) continue;
            if (GlanceBy(o, dt) is CrewMember c) SmellFound(c, o, here: true);
        }
        foreach (var d in Devices) // 먼지 타는 히터: 한 번 알아채면 그 히터는 다시 둘러보지 않는다 (창고에 들어가면 잊는다)
        {
            if (d.Kind != PortableKind.Heater) continue;
            if (d.Stored) { _dustSeen.Remove(d.Id); continue; }
            if (!d.Running || d.Dust <= 0.05f || _dustSeen.Contains(d.Id) || RoomOf(d) is not Room r) continue;
            if (GlanceBy(r, dt) is not CrewMember c) continue;
            _dustSeen.Add(d.Id);
            SmellFound(c, r, here: true);
        }
    }

    private readonly HashSet<int> _dustSeen = new();

    /// <summary>그 방에서 탄내를 맡은 사람 하나가 둘러본다 (10분쯤 안에 · 꼼꼼하면 빨리).</summary>
    private CrewMember? GlanceBy(Room o, float dt)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Room != o || !c.CanAct || !c.IsAwake || c.Job?.Urgent == true || c.Suit != null) continue;
            if (w.Smells.Smelled(c, SmellKind.Burnt, SimTime.Minutes(20)) is null) continue; // 냄새를 맡은 사람만 둘러본다
            if (!R.Chance(MathF.Min(1f, (4f + 4f * c.Traits.Diligence) * dt))) continue;
            c.Interrupt(w);
            return c;
        }
        return null;
    }

    private void BodyLinks()
    {
        var w = _w;
        var body = w.Body;
        var grid = w.Ship.Grid;
        foreach (var d in Devices)
        {
            // 양수기: 호스 끝(배수구) 둘레가 젖는다 — 미끄럽고, 그 칸에 놓인 케이블엔 물이 스민다
            if (d.Kind == PortableKind.Pump && d.Running && d.Placed && d.HoseTo is Vector2 h && RoomOf(d) is { Flood: > 0f })
            {
                var hc = Cell.FromPosition(h);
                if (grid.InBounds(hc) && w.Ship.IsWalkable(hc) && body.Mark(hc, CellMark.Wet) < 0.45f) { body.RaiseMark(hc, CellMark.Wet, 0.55f, "양수기 호스 물"); Stats.HoseWets++; }
            }
            // 카트: 밀고 다니는 바퀴가 바닥을 닳게 한다 (닳은 바닥은 미끄럽다)
            if (d.Kind == PortableKind.Cart && d.HeldBy is CrewMember p && p.IsMoving && grid.InBounds(p.Cell))
            {
                int i = grid.Index(p.Cell);
                if (i < body.Wear.Length) body.Wear[i] = MathF.Min(1f, body.Wear[i] + BodySystem.WearPerStep * 3f);
            }
        }
    }

    // ───────────────────────── 주 컴퓨터 ─────────────────────────

    private void ComputerWatch()
    {
        var w = _w;
        var au = w.Automation;
        // 1) 과부하 예측: 배전반이 재는 콘센트 부하 (무엇이 꽂혔는지는 모른다)
        for (int i = 0; i < PowerGrid.CircuitCount; i++)
        {
            if (_hot[i] < 0.06f || OutletRoom(i) is not Room room) continue; // 사람이 이미 알아챘는지는 모른다 (같은 경고는 30분에 한 번)
            float load = CircuitLoad[i], pct = load / OutletCapKw * 100f;
            int ci = i;
            float eta = TripMinutes(i);
            var act = au.Book.Add(ActKind.Advice, room, $"{PowerGrid.CircuitName(i)} 회로 {room.Name} 콘센트 부하 {load:0.0}kW (견딤 {OutletCapKw:0.0}kW · {pct:0}%)",
                $"예측: {(eta < 90f ? $"{eta:0}분쯤 뒤" : "한참 뒤")} 차단기가 떨어진다 · 그 전에 멀티탭이 달아오른다 · 원인 추정: 콘센트에 꽂은 장비가 많다", "선내 방송", $"{room.Name} 콘센트에서 하나를 뽑으라",
                $"pw:over:{i}", SimTime.Minutes(30), 25f, (world, a) => GradeOver(ci, a));
            if (act == null) continue;
            Stats.ComputerWarns++;
            var b = au.Speak.Announce(au.Voice.Style($"{PowerGrid.CircuitName(i)} 회로 콘센트 부하 {pct:0}% — 곧 차단기가 떨어진다. {room.Name}에서 하나를 뽑으라"), room, 1);
            if (b is not { HeardBy.Count: > 0 }) continue; // 아무도 못 들었다 (잠 · 스피커)
            if (!Warned(i)) _warnWhy[i] = $"주 컴퓨터가 콘센트 부하 {pct:0}%를 알렸다";
            _warnAt[i] = w.Tick;
            _nextScan = w.Tick; // 들은 사람이 곧 움직인다
            Rethink(b);
        }

        // 2) 빈 방 히터: 문 감지기로는 아무도 없는데 콘센트로 큰 부하가 오래 나간다 (배터리로 돌리면 못 본다)
        foreach (var d in Devices)
        {
            if (d.Kind != PortableKind.Heater) continue;
            if (!d.Running || !d.Placed || d.Plug != PortablePlug.Outlet || d.Outlet is not Room o || RoomOf(d) is not Room r || au.Belief.PeopleIn(r) is not 0)
            {
                _alone.Remove(d.Id);
                continue;
            }
            if (!_alone.TryGetValue(d.Id, out var since)) { _alone[d.Id] = w.Tick; continue; }
            if (w.Tick - since < SimTime.Minutes(20) || _flagged.Contains(d.Id)) continue;
            int id = d.Id;
            float minutes = (w.Tick - since) / (float)SimTime.Minutes(1);
            var act = au.Book.Add(ActKind.Advice, r, $"{r.Name} — 문 감지기로는 아무도 없는데 {o.Name} 콘센트로 {d.Spec.Kw * d.Supply:0.0}kW가 {minutes:0}분째 나간다 · 방 {r.Air.Temperature:0}℃",
                "원인 추정: 켜 두고 간 히터 · 예측: 침구 곁이면 불이 날 수 있다", "선내 방송", $"{r.Name}에 가서 끄라",
                $"pw:heat:{d.Id}", SimTime.Hours(2), 45f, (world, a) => GradeHeater(id, r, a));
            if (act == null) continue;
            Stats.HeaterWarns++;
            var b = au.Speak.Announce(au.Voice.Style($"{r.Name}에 아무도 없는데 콘센트로 전기가 계속 나간다 — 켜 둔 히터 같다. 불이 나기 전에 끄라"), r, 1);
            if (b is { HeardBy.Count: > 0 }) { _flagged.Add(d.Id); _nextScan = w.Tick; Rethink(b); }
        }

        // 3) 창고 충전대의 빈 자리: 하루 넘게 안 돌아온 장비 (어디 있는지는 콘센트에 꽂힌 것만 안다)
        if (w.Tick >= _nextInventory)
        {
            _nextInventory = w.Tick + SimTime.Hours(6);
            var away = Devices.Where(d => d.Placed && d.Kind != PortableKind.Cart && d.PlacedSince >= 0 && w.Tick - d.PlacedSince > SimTime.TicksPerDay).ToList();
            if (away.Count == 0) return;
            string what = string.Join(" · ", away.GroupBy(d => d.Kind).OrderBy(g => g.Key).Select(g => $"{g.First().Name} {g.Count()}개"));
            var seen = away.Where(d => d.Plug == PortablePlug.Outlet && d.Outlet != null && RoomOf(d) != null).Select(d => RoomOf(d)!.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            var ids = away.Select(d => d.Id).ToList();
            var act = au.Book.Add(ActKind.Advice, null, $"창고 충전대 빈 자리 {away.Count} — {what} · 하루 넘게 안 돌아왔다",
                seen.Count > 0 ? $"콘센트 부하로 보이는 곳: {string.Join(" · ", seen)} · 나머지는 어디 있는지 모른다" : "어디 있는지는 모른다 (콘센트에 꽂혀 있지 않다)",
                "선내 방송", "다 쓴 장비는 창고로", "pw:inv", SimTime.Hours(5), 240f, (world, a) => GradeInventory(ids));
            if (act == null) return;
            Stats.InventoryCalls++;
            var b = au.Speak.Announce(au.Voice.Style($"창고 충전대에 {what}이 하루 넘게 비었다 — 다 쓴 장비는 창고로 돌려놓으라"), null, 0);
            if (b is not { HeardBy.Count: > 0 }) return;
            foreach (var d in away)
            {
                if (!d.Forgotten) continue;
                d.Forgotten = false;
                d.WillForget = false;
                Stats.Found++;
                Stats.InventoryFound++;
                _recalled.Add(d.Id);
                var who = w.Crew.FirstOrDefault(x => x.Id == d.InstalledBy);
                w.Log.Add(w.Tick, LogKind.Life, $"방송을 듣고 {(who != null ? Ko.IGa(who.Name) + " " : "")}{RoomOf(d)?.Name ?? "?"}에 두고 잊은 {Ko.EulReul(d.Name)} 떠올렸다 — 창고에 돌려놓는다", d.InstalledBy);
            }
            _nextScan = w.Tick;
        }
    }

    /// <summary>방송을 들은 사람은 곧 하던 일을 다시 따져 본다.</summary>
    private void Rethink(Broadcast b)
    {
        foreach (var c in _w.Crew) if (b.HeardBy.Contains(c.Id)) c.NextThinkTick = Math.Min(c.NextThinkTick, _w.Tick + 1);
    }

    private (int, string)? GradeOver(int i, ComputerAct a) =>
        _tripAt[i] > a.Tick ? (2, "경고했지만 차단기가 떨어졌다 — 사람이 늦었다")
        : CircuitLoad[i] <= OutletCapKw ? (1, "맞았다 — 차단기가 떨어지기 전에 뽑았다")
        : (-1, "부하가 그대로다 — 아무도 뽑지 않았다");

    private (int, string)? GradeHeater(int id, Room r, ComputerAct a)
    {
        if (_heaterOff.Contains(id)) return (1, "맞았다 — 가 보니 켜 두고 간 히터였다 (껐다)");
        if (_w.Fire.CountIn(r) > 0) return (1, $"경고대로 {r.Name}에 불이 났다");
        var d = Devices.FirstOrDefault(x => x.Id == id);
        return d is { Running: true } ? (2, "아직 켜져 있다 — 아무도 가지 않았다") : (2, "꺼졌다 (사람이 돌아왔거나 전기가 끊겼다)");
    }

    private (int, string)? GradeInventory(List<int> ids)
    {
        int back = Devices.Count(d => ids.Contains(d.Id) && d.Stored);
        return back > 0 ? (1, $"{back}개가 창고로 돌아왔다") : (2, "아직 아무것도 안 돌아왔다 (쓰는 중일 수 있다)");
    }

    /// <summary>Scan: 주 컴퓨터가 짚은 빈 방 히터를 끄러 간다 (방송을 들은 사람들).</summary>
    private void ComputerNeeds()
    {
        foreach (var d in Devices)
        {
            if (!_flagged.Contains(d.Id)) continue;
            if (!d.Placed || !d.On) { _flagged.Remove(d.Id); continue; }
            if (RoomOf(d) is Room r)
                Need(PortableTask.Unplug, d.Kind, r, d.At, false, $"주 컴퓨터: 아무도 없는 {r.Name}에 켜 둔 히터 — 불이 날 수 있다", 0.62f, $"cpuheat:{d.Id}", device: d);
        }
    }

    /// <summary>주 컴퓨터 방송으로 떠올린 장비 (회수가 조금 더 급하다).</summary>
    public bool Recalled(PortableDevice d) => _recalled.Contains(d.Id);

    /// <summary>짚인 히터를 끈다 (Unplug 일의 끝): 가 보니 아무도 없는 방에 켜 둔 히터.</summary>
    private bool TurnOffFlagged(CrewMember c, PortableDevice d)
    {
        if (!_flagged.Remove(d.Id)) return false;
        var w = _w;
        var r = RoomOf(d);
        bool forgot = d.Forgotten;
        d.On = false;
        d.Running = false;
        d.Plug = PortablePlug.None;
        d.Outlet = null;
        d.CableTo = null;
        d.Purpose = null;
        d.Forgotten = false;
        _heaterOff.Add(d.Id);
        Stats.HeaterOffs++;
        c.Say(w, Persona.Say(c, "아무도 없는데 히터가 켜져 있었네"));
        w.Log.Add(w.Tick, LogKind.Work, $"주 컴퓨터 방송을 듣고 {r?.Name ?? "?"}에 가 보니 {(forgot ? "누군가 잊고 간 " : "")}히터가 켜져 있었다 — 껐다" + (Flammable(d.At) ? " (침구 곁이었다)" : ""), c.Id);
        if (r != null) MarkLog.Add(r.Marks, w.Tick, $"{c.Name}: 빈 방 히터 끔 (주 컴퓨터 경고)");
        return true;
    }

    // ───────────────────────── 음식 ─────────────────────────

    /// <summary>정전된 식당에서 식은 접시를 켜 둔 히터 곁에 대 데운다 (조금 덜 서운하다).</summary>
    private void WarmPlate(CrewMember c, Room room)
    {
        if (!_w.Cooking.EatingCold(c) || !RunningIn(room, PortableKind.Heater)) return;
        Stats.WarmPlates++;
        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.02f);
        if (_gatherLog.TryGetValue(-1 - room.Id, out var last) && _w.Tick - last < SimTime.Hours(3)) return;
        _gatherLog[-1 - room.Id] = _w.Tick;
        _w.Log.Add(_w.Tick, LogKind.Life, $"정전된 {room.Name} — 식은 접시를 켜 둔 히터 곁에 대 데워 먹었다", c.Id);
    }

    // ───────────────────────── 냄새 ─────────────────────────

    /// <summary>냄새 훅 (Smell.Sources): 먼지 타는 히터 · 과부하로 달아오른 콘센트.</summary>
    public void AddSmells(SmellSystem s)
    {
        foreach (var d in Devices)
            if (d.Kind == PortableKind.Heater && d.Running && d.Dust > 0.05f && RoomOf(d) is Room r) s.Emit(r, SmellKind.Burnt, 0.05f + 0.15f * d.Dust);
        for (int i = 0; i < PowerGrid.CircuitCount; i++)
            if (CircuitLoad[i] > OutletCapKw && _hot[i] > 0.1f && OutletRoom(i) is Room o) s.Emit(o, SmellKind.Burnt, 0.06f + 0.3f * MathF.Min(1f, _hot[i]));
    }

    /// <summary>냄새 훅 (Smell.Inspect): 탄내를 따라온 사람이 이동식 장비에서 까닭을 찾는다 — 찾았으면 true.</summary>
    public bool SmellFound(CrewMember c, Room room) => SmellFound(c, room, false);

    private bool SmellFound(CrewMember c, Room room, bool here)
    {
        var w = _w;
        for (int i = 0; i < PowerGrid.CircuitCount; i++)
        {
            if (CircuitLoad[i] <= OutletCapKw || ProjectedKw(i) <= OutletCapKw || OutletRoom(i) != room) continue; // 먼저 온 사람이 이미 뽑았으면 식는 중
            Stats.HotOutlets++;
            if (!_learned[i] && !Warned(i)) { _warnAt[i] = w.Tick; _warnWhy[i] = $"{Ko.IGa(c.Name)} 콘센트가 달아오른 걸 알아챘다"; }
            _nextScan = w.Tick;
            c.Say(w, Persona.Say(c, "콘센트가 뜨겁다 — 너무 많이 꽂았어"));
            w.Log.Add(w.Tick, LogKind.Warning, here
                ? $"{Ko.IGa(c.Name)} {room.Name}에서 피복 타는 냄새에 둘러보니 멀티탭이 달아올라 있었다 — 이동식 장비를 너무 많이 꽂았다 ({CircuitLoad[i]:0.0}kW)"
                : $"{Ko.IGa(c.Name)} 탄 냄새를 따라와 {room.Name} 콘센트가 달아오른 걸 찾았다 — 이동식 장비를 너무 많이 꽂았다 ({CircuitLoad[i]:0.0}kW)", c.Id);
            MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: 콘센트 타는 냄새 ({PowerGrid.CircuitName(i)} 회로)");
            // 그 자리에서 가장 큰 것 하나를 뽑는다 (다른 회로 콘센트에 여유가 있으면 옮겨 꽂는다)
            var big = Devices.Where(d => d.Placed && d.On && d.Plug == PortablePlug.Outlet && d.Outlet == room).OrderByDescending(d => d.Spec.Kw).ThenBy(d => d.Id).FirstOrDefault();
            if (big != null) Unplug(c, big);
            return true;
        }
        var h = Devices.FirstOrDefault(d => d.Kind == PortableKind.Heater && d.Running && d.Dust > 0.02f && RoomOf(d) == room);
        if (h == null) return false;
        Stats.DustSniffs++;
        c.Say(w, Persona.Say(c, "히터 먼지 타는 냄새였네"));
        bool moved = false;
        if (Flammable(h.At))
        {
            var to = room.Cells.Where(x => w.Ship.IsOpenFloor(x) && !Occupied(x) && !Flammable(x)).OrderBy(x => (x.Center - h.At.Center).LengthSquared()).ThenBy(x => x.Y).ThenBy(x => x.X).Cast<Cell?>().FirstOrDefault();
            if (to is Cell t) { h.At = t; moved = true; }
        }
        _dustSeen.Add(h.Id);
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {(here ? "탄 냄새에 둘러보니" : "탄 냄새를 따라와 보니")} 오래 둔 히터 열선의 먼지가 타는 냄새였다" + (moved ? " — 침구 곁이라 조금 옮겨 놓았다" : ""), c.Id);
        return true;
    }
}
