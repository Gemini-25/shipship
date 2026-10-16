using System.Linq;

namespace ShipSim.Core;

// v23 행성 머무름: 땅에 있는 동안의 일 — 날씨(PlanetWeatherSim.cs) · 채굴 · 정제 · 기지 · 탐사(PlanetBase.cs).
public sealed partial class PlanetSystem
{
    private void OnLanded(Planet p, bool crash) { StartWeather(p); StartBase(p); }
    private void OnLiftoff(Planet p) { Sheltering = false; PackUp(p); }
    private void UpdateGround(float dt) { UpdateWeather(dt); HarmBuilds(dt); UpdateBase(dt); }
    /// <summary>임무 기간 전에 떠날 까닭이 있나 (사나운 날씨가 온다 · 배가 상한다).</summary>
    private bool LeaveEarly() => WeatherLeaveEarly();
    /// <summary>이륙을 미룰 까닭 (폭풍 속 · 원정대가 아직 밖에).</summary>
    private string? HoldForWeather() => _w.Expedition.PlanetTripOut ? "원정대가 아직 돌아오지 않았다" : WeatherHold();
    /// <summary>머문 동안 한 일 한 줄 (떠날 때 연대기에).</summary>
    private string VisitLine(Planet p) => string.Join(" · ", new[] { BaseLine(), WeatherLine() }.Where(s => s.Length > 0));
}
