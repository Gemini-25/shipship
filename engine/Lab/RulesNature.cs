using ShipEngine;

namespace Lab;

/// <summary>
/// ⑫ 자연 사건 (docs/ENGINE.md ⑫ — 이야기꾼 대신 · 함장 결정): 위험도 = 항로만 · 미리 알기 = 센서로 잡힌 것만 · 배 안의 일은 세상 법칙에서.
/// 기능만 확인한다 — 빈도 · 크기의 정밀한 조절과 확인은 엔진 개편을 다 마친 뒤 맨 끝에.
/// </summary>
public static partial class Program
{
    private static void NatureRules()
    {
        if (!Do("자연 사건")) return;
        Console.WriteLine("\n[⑫ 자연 사건 — 항로 · 센서]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = new Dictionary<char, string> { ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed", ['R'] = "SensorArray" };
        var lay = Worlds.Ascii(WeatherShip, legend);

        // ── 항로: 같은 바위 띠(1.2AU)를 반나절 — 거친 항로(8)는 파편이 잦고 고요한 항로(1)는 드물다 · 그 항로만 지나는 구간(옛 정거장 파편 지대)은 그 항로에만 ·
        //    아침 방송이 지도로 아는 구간을 알린다 · 면을 바꾸는 항로는 출발 점화 Δv가 더 든다 (√(v₁² + v₂² − 2v₁v₂cos Δi)) ──
        {
            (int made, string morning, double dv) Run(int route, double au, bool voyage = true)
            {
                var s = Worlds.Full(lay, 21);
                var o = s.Get<Outside>(); var sm = s.Get<ShipMind>();
                o.Route = route; o.Distance = au;
                if (voyage) o.StartVoyage(s, 1.52);   // 항해 중 (전이 궤도 — 반나절엔 거리가 거의 그대로)
                s.Run(Time.Hours(12));
                double dv = Outside.HohmannDv(1.0, 1.52, o.Rt?.Incline ?? 0).dv1;
                return (o.RockSeq + o.CloudSeq, sm.Morning, dv);
            }
            var calmT = Task.Run(() => Run(1, 1.2)); var wildT = Task.Run(() => Run(8, 1.2));
            var staT = Task.Run(() => Run(8, 1.04)); var usualT = Task.Run(() => Run(4, 1.04, false));   // 깊은 우주 바탕은 항해 중에만 — 가만히 떠 있게
            var calm = calmT.Result; var wild = wildT.Result; var sta = staT.Result; var usual = usualT.Result;
            var (flat, _) = Outside.HohmannDv(1.0, 1.52);
            if (Debug) Console.WriteLine($"    아침 방송(8): {wild.morning}\n    (8 · 1.04AU): {sta.morning}");
            Check("항로: 같은 바위 띠를 반나절 — 거친 항로(8)는 파편이 잦고 고요한 항로(1)는 드물다 · 옛 정거장 파편 지대는 그 항로(8)에만 · 아침 방송이 지나는 구간을 알린다 · 면을 바꾸는 항로(1)는 출발 Δv가 더 든다",
                wild.made >= 2 && calm.made < wild.made && sta.made >= 2 && usual.made == 0 && wild.morning.Contains("작은 바위 띠") && sta.morning.Contains("옛 정거장")
                && calm.dv > flat + 100 && Math.Abs(wild.dv - flat) < 1,
                $"바위 띠 반나절: 항로 8 파편 {wild.made} · 항로 1 {calm.made} / 1.04AU: 항로 8 {sta.made} · 항로 4 {usual.made} / 출발 Δv: 항로 1 {calm.dv:0}m/s · 항로 8 {wild.dv:0} · 면 그대로 {flat:0}");
        }

        // ── 센서로 잡힌 것만: 12분 뒤 닿는 파편 폭풍 — 센서 배열이 돌면 6000km에서 잡혀(사실 '센서에 잡혔다') 닿기 전에 대피하고 ·
        //    센서 배열이 고장 나면 주컴퓨터는 구름을 모른다 (대피 없음 — 예전엔 센서 없이도 알았다) ──
        {
            (long sensed, long shelters, string log) Run(bool broken)
            {
                var s = Worlds.Full(lay, 22);
                var m = s.Get<Mind>(); var o = s.Get<Outside>(); var sm = s.Get<ShipMind>(); var net = s.Get<Network>();
                m.AddPerson(s, Worlds.At(lay, 3, 1));
                s.Run(5);
                if (broken) foreach (var d in net.Devices) if (d.Type == "SensorArray") net.Broken[d.Id * 4 + (int)Part.Electric] = 1;
                long f0 = s.Facts.Total, t0 = s.Tick;
                o.AddCloud("kessler", 10000 * 12 * 60 + 400_000, 400_000, 0, 1, 0, s.Causes.Add(CauseKind.Setup, "파편 폭풍이 항로로 들어온다", s.Tick));
                for (int k = 0; k < Time.Minutes(11); k++) s.Step();
                long sensed = s.Facts.Since(f0).Count(x => x.Kind == FactKind.Sensed && x.Other == (int)TrackKind.Cloud);
                return (sensed, sm.Shelters, string.Join(" / ", sm.Log.Where(l => l.tick >= t0).Select(l => l.text).Take(4)));
            }
            var blindT = Task.Run(() => Run(true)); var seeing = Run(false); var blind = blindT.Result;
            if (Debug) Console.WriteLine($"    {seeing.log}\n    고장: {blind.log}");
            Check("센서로 잡힌 것만: 12분 뒤 닿는 파편 폭풍 — 센서 배열이 돌면 잡혀(사실 '센서에 잡혔다') 닿기 전에 대피 · 센서 배열이 고장 나면 주컴퓨터는 구름을 모른다",
                seeing.sensed == 1 && seeing.shelters == 1 && blind.sensed == 0 && blind.shelters == 0,
                $"센서 돎: 잡힘 {seeing.sensed} · 대피 {seeing.shelters} / 고장: 잡힘 {blind.sensed} · 대피 {blind.shelters}");
        }

        // ── 별 폭발: X선 감시(센서 배열)가 조짐을 잡으면 폭풍 전에 대피 · 센서 배열이 고장 나면 조짐을 모르고 폭풍이 와서 선량계가 오르는 방사선을 잰 뒤에야 ──
        {
            (long beforeStorm, long after, bool flareStarted) Run(bool broken)
            {
                var s = Worlds.Full(lay, 23);
                var m = s.Get<Mind>(); var o = s.Get<Outside>(); var sm = s.Get<ShipMind>(); var net = s.Get<Network>();
                m.AddPerson(s, Worlds.At(lay, 3, 1));
                s.Run(5);
                if (broken) foreach (var d in net.Devices) if (d.Type == "SensorArray") net.Broken[d.Id * 4 + (int)Part.Electric] = 1;
                o.ScheduleFlare(1, s.Causes.Add(CauseKind.Setup, "별의 표면이 끓어오르기 시작했다", s.Tick));
                long before = 0;
                for (int k = 0; k < Time.Minutes(60); k++) { s.Step(); if (o.FlareStart < 0) before = sm.Shelters; }
                return (before, sm.Shelters, o.FlareStart >= 0 || o.Flares > 0);
            }
            var blindT = Task.Run(() => Run(true)); var seeing = Run(false); var blind = blindT.Result;
            Check("별 폭발: X선 감시가 조짐을 잡으면 폭풍 전에 대피 · 센서 배열이 고장 나면 조짐을 모르고 폭풍이 와서 선량계가 재고서야 대피",
                seeing.beforeStorm == 1 && blind.beforeStorm == 0 && blind.after == 1 && blind.flareStarted,
                $"센서 돎: 폭풍 전 대피 {seeing.beforeStorm} / 고장: 폭풍 전 {blind.beforeStorm} · 폭풍 뒤 {blind.after}");
        }
    }
}
