using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v23 행성 기지 점검 (--planetworktest): 땅의 캘 곳 · 이름난 곳 · 드론 채굴 · 정제(얼음 → 물 · 추진제 · 원료 → 판) · 기지 짓기(기상탑 · 방벽 …)
//   · 피뢰침 · 드론이 이름난 곳을 살핌 · 원정대(걸어서) · 떠날 때 기지를 남기고 자재 절반 · 불시착 생존(추진제부터) · 저장.
public static partial class Program
{
    private static int RunPlanetWorkTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"행성 기지 점검 (v23) · 시드 {seed}\n");
        try
        {
            World On(string key, int s = 0, float stay = 3f)
            {
                var w = World.CreateDefault(seed + s, 0, "Hanbit");
                PlanetSystem.Off = true;
                Run(w, SimTime.Hours(1));
                w.Planet.LandNow(PlanetGen.Special(new Rng(seed + s), 900, key));
                w.Planet.SetStay(stay);
                w.Planet.SetWeather(PlanetWeather.Calm, 0f, 30f);
                w.Planet.SetNext(PlanetWeather.Calm, 0f, 30f);
                return w;
            }

            // 1) 땅 · 채굴
            var w = On("rust");
            var pl = w.Planet;
            int digSites = pl.Sites.Count(s => s.Kind != GroundKind.Landmark), marks = pl.Sites.Count(s => s.Kind == GroundKind.Landmark);
            bool expSite = w.Expedition.Sites.Any(s => s.Kind == SiteKind.Surface);
            Check("내려앉으면 배 둘레 땅에 캘 곳 · 이름난 곳 · 원정대가 걸어갈 먼 곳이 생긴다", digSites >= 2 && marks >= 1 && expSite && pl.Sites.All(s => w.Ship.Grid.Kind(Cell.FromPosition(s.At)) == TileKind.Void),
                $"캘 곳 {digSites} ({string.Join(" · ", pl.Sites.Where(s => s.Kind != GroundKind.Landmark).Select(s => $"{s.Name} {ItemKinds.Name(s.Item)} {s.Left}"))}) · 이름난 곳 {marks} · 먼 곳 {(expSite ? w.Expedition.Sites.First(s => s.Kind == SiteKind.Surface).Name : "없음")}");
            int ore0 = w.Ship.CountStored(ItemKind.MetalOre);
            Run(w, SimTime.Hours(12));
            int got = pl.Gathered.Values.Sum();
            Check("드론이 땅으로 날아가 캐 온다 (채굴 드론이 가장 많이)", got >= 4 && pl.Sites.Any(s => s.Mined > 0), $"캔 것 {string.Join(" · ", pl.Gathered.Select(kv => $"{ItemKinds.Name(kv.Key)} {kv.Value}"))} · 금속 원료 창고 {ore0} → {w.Ship.CountStored(ItemKind.MetalOre)}");
            Check("주컴퓨터가 날씨 · 필요를 보고 기지를 짓는다 (드론이 짓는다)", pl.Builds.Count >= 1 && pl.Builds.Any(b => b.Done >= 0),
                $"{string.Join(" · ", pl.Builds.Select(b => $"{PlanetSystem.BuildName(b.Kind)} {(b.Done >= 0 ? "완공" : $"{b.Work:0.0}시간")} ({b.Why})"))} · {string.Join(" / ", pl.Notes.TakeLast(3))}");

            // 2) 정제
            {
                var r = On("frost", 1);
                r.Propulsion.Propellant = r.Propulsion.Capacity * 0.3f;
                r.Water.Level = r.Water.Capacity * 0.5f;
                float prop0 = r.Propulsion.Propellant, water0 = r.Water.Level;
                Run(r, SimTime.Hours(10));
                Check("정제 — 캔 얼음을 녹여 물 · 추진제로", r.Planet.Refined >= 3 && r.Propulsion.Propellant > prop0 + 5f, $"정제 {r.Planet.Refined} · 추진제 {prop0:0} → {r.Propulsion.Propellant:0} · 물 {water0:0} → {r.Water.Level:0}L · 캔 얼음 {r.Planet.Gathered.GetValueOrDefault(ItemKind.Ice)}");
            }

            // 3) 기상탑 피뢰침 · 4) 이름난 곳 살피기
            {
                var t = On("thunder", 2);
                for (int h = 0; h < 30 && t.Planet.Builds.All(b => !(b.Kind == BuildKind.Tower && b.Done >= 0)); h++) Run(t, SimTime.Hours(1));
                bool tower = t.Planet.Builds.Any(b => b.Kind == BuildKind.Tower && b.Done >= 0);
                t.Planet.SetWeather(PlanetWeather.Thunder, 1f, 6f);
                Run(t, SimTime.Hours(5));
                int caught = t.Planet.Harm.GetValueOrDefault("피뢰침이 받은 번개");
                Check("번개 잦은 행성엔 기상탑을 먼저 — 피뢰침이 번개를 받는다", tower && caught >= 1, $"기상탑 {(tower ? "완공" : "없음")} · 번개 {t.Planet.Strikes} 가운데 피뢰침 {caught} · 예보 {t.Planet.ForecastLead:0.0}시간");
                var sv = On("crystal", 3);
                for (int h = 0; h < 24 && sv.Planet.Surveys == 0; h++) Run(sv, SimTime.Hours(1));
                var m = sv.Planet.Sites.FirstOrDefault(s => s.Surveyed);
                Check("드론이 이름난 곳을 살핀다 (희귀 소재 · 새 줄기 · 땅의 기록)", sv.Planet.Surveys >= 1 && m != null && m.Found.Length > 0, m == null ? "살핀 곳 없음" : $"{m.Name} — {m.Found}");
            }

            // 5) 원정대 (걸어서) — 청하고 · 나가 있는 동안 이륙을 미룬다
            {
                var e = On("crystal", 4, stay: 2.5f);
                bool asked = false, tripOut = false, held = false;
                for (int h = 0; h < 60 && e.Planet.Phase == PlanetPhase.Landed; h++)
                {
                    Run(e, SimTime.Hours(1));
                    asked |= e.Log.Entries.Any(x => x.Text.Contains("원정대를 보내 보자"));
                    tripOut |= e.Expedition.PlanetTripOut;
                    held |= e.Planet.Notes.Any(n => n.Contains("원정대가 아직"));
                }
                var trip = e.Expedition.Past.Concat(e.Expedition.Current != null ? new[] { e.Expedition.Current } : Array.Empty<Trip>()).LastOrDefault(t => t.Site.Kind == SiteKind.Surface);
                Check("주컴퓨터가 먼 곳까지 원정대를 청한다 (원정 체계 그대로 — 걸어서)", asked && (trip != null || e.Expedition.Proposals.Any(p => p.Site.Kind == SiteKind.Surface)),
                    $"청함 {asked} · 나감 {tripOut} · 이륙 미룸 {held} · {(trip != null ? $"{trip.Site.Name} {trip.Phase} · {e.Expedition.LootText(trip)}" : "제안만")}");
            }

            // 6) 떠날 때
            {
                int plate0 = w.Ship.CountStored(ItemKind.Plate);
                pl.SetStay(0.01f);
                for (int h = 0; h < 12 && pl.Phase != PlanetPhase.None; h++) Run(w, SimTime.Hours(1));
                Check("떠날 때 기지는 남기고 쓸 만한 자재 절반을 걷어 간다 · 먼 곳은 지운다", pl.Phase == PlanetPhase.None && w.History.Events.Any(e => e.Text.Contains("남기고 떠났다")) && !w.Expedition.Sites.Any(s => s.Kind == SiteKind.Surface && !s.Taken),
                    $"{pl.Phase} · 못 뜨는 까닭 {pl.CannotLift() ?? "없음"} · 엔진 {string.Join(" / ", w.Propulsion.Engines.Select(m => $"{m.Efficiency:0.00} 전원 {m.Powered} 고장 {string.Join(",", m.Faults.Select(f => f.Kind))} 상태 {m.Condition:0.00} 열 {m.Heat:0.00}"))} · {string.Join(" / ", pl.Notes.TakeLast(3))} · {w.History.Events.LastOrDefault(e => e.Text.Contains("남기고 떠났다"))?.Text} · {w.History.Events.LastOrDefault(e => e.Text.Contains("에서 떠났다"))?.Text}");
            }

            // 7) 불시착 생존: 추진제가 바닥이면 얼음부터
            {
                var c = World.CreateDefault(seed + 5, 0, "Hanbit");
                PlanetSystem.Off = true;
                Run(c, SimTime.Hours(1));
                c.Propulsion.Propellant = c.Propulsion.Capacity * 0.02f;
                c.Planet.LandNow(PlanetGen.Special(new Rng(seed + 5), 901, "frost"), crash: true);
                c.Planet.SetWeather(PlanetWeather.Calm, 0f, 60f); c.Planet.SetNext(PlanetWeather.Calm, 0f, 60f);
                string? why0 = c.Planet.CannotLift();
                bool lifted = false;
                for (int h = 0; h < 24 * 6 && !lifted; h++) { Run(c, SimTime.Hours(1)); lifted = c.Planet.Stats.Liftoffs > 0 || c.Planet.Phase == PlanetPhase.Liftoff; }
                Check("불시착 생존 — 추진제가 바닥이면 얼음부터 캐서 채우고, 고친 뒤 뜬다", why0 != null && lifted && c.Planet.Gathered.GetValueOrDefault(ItemKind.Fuel) > 0,
                    $"처음 까닭: {why0} · 추진제로 바꾼 얼음 {c.Planet.Gathered.GetValueOrDefault(ItemKind.Fuel)} · 추진제 {c.Propulsion.Propellant:0}/{c.Propulsion.Capacity:0} · {c.Planet.Stats.Line()}");
            }

            // 8) 저장
            {
                var s = On("rust", 6);
                Run(s, SimTime.Hours(8));
                var s2 = StateSave.Read(StateSave.Write(s));
                Check("저장 · 되살림 — 땅 자리 · 기지 · 캔 것", s2.Planet.Sites.Count == s.Planet.Sites.Count && s2.Planet.Builds.Count == s.Planet.Builds.Count && s2.Planet.Gathered.Values.Sum() == s.Planet.Gathered.Values.Sum()
                    && s2.Planet.Sites.Zip(s.Planet.Sites).All(z => z.First.Left == z.Second.Left && z.First.At == z.Second.At),
                    $"땅 자리 {s2.Planet.Sites.Count} · 기지 {s2.Planet.Builds.Count} · 캔 것 {s2.Planet.Gathered.Values.Sum()}");
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
