namespace ShipEngine;

/// <summary>
/// 야간 조명 (그래픽 단계 — 함장 결정 8 '배 안 하루 주기를 빛으로'): 주컴퓨터가 10분마다 방마다 조명 몫을 정해 망에 요청한다.
/// 밤(22시 ~ 6시): 침실 3% (잠을 덜 깨우는 밤등 · 30럭스 아래) · 복도 · 쉼터 · 식당 · 창고 30% (90럭스 — 보는 데 모자람 없다) ·
/// 일하는 방(함교 · 기관 · 원자로 · 의무실 · 생명 유지 · 작업실 · 재배실 …)은 그대로 · 센서가 위험을 읽은 방(연기 · 일산화탄소 · 기압 · 더위)은 밤에도 환하게.
/// 아침 6시에 다시 환하게 (아침 방송과 함께 하루가 시작된다).
/// </summary>
public sealed partial class ShipMind
{
    public const double NightFrom = 22, NightTo = 6;
    public const float NightQuarters = 0.03f, NightCommon = 0.3f;
    public long LampsDimmed;   // 밤에 낮춘 방 수 (누계)

    /// <summary>방 종류 · 시각 · 위험으로 조명 몫을 정한다 (주컴퓨터의 판단 — 함수 하나로 시험한다).</summary>
    public static float LightPolicy(string type, double hour, bool danger)
    {
        bool night = hour >= NightFrom || hour < NightTo;
        if (!night || danger) return 1f;
        return type switch
        {
            "Quarters" => NightQuarters,
            "Corridor" or "Lounge" or "Mess" or "Galley" or "Gym" or "Chapel" or "School" or "Observatory" or "Storage" or "Cargo" or "OreHold" or "Hangar" or "Gear" or "DroneBay" => NightCommon,
            _ => 1f,   // 일하는 방 · 재배실 · 에어락
        };
    }

    /// <summary>센서가 위험을 읽은 방: 연기 · 일산화탄소 · 낮은 기압 · 더위.</summary>
    private bool RoomDanger(int r) => r < RoomSoot.Length && (RoomSoot[r] > 30 || RoomCO[r] > 50 || RoomKPa[r] < 85 && RoomKPa[r] > 0 || RoomAirT[r] > 315);

    private void NightLights(Sim sim)
    {
        if (_net == null || sim.Tick % 250 != 11) return;
        double hour = Time.HourOfDay(sim.Tick);
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            float want = LightPolicy(L.Rooms[r].Type, hour, RoomDanger(r));
            if (r < _net.LampDim.Length && Math.Abs(_net.LampDim[r] - want) > 1e-4f)
            {
                if (want < _net.LampDim[r]) LampsDimmed++;
                _net.SetLampDim(r, want);
            }
        }
    }
}
