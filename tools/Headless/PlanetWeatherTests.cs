using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v23 행성 날씨 점검 (--planetweathertest): 번개 · 태풍 · 산성비 부식 · 모래 폭풍(필터 · 태양) · 타는 행성(선체 열) · 지진 · 방사선
//   · 예보와 대피 · 일찍 떠남 / 버팀 · 폭풍 속 이륙 미룸 · 저장.
public static partial class Program
{
    private static int RunPlanetWeatherTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"행성 날씨 점검 (v23) · 시드 {seed}\n");
        try
        {
            World On(string key, int s = 0)
            {
                var w = World.CreateDefault(seed + s, 0, "Hanbit");
                PlanetSystem.Off = true;
                Run(w, SimTime.Hours(1));
                w.Planet.LandNow(PlanetGen.Special(new Rng(seed + s), 900, key));
                return w;
            }
            World OnClimate(PlanetClimate cl)
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                PlanetSystem.Off = true;
                Run(w, SimTime.Hours(1));
                var r = new Rng(seed * 3 + (int)cl);
                Planet p;
                do p = PlanetGen.Make(r, 901, 3); while (p.Climate != cl || p.Special);
                p.Weathers.Clear(); p.Weathers.Add(PlanetWeather.Calm);
                w.Planet.LandNow(p);
                return w;
            }

            // 1) 번개
            {
                var w = On("thunder");
                int faults0 = w.Ship.Machines.Sum(m => m.Faults.Count);
                float ext0 = w.Exterior.All.Sum(f => f.Condition);
                w.Planet.SetWeather(PlanetWeather.Thunder, 1f, 6f);
                Run(w, SimTime.Hours(5));
                int faults = w.Ship.Machines.Sum(m => m.Faults.Count) - faults0;
                float extLoss = ext0 - w.Exterior.All.Sum(f => f.Condition);
                Check("번개 — 실제로 떨어져 바깥 설비 · 회로를 망가뜨린다", w.Planet.Strikes >= 3 && (faults > 0 || extLoss > 0.1f),
                    $"번개 {w.Planet.Strikes} · 새 고장 {faults} · 바깥 설비 손상 {extLoss:0.00} · {string.Join(" · ", w.Planet.Harm.Select(kv => $"{kv.Key} {kv.Value}"))}");
            }
            // 2) 태풍
            {
                var w = On("eye");
                float ext0 = w.Exterior.All.Sum(f => f.Condition);
                float hull0 = w.Ship.Walls.Select(kv => kv.Value).Where(x => x.IsHull).Sum(x => x.Integrity);
                w.Planet.SetWeather(PlanetWeather.Typhoon, 1f, 8f);
                Run(w, SimTime.Hours(8));
                float extLoss = ext0 - w.Exterior.All.Sum(f => f.Condition), hullLoss = hull0 - w.Ship.Walls.Select(kv => kv.Value).Where(x => x.IsHull).Sum(x => x.Integrity);
                Check("태풍 — 태양 날개 · 안테나가 찢기고 외벽이 찌그러진다 · 그동안 밖 일을 멈춘다(대피)", (extLoss > 0.2f || hullLoss > 0.05f) && w.Planet.Wind > 0.8f,
                    $"바깥 설비 손상 {extLoss:0.00} · 외벽 {hullLoss:0.00} · 바람 {w.Planet.Wind:0.00} · {string.Join(" · ", w.Planet.Harm.Select(kv => $"{kv.Key} {kv.Value}"))}");
            }
            // 3) 산성비 부식 → 용접 일감
            {
                var w = On("rust");
                w.Planet.SetWeather(PlanetWeather.AcidRain, 1f, 14f);
                Run(w, SimTime.Hours(12));
                int corroded = w.Ship.Walls.Select(kv => kv.Value).Count(x => x.IsHull && x.Integrity < 0.999f);
                Check("산성비 · 쇳물비 — 외벽이 삭는다 (용접 일감)", corroded >= 3 && w.Planet.Harm.GetValueOrDefault("삭은 외벽") >= 3, $"삭은 외벽 칸 {corroded} · {string.Join(" · ", w.Planet.Harm.Select(kv => $"{kv.Key} {kv.Value}"))}");
            }
            // 4) 모래 폭풍
            {
                var w = On("singing");
                w.Planet.SetWeather(PlanetWeather.DustStorm, 1f, 10f);
                float solar = w.Planet.SolarMul;
                Run(w, SimTime.Hours(8));
                int clogged = w.Planet.Harm.GetValueOrDefault("막힌 필터"); // 막힌 횟수 (로봇이 그새 갈아 끼우기도 한다)
                Check("모래 폭풍 · 유독 먼지 — 필터가 막히고 태양 빛이 끊긴다", clogged >= 1 && solar < 0.35f, $"막힌 필터 {clogged} · 태양 빛 {solar:0.00} · {string.Join(" · ", w.Planet.Harm.Select(kv => $"{kv.Key} {kv.Value}"))}");
            }
            // 5) 타는 행성 · 얼어붙은 행성: 선체 온도 → 공조
            {
                var cool = World.CreateDefault(seed, 0, "Hanbit"); PlanetSystem.Off = true; Run(cool, SimTime.Hours(1));
                Run(cool, SimTime.Hours(6));
                float baseKw = cool.Air.CoolingKw;
                var hot = OnClimate(PlanetClimate.Scorched);
                float t0 = hot.Ship.LiveRooms.Average(r => r.Air.Temperature);
                Run(hot, SimTime.Hours(6));
                float t1 = hot.Ship.LiveRooms.Average(r => r.Air.Temperature);
                Check("타는 행성 — 선체가 달궈져 방이 더워지고 냉방이 힘겹다", hot.Planet.ShellC > 40f && (t1 > t0 + 1f || hot.Air.CoolingKw > baseKw * 1.5f),
                    $"바깥 {hot.Planet.OutsideC:0}°C · 선체 {hot.Planet.ShellC:0}°C · 방 평균 {t0:0.0} → {t1:0.0}°C · 냉방 {baseKw:0.0} → {hot.Air.CoolingKw:0.0}kW");
                var cold = OnClimate(PlanetClimate.Frozen);
                Run(cold, SimTime.Hours(6));
                Check("얼어붙은 행성 — 선체가 얼어 난방이 늘어난다", cold.Planet.ShellC < -15f && cold.Air.HeatingKw > cool.Air.HeatingKw + 2f,
                    $"바깥 {cold.Planet.OutsideC:0}°C · 선체 {cold.Planet.ShellC:0}°C · 난방 {cool.Air.HeatingKw:0.0} → {cold.Air.HeatingKw:0.0}kW · 방 평균 {cold.Ship.LiveRooms.Average(r => r.Air.Temperature):0.0}°C");
            }
            // 6) 지진 · 7) 방사선
            {
                var w = On("crystal");
                w.Planet.QuakeNow();
                Run(w, 30);
                Check("지진 — 배가 흔들린다 (넘어지고 떨어지고 외벽이 상한다)", w.Maneuver.Recent.Any(m => m.Why.Contains("지진")) && w.Planet.Harm.GetValueOrDefault("지진") == 1,
                    $"{string.Join(" / ", w.Maneuver.Recent.Where(m => m.Why.Contains("지진")).Select(m => $"{m.Why} {m.G:0.00}G · 넘어짐 {m.Fell} · 떨어짐 {m.Dropped}"))}");
                var r = On("halfworld");
                Run(r, SimTime.Hours(2));
                float rad = r.Ship.LiveRooms.Max(x => x.Radiation);
                Check("자기장 없는 행성 — 바깥쪽 방에 방사선이 든다", rad >= 0.1f, $"가장 센 방 {rad:0.00}");
            }
            // 8) 예보 · 대피 · 9) 일찍 떠남 / 버팀 · 10) 폭풍 속 이륙 미룸
            {
                var w = On("thunder", 1);
                w.Planet.SetWeather(PlanetWeather.Calm, 0f, 2f);
                w.Planet.SetNext(PlanetWeather.Thunder, 0.95f, 2f);
                w.Planet.SetStay(0.3f);
                Run(w, SimTime.Minutes(30));
                bool fc = w.Planet.Forecasted >= 0 && w.Log.Entries.Any(e => e.Text.Contains("예보 —"));
                Check("예보 — 사나운 날씨를 몇 시간 앞서 알리고 밖 일을 멈춘다", fc && w.Planet.Sheltering, $"예보 {(fc ? "있음" : "없음")} · 대피 {w.Planet.Sheltering} · 예보 시간 {w.Planet.ForecastLead:0.0}시간");
                // 임무 거의 끝 → 일찍 뜬다
                var early = On("thunder", 2);
                early.Planet.SetStay(0.5f);
                Run(early, SimTime.Hours(9)); // 임무 3분의 2쯤
                early.Planet.SetWeather(PlanetWeather.Calm, 0f, 2f);
                early.Planet.SetNext(PlanetWeather.Thunder, 0.95f, 2f);
                Run(early, SimTime.Minutes(30));
                Check("임무가 거의 끝났으면 폭풍이 오기 전에 일찍 뜬다", early.Planet.Stats.Early == 1 && early.Planet.Phase != PlanetPhase.Landed, $"{early.Planet.Decision} · {early.Planet.Stats.Line()}");
                // 임무 초반 → 버틴다
                var stay = On("thunder", 3);
                stay.Planet.SetStay(2.5f);
                stay.Planet.SetWeather(PlanetWeather.Calm, 0f, 2f);
                stay.Planet.SetNext(PlanetWeather.Thunder, 0.95f, 2f);
                Run(stay, SimTime.Minutes(30));
                Check("임무 초반이면 버틴다", stay.Planet.Hunkered && stay.Planet.Phase == PlanetPhase.Landed, $"{stay.Planet.Decision}");
                // 떠날 때가 됐는데 폭풍 속 → 미룬다 → 지나가면 뜬다
                var hold = On("thunder", 4);
                hold.Planet.SetStay(0.01f);
                hold.Planet.SetWeather(PlanetWeather.Thunder, 0.9f, 3f);
                hold.Planet.SetNext(PlanetWeather.Calm, 0f, 3f);
                Run(hold, SimTime.Hours(1));
                bool held = hold.Planet.Phase == PlanetPhase.Landed && hold.Planet.Stats.Holds >= 1;
                for (int m = 0; m < 6 * 60 && hold.Planet.Phase == PlanetPhase.Landed; m += 10) Run(hold, SimTime.Minutes(10));
                Check("떠날 때 폭풍 속이면 이륙을 미루고 지나간 뒤에 뜬다", held && hold.Planet.Phase != PlanetPhase.Landed && hold.Planet.Weather != PlanetWeather.Thunder,
                    $"미룸 {hold.Planet.Stats.Holds} · {string.Join(" / ", hold.Planet.Notes.TakeLast(2))}");
                // 저장
                var w2 = StateSave.Read(StateSave.Write(stay));
                Check("저장 · 되살림 — 날씨 · 예보 · 버팀 · 피해 장부", w2.Planet.Weather == stay.Planet.Weather && w2.Planet.NextWx == stay.Planet.NextWx && w2.Planet.Hunkered && w2.Planet.WxUntil == stay.Planet.WxUntil,
                    $"{w2.Planet.Weather} → {w2.Planet.NextWx} · 버팀 {w2.Planet.Hunkered}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"  ✗ 예외: {e}");
            _fails++;
        }
        PlanetSystem.Off = false;
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
