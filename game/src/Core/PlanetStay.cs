namespace ShipSim.Core;

// v23 행성 머무름: 땅에 있는 동안의 일 — 날씨(PlanetWeatherSim.cs) · 채굴 · 기지 · 탐사(3단계).
public sealed partial class PlanetSystem
{
    private void OnLanded(Planet p, bool crash) { StartWeather(p); }
    private void OnLiftoff(Planet p) { Sheltering = false; }
    private void UpdateGround(float dt) { UpdateWeather(dt); }
    /// <summary>임무 기간 전에 떠날 까닭이 있나 (사나운 날씨가 온다 · 배가 상한다).</summary>
    private bool LeaveEarly() => WeatherLeaveEarly();
    /// <summary>날씨 때문에 이륙을 미룰 까닭.</summary>
    private string? HoldForWeather() => WeatherHold();
    /// <summary>머문 동안 한 일 한 줄 (떠날 때 연대기에).</summary>
    private string VisitLine(Planet p) => WeatherLine();
    // 3단계(기지)가 채운다
    private float BarrierShield() => 0f;
    private bool TowerCatches() => false;
    private float TowerLead() => 0f;
    private void OnQuake(float g) { }
}
