namespace ShipSim.Core;

// v23 행성 머무름: 땅에 있는 동안의 일 (날씨 · 채굴 · 기지 · 탐사)이 붙는 자리.
public sealed partial class PlanetSystem
{
    private void OnLanded(Planet p, bool crash) { }
    private void OnLiftoff(Planet p) { }
    private void UpdateGround(float dt) { }
    /// <summary>임무 기간 전에 떠날 까닭이 있나 (날씨가 나빠진다 · 배가 상한다).</summary>
    private bool LeaveEarly() => false;
    /// <summary>날씨 때문에 이륙을 미룰 까닭.</summary>
    private string? HoldForWeather() => null;
    /// <summary>머문 동안 한 일 한 줄 (떠날 때 연대기에).</summary>
    private string VisitLine(Planet p) => "";
}
