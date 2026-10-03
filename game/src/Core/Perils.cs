using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.26 위험이 사람에게 닿는 길 (점검 항해: 열흘 세 시드에 사망 0 — 큰 사고가 사람에게 잘 닿지 않았다).
//   열 — 김 · 불 · 냉각 상실 · 우주 열로 달아오른 방에 오래 있으면 몸에 열이 쌓인다 (습하면 · 힘쓰면 · 약한 몸이면 더, 우주복은 막는다).
//        어지럽다 → 쓰러진다(열사병) → 그 방에 그대로 두면 숨진다. 서늘한 곳으로 옮겨지면 열이 빠지고 살아난다.
//   방사선 — 쌓인 피폭: 4Sv 넘으면 골수가 상해 기운이 빠지고(침대 · 의무실이면 덜), 7Sv 넘으면 몇 시간 안에 쓰러지고,
//        12Sv 넘으면 손쓸 길이 거의 없다. 태양 폭풍 세기는 그때그때 다르다 (Hazards.StormPeak) · 덮개가 안 내려간 창가는 더 쬔다.
//   늦게 아는 사람 — 잠든 사람은 냄새로는 잘 안 깬다 (몸으로 느끼는 숨 막힘 · 추위 · 열기는 깨운다) · 녹초면 더 늦다.
// 사람: 몸에 열이 오르면 그 방을 위험하다고 여겨 나간다 · 쓰러진 사람은 업어 옮긴다(이미 있는 길) · 기억 · 기록 · 연대기에 남는다.
// 주 컴퓨터: 데이터선이 닿는 방이면 생체 신호로 열사병 · 큰 피폭을 알아채 가장 가까운 사람을 부르고, 피폭이 큰 사람은 의무실로 부른다.
public sealed class PerilSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6421 + 1931));

    private readonly Dictionary<int, float> _heat = new();      // 몸에 쌓인 열 0~1.5
    private readonly Dictionary<int, long> _stroke = new();     // 열사병으로 쓰러진 때
    private readonly Dictionary<int, int> _radStage = new();    // 0 · 1(4Sv) · 2(7Sv) · 3(12Sv)
    private readonly Dictionary<int, long> _radSince = new();   // 7Sv를 넘은 때 (몇 시간 뒤 쓰러진다)
    private readonly HashSet<int> _woozy = new();               // "어지럽다" 한 번만
    private readonly HashSet<int> _paged = new();

    public int HeatStrokes, HeatDeaths, HeatRescued, RadSevere, RadCollapses, RadDeaths, LateWakes, Paged;

    public PerilSystem(World w) => _w = w;

    public float HeatOf(CrewMember c) => _heat.TryGetValue(c.Id, out var h) ? h : 0f;
    public bool Stroked(CrewMember c) => _stroke.ContainsKey(c.Id);
    public int RadStage(CrewMember c) => _radStage.TryGetValue(c.Id, out var s) ? s : 0;

    /// <summary>죽은 까닭 (World.Die): 열사병 · 방사선 병이 몸을 무너뜨렸다면 그 말로.</summary>
    public string? DeathCause(CrewMember c)
    {
        if (c.Vitals.Oxygen < 0.4f) return null; // 숨이 막혀 죽었다 (질식이 먼저)
        if (_stroke.ContainsKey(c.Id) && HeatOf(c) > 0.5f) return "열사병";
        if (c.Vitals.InjuryCause == "방사선 병" || RadStage(c) >= 2 && c.Vitals.Injury < 0.3f) return "방사선 병";
        return null;
    }

    /// <summary>통합6 골수가 무너진 몸(7Sv 넘고 고비를 못 넘김)은 저절로 · 침대에서 기운을 되찾지 못한다 (Needs 회복 배율).</summary>
    public float MarrowMul(CrewMember c)
    {
        int s = RadStage(c);
        if (s < 2 || _w.RadCare.Of(c)?.Stable == true) return 1f;
        return s >= 3 ? 0f : _w.RadCare.Treated(c) ? 0.1f : 0.05f; // 수액 · 영양을 받으면 조금은
    }

    /// <summary>위험 판단(EvacuateActivity.DangerHere): 몸에 열이 차오르면 그 방이 위험하다 (어지러워지기 전에 나간다).</summary>
    public float HeatDanger(CrewMember c)
    {
        float h = HeatOf(c);
        if (h < 0.3f || c.Room == null || c.Room.Air.Temperature < 36f) return 0f;
        return MathF.Min(0.95f, 0.25f + 0.6f * h);
    }

    /// <summary>
    /// 잠든 사람이 이 방 공기에 깨나 (World.SenseHazard): 숨 막힘 · 추위 · 열기 · 매운 가스는 몸이 깨운다,
    /// 연기 냄새만으로는 잘 안 깬다 (짙을수록 · 덜 지쳤을수록 빨리) — 감지기가 울리면 경보가 깨운다 (RaiseAlert).
    /// </summary>
    public bool WakesFromSleep(CrewMember c)
    {
        var air = c.Room!.Air;
        if (air.O2 < 16.5f || air.Temperature < 8f || air.Temperature > 40f || air.Toxin > 0.1f) return true;
        if (air.Smoke <= 0.15f) return false;
        float p = (0.004f + 0.03f * (air.Smoke - 0.15f)) * (c.Needs.Fatigue > 0.7f ? 0.4f : 1f) * (c.DeepAsleep ? 0.3f : 1f);
        if (R.Chance(p)) { LateWakes++; return true; }
        return false;
    }

    public void Update(float dt)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away) continue;
            Heat(c, dt);
            Radiation(c, dt);
        }
    }

    // ───────────────────────────── 열 ─────────────────────────────

    private void Heat(CrewMember c, float dt)
    {
        var w = _w;
        var room = c.Room;
        float t = room != null && !c.Outside ? room.Air.Temperature : 20f;
        bool suited = c.Suit is { Oxygen: > 0.05f };
        float steam = room != null && !c.Outside ? w.Piping.SteamAt(c.Cell) : 0f;
        float hot = suited ? MathF.Max(0f, t - 70f) : MathF.Max(0f, t - 40f) + 25f * steam;
        float h = HeatOf(c);
        if (hot > 0f)
        {
            float hum = room?.Humidity ?? 0.4f;
            float work = c.Pose == Pose.Working ? 1.5f : c.IsMoving ? 1.2f : 1f;
            h += hot / 18f * (0.7f + 0.6f * hum) * work * (1f + 0.5f * c.Vitals.Frailty) * dt * w.React.HeatMul(c); // v17.8 겉옷 · 선풍기 · 찬물이면 열이 덜 찬다
        }
        else if (h > 0f)
        {
            float cool = t < 30f ? 1.2f : 0.5f;
            if (c.CareBed != null || room?.Type == RoomType.Medbay) cool *= 2f; // 찬 수건 · 수액
            h = MathF.Max(0f, h - cool * dt);
        }
        h = MathF.Min(1.5f, h);
        if (h > 0f) _heat[c.Id] = h; else _heat.Remove(c.Id);

        if (h >= 0.55f && c.CanAct && _woozy.Add(c.Id))
        {
            c.Say(w, Persona.Say(c, "머리가 핑 돈다 — 여기 너무 뜨거워"));
            w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} 열기에 어지러워한다 ({room?.Name ?? "?"} {t:0}℃)", c.Id);
            c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1);
        }
        else if (h < 0.2f) _woozy.Remove(c.Id);

        // 열사병: 쓰러진다
        if (h >= 1f && !_stroke.ContainsKey(c.Id) && !c.Dead)
        {
            _stroke[c.Id] = w.Tick;
            HeatStrokes++;
            c.Vitals.InjuryCause = "열사병";
            c.Vitals.Health = MathF.Min(c.Vitals.Health, 0.11f);
            MarkLog.Add(c.Memory.Marks, w.Tick, $"열기에 쓰러졌다 ({room?.Name ?? "?"} {t:0}℃)");
            if (room != null) MarkLog.Add(room.Marks, w.Tick, $"{Ko.IGa(c.Name)} 열기에 쓰러졌다 ({t:0}℃)");
            w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} 열사병으로 쓰러졌다 — {room?.Name ?? "?"} {t:0}℃", c.Id);
            Page(c, "열사병으로 쓰러졌습니다", $"열사병 ({t:0}℃)");
        }
        if (!_stroke.TryGetValue(c.Id, out var since)) return;
        // 쓰러진 채: 뜨거운 곳에 그대로면 몸이 무너진다 · 서늘한 곳이면 버틴다
        if (h > 0.6f)
        {
            float drain = (t > 38f && !suited ? 0.35f : 0.06f) * dt;
            c.Vitals.Health = MathF.Max(w.CrewCanDie ? 0f : 0.02f, c.Vitals.Health - drain);
        }
        else if (h < 0.25f)
        {
            _stroke.Remove(c.Id);
            HeatRescued++;
            var by = c.CarriedBy ?? w.Crew.Where(o => o != c && o.CanAct && o.Room == c.Room).OrderBy(o => o.Id).FirstOrDefault();
            w.Log.Add(w.Tick, LogKind.Life, $"{c.Name}: 서늘한 곳에서 열이 내렸다", c.Id);
            if (by != null)
            {
                c.ChangeAffinity(by, 0.06f);
                MarkLog.Add(c.Memory.Marks, w.Tick, $"열사병 — {Ko.IGa(by.Name)} 끌어내 식혀 줬다");
            }
            w.History.Add(w, HistoryKind.Casualty, $"{Ko.IGa(c.Name)} 열사병에서 깨어났다 — 서늘한 곳으로 옮겨져 열이 내렸다 ({(w.Tick - since) / (float)SimTime.TicksPerHour * 60f:0}분)", c.Room, new[] { c });
        }
    }

    // ───────────────────────────── 방사선 ─────────────────────────────

    private void Radiation(CrewMember c, float dt)
    {
        var w = _w;
        float d = c.Dose;
        int stage = d >= 12f ? 3 : d >= 7f ? 2 : d >= 4f ? 1 : 0;
        int was = RadStage(c);
        if (stage > was)
        {
            _radStage[c.Id] = stage;
            if (stage >= 2) _radSince[c.Id] = w.Tick;
            RadSevere++;
            string feel = stage switch { 1 => "속이 뒤집힌다 · 기운이 하나도 없다", 2 => "토하고 · 피부가 붉게 달아오른다", _ => "손발이 떨리고 정신이 흐려진다" };
            w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} 방사선을 크게 쬐었다 — {d:0.0}Sv · {feel}", c.Id);
            MarkLog.Add(c.Memory.Marks, w.Tick, $"방사선 {d:0.0}Sv — {feel}");
            Memory.Shake(w, c, 0.08f * stage, "방사선을 크게 쬔 일");
            if (stage >= 1) w.Ailments.Catch(c, "radiation", null, $"피폭 {d:0.0}Sv");
            Page(c, stage >= 2 ? "방사선 피폭이 위험한 수준입니다" : "방사선을 크게 쬐었습니다", $"피폭 {d:0.0}Sv");
        }
        else if (stage < was && stage == 0) { _radStage.Remove(c.Id); _radSince.Remove(c.Id); }
        if (stage == 0) return;

        // 골수 · 장 · 신경: 피폭이 클수록 빨리 무너진다 (침대에 누워 수액 · 의무실이면 느리게)
        bool care = c.CareBed != null || c.Room?.Type == RoomType.Medbay && c.Pose is Pose.Sleeping or Pose.Down;
        // 통합6 12Sv 넘으면 하루 안에 무너진다 — 그래도 몇 시간은 버텨 손을 쓸 틈은 있다 (곁을 지키고 · 처치를 받고도 숨진다)
        float drain = stage switch { 3 => 0.075f + 0.025f * (d - 12f), 2 => 0.08f + 0.03f * (d - 7f), _ => 0.012f * (d - 3f) };
        if (care) drain *= stage == 3 ? 0.8f : stage == 2 ? 0.6f : 0.35f; // 통합6 누워 있어도 골수는 다시 피를 못 만든다
        drain *= w.RadCare.DrainMul(c); // 통합5 수액 · 골수 주사 · 수혈
        c.Vitals.Health = MathF.Max(w.CrewCanDie ? 0f : 0.02f, c.Vitals.Health - drain * dt);
        // 7Sv 넘고 몇 시간 — 토하고 기운이 빠져 쓰러진다 (그 전에 수액을 맞았으면 버틴다) · 12Sv 넘으면 몸이 스스로 무너질 때까지
        if (stage == 2 && !c.Down && !w.RadCare.Treated(c) && _radSince.TryGetValue(c.Id, out var at) && w.Tick - at > SimTime.Hours(2.5f))
        {
            RadCollapses++;
            c.Vitals.InjuryCause = "방사선 병";
            c.Vitals.Health = MathF.Min(c.Vitals.Health, 0.11f);
            _radSince[c.Id] = long.MaxValue / 2; // 한 번만
        }
    }

    // ───────────────────────────── 주 컴퓨터 ─────────────────────────────

    private void Page(CrewMember c, string what, string why)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline || c.Room is not Room room || !room.DataLinked || !room.Powered || !_paged.Add(c.Id * 4 + (what.Length & 3))) return;
        var near = w.Crew.Where(o => o != c && o.CanAct && !o.Outside && o.Room != null && o.Pose != Pose.Sleeping)
            .OrderBy(o => (o.Position - c.Position).LengthSquared()).ThenBy(o => o.Id).FirstOrDefault();
        Paged++;
        a.Speak.Announce(a.Voice.Style($"{room.Name} — {Ko.IGa(c.Name)} {what}" + (near != null ? $". {near.Name}, 가장 가깝습니다 — 가 주세요" : "")), room, 1);
        a.Book.Add(ActKind.Shed, null, $"{c.Name} 생체 신호 — {why}", near != null ? $"가장 가까운 {Ko.EulReul(near.Name)} 불렀다" : "부를 사람이 없다",
            "방송으로 불렀다", "", "peril", SimTime.Hours(2), 15f,
            (world, act) => world.Crew.FirstOrDefault(x => x.Id == c.Id) is { Dead: false } ? (1, "맞았다 — 살았다") : (-1, "늦었다 — 숨졌다"));
        if (near != null) near.NextThinkTick = Math.Min(near.NextThinkTick, w.Tick + 1);
        w.Board.RequestScan();
    }

    /// <summary>World.Die 뒤: 왜 늦었는지 연대기에 (혼자 · 잠든 시간 · 뜨거운 방에 남겨졌다).</summary>
    public void OnDeath(CrewMember c)
    {
        var w = _w;
        string? cause = DeathCause(c);
        if (cause == null) return;
        if (cause == "열사병") HeatDeaths++; else RadDeaths++;
        int awake = w.Crew.Count(o => !o.Dead && o != c && o.Pose != Pose.Sleeping && o.CanAct);
        string why = cause == "열사병"
            ? (w.Crew.Any(o => !o.Dead && o != c && o.Room == c.Room && o.CanAct) ? "곁에 있던 사람도 열기에 버티지 못했다" : awake <= 1 ? "깨어 있는 사람이 없었다" : "뜨거운 방에서 아무도 끌어내지 못했다")
            : w.RadCare.DeathNote(c) ?? (c.Outside ? "선체 밖에서 쬐었다" : "피할 곳에 닿지 못하고 쬐었다"); // 통합5 손을 써 봤는지
        w.History.Add(w, HistoryKind.Death, $"{Ko.IGa(c.Name)} {cause}으로 숨졌다 — {why}", c.Room, new[] { c });
        foreach (var o in w.Crew)
            if (!o.Dead && o != c && o.AffinityTo(c) > 0.3f) MarkLog.Add(o.Memory.Marks, w.Tick, $"{c.Name} — {cause} · {why}");
        _stroke.Remove(c.Id);
        _heat.Remove(c.Id);
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(HeatStrokes); I(HeatDeaths); I(HeatRescued); I(RadSevere); I(RadCollapses); I(LateWakes); I(Paged);
        foreach (var k in _heat.Keys.OrderBy(x => x)) { I(k); F(_heat[k]); }
    }
}
