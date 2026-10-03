using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// 의료 3차 — 주컴퓨터 원격 의료: 데이터선이 닿는 침대 · 기계의 생체 신호를 읽고 손을 댄다.
//   투여 펌프: 아픈 사람에게 진통제를 조금씩 나눠 넣는다 (한 번에 먹는 것보다 덜 길든다) · 의존이 쌓이면 줄여 간다.
//   인공 장기 기계: 전기가 끊겨 내장 전지로 돌면 흐름을 낮춰 전지를 늘린다 · 산소가 모자라면 인공 폐 흐름을 올린다.
//   진단 보조: 의무관이 치료할 때 생체 기록으로 무엇인지 짚어 준다 (치료가 더 잘 듣는다) · 체력이 한 시간 새 떨어지면 알린다.
//   데이터선이 끊긴 방 · 컴퓨터가 멎으면 못 한다 — 사람이 직접 봐야 한다.
public sealed class TelemedSystem
{
    private readonly World _w;
    private readonly SortedDictionary<int, float> _last = new();   // 사람 → 한 시간 전 체력
    private readonly SortedDictionary<int, long> _told = new();
    private readonly SortedSet<long> _diag = new();
    public int PumpDoses, Tapered, EcoTicks, FlowUps, Diagnoses, Watches;

    public TelemedSystem(World w) => _w = w;

    private bool Online => _w.Automation.Present && _w.Automation.CoreOnline;

    /// <summary>이 사람이 데이터선이 닿는 침대(치료 침대 · 수술대 · 간이침대)에 누워 있다 — 컴퓨터가 펌프를 만진다.</summary>
    public bool PumpAt(CrewMember c)
    {
        var w = _w;
        if (!Online || c.Room is not Room r || !r.DataLinked || !r.Powered) return false;
        if (c.CareBed is Furniture b && (b.Cells.Contains(c.Cell) || (b.Center - c.Position).LengthSquared() < 2.5f)) return true;
        if (w.Surgery.OnTable(c)) return true;
        return w.Recovery.WardBed.TryGetValue(c.Id, out var id) && id < w.Ship.Furniture.Count && (w.Ship.Furniture[id].Center - c.Position).LengthSquared() < 2.5f;
    }

    /// <summary>진단 보조 (Ailments.Treated가 부른다): 생체 기록으로 짚어 주면 약이 더 잘 듣는다.</summary>
    public float Diagnose(CrewMember pt, CrewMember doctor, string id)
    {
        var w = _w;
        if (!Online || pt.Room is not Room r || !r.DataLinked || r.Type != RoomType.Medbay && pt.CareBed == null) return 1f;
        Diagnoses++;
        int ih = 0; foreach (char ch in id) ih = unchecked(ih * 31 + ch);
        long key = (long)pt.Id * 1_000_003L + (ih & 0xffff) + w.Tick / SimTime.TicksPerDay * 7919L;
        if (_diag.Add(key))
        {
            w.Log.Add(w.Tick, LogKind.Work, $"주컴퓨터가 {pt.Name}의 생체 기록을 {doctor.Name}에게 띄웠다 — 열 · 맥 · 피 검사로 짚었다", doctor.Id);
            if (_diag.Count > 200) _diag.Remove(_diag.Min);
        }
        return 1.15f;
    }

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick % SimTime.Minutes(10) >= World.SystemInterval) return;
        if (!Online) return;
        var a = w.Automation;
        Organs();
        bool hour = w.Tick % SimTime.Hours(1) < World.SystemInterval;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.Outside || !PumpAt(c)) { if (hour) _last.Remove(c.Id); continue; }
            Pain(c);
            if (!hour) continue;
            // 체력이 한 시간 새 크게 떨어진다 — 알린다
            float h = c.Vitals.Health;
            if (_last.TryGetValue(c.Id, out var prev) && h < prev - 0.06f && Tell(c.Id * 4 + 1, SimTime.Hours(2)))
            {
                Watches++;
                string why = w.Surgery.Internal(c) ? "배 속에서 피가 새는 것 같다" : w.Casualty.Of(c) != null ? "상처에서 피가 계속 난다" : c.Ailments.Count > 0 ? $"{c.Ailments[0].Id switch { "sepsis" => "피에 균이 돈다", "woundinf" => "상처가 곪는다", _ => "앓는 것이 깊어진다" }}" : "까닭을 더 봐야 한다";
                a.Speak.Announce(a.Voice.Style($"{c.Name} 생체 신호가 나빠집니다 — 한 시간 새 체력 {prev * 100:0}% → {h * 100:0}% · {why}"), c.Room, 2);
                a.Book.Add(ActKind.Alarm, c.Room, $"{c.Name} 체력 {prev * 100:0}% → {h * 100:0}% (한 시간)", why, "의무관을 불렀다", "의무관: 침대 곁으로", $"tele:watch:{c.Id}", SimTime.Hours(2));
                foreach (var m in w.Crew) if (m.Role == CrewRole.Medic && m.CanAct && !m.Outside) m.NextThinkTick = Math.Min(m.NextThinkTick, w.Tick + 1);
            }
            _last[c.Id] = h;
        }
    }

    /// <summary>투여 펌프: 진통제를 조금씩 (의존이 쌓이면 줄여 간다).</summary>
    private void Pain(CrewMember c)
    {
        var w = _w;
        var a = w.Automation;
        if (c.Vitals.Injury < 0.3f && c.Needs.Stress < 0.75f) return;
        float dep = w.Pharmacy.Dependence.GetValueOrDefault(c.Id);
        if (dep >= 0.45f)
        {
            if (Tell(c.Id * 4 + 2, SimTime.Hours(12)))
            {
                Tapered++;
                a.Book.Add(ActKind.Advice, c.Room, $"{c.Name} 진통제 의존 {dep * 100:0}%", "더 넣으면 끊기 어렵다", "투여 펌프를 줄여 간다", "", $"tele:taper:{c.Id}", SimTime.Hours(12));
            }
            return;
        }
        if (!w.Pharmacy.Painkill(c, null, "투여 펌프")) return;
        PumpDoses++;
        // 조금씩 나눠 넣었다 — 한 번에 먹는 것보다 덜 길든다
        w.Pharmacy.Dependence[c.Id] = MathF.Max(dep, w.Pharmacy.Dependence.GetValueOrDefault(c.Id) - 0.05f);
        if (Tell(c.Id * 4 + 3, SimTime.Hours(6)))
            w.Log.Add(w.Tick, LogKind.Work, $"주컴퓨터가 {(c.CareBed?.Name ?? "침대")} 투여 펌프로 {c.Name}에게 진통제를 조금씩 넣는다", c.Id);
    }

    /// <summary>인공 장기 기계를 원격으로: 내장 전지면 흐름을 낮추고 · 산소가 모자라면 올린다.</summary>
    private void Organs()
    {
        var w = _w;
        var a = w.Automation;
        foreach (var b in w.Organs.Bodies)
        {
            if (!b.Hooked || w.Organs.Machine(b) is not Furniture f || !f.Room.DataLinked || f.Machine is not Machine m) continue;
            var c = w.Crew.FirstOrDefault(x => x.Id == b.Crew);
            if (c == null || c.Dead) continue;
            if (!m.Powered && w.Organs.Cell(f) > 0f && w.Organs.Cell(f) < 1f)
            {
                float cap = f.Type == FurnitureType.Dialyzer ? 0.5f : 1f;
                w.Organs.Eco(f, 0.35f * (SimTime.Minutes(10) / (float)SimTime.TicksPerHour) / cap);
                EcoTicks++;
                if (Tell(f.Id * 4 + 1, SimTime.Hours(1)))
                    a.Book.Add(ActKind.Module, f.Room, $"{f.Name} 내장 전지 {w.Organs.Cell(f) * 100:0}% · {c.Name}", "전기가 돌아올 때까지 버텨야 한다",
                        $"{f.Name} 흐름을 원격으로 낮춰 전지를 늘렸다", "", $"tele:eco:{f.Id}", SimTime.Hours(1));
            }
            else if (m.Powered && f.Type == FurnitureType.Ecmo && c.Vitals.Oxygen < 0.85f)
            {
                c.Vitals.Oxygen = MathF.Min(1f, c.Vitals.Oxygen + 0.05f);
                FlowUps++;
                if (Tell(f.Id * 4 + 2, SimTime.Hours(1)))
                    a.Book.Add(ActKind.Module, f.Room, $"{c.Name} 피 속 산소 {c.Vitals.Oxygen * 100:0}%", "폐가 일을 못 한다", "인공 폐 흐름을 올렸다", "", $"tele:flow:{f.Id}", SimTime.Hours(1));
            }
        }
    }

    private bool Tell(int key, long gap)
    {
        if (_told.TryGetValue(key, out var t) && _w.Tick - t < gap) return false;
        _told[key] = _w.Tick;
        return true;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var kv in _last) { I(kv.Key); F(kv.Value); }
        I(PumpDoses); I(Tapered); I(EcoTicks); I(FlowUps); I(Diagnoses); I(Watches);
    }
}
