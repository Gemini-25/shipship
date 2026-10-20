namespace ShipEngine;

/// <summary>
/// 꾸밈의 센서 셋 (함장 승인 — docs/FURNISH.md): 누출 센서 · 진동 센서 · 열화상 카메라는 주컴퓨터의 눈이다.
/// 그 방에 돌아가는(전기가 들고 고장 나지 않은) 센서가 있고 그 방이 센서 선 · 무선으로 닿으면 주컴퓨터가 더 일찍 안다:
/// <para>누출 센서 — 그 방 관의 샘(물 · 냉각수)을 바로 안다: 밸브로 좁히지 않고 그 방으로 로봇을 보낸다 (흐름 계량이 못 잡는 작은 샘도).</para>
/// <para>진동 센서 — 볼트가 반쯤 풀린 때(떨림의 결이 바뀐다)부터 안다: 덜컹거리기 전에 조이기를 맡긴다.</para>
/// <para>열화상 카메라 — 방 공기가 데워지기 전에 겉면의 뜨거운 자리(60℃ 넘게 · 배터리 셀 묶음 포함)를 본다: 로봇을 보내 살핀다.
/// 늘 뜨거운 설비(원자로 · 엔진처럼 일부러 열을 내는 것 — 설비 표의 열 1kW 넘게)의 겉면은 빼고 본다.</para>
/// 센서가 없거나 꺼지면 예전대로 — 흐름 계량 · 덜컹 소리 · 방 공기 온도로만.
/// </summary>
public sealed partial class ShipMind
{
    public const byte EyeLeak = 1, EyeVib = 2, EyeHeat = 4;
    /// <summary>열화상이 '뜨겁다'고 보는 겉면 · 셀 묶음 온도 (60℃).</summary>
    public const double HotSurfaceK = 333;
    private int[]? _eyeDevs;
    private byte[] _eyes = Array.Empty<byte>();
    private bool[]? _hotFurn;

    /// <summary>방마다 지금 돌아가는 센서 (비트 — 누출 1 · 진동 2 · 열화상 4). 세상의 값에서 그때그때 셈한다 (찍을 상태가 아니다).</summary>
    private byte[] Eyes()
    {
        if (_eyes.Length != L.Rooms.Count) _eyes = new byte[L.Rooms.Count];
        Array.Clear(_eyes);
        if (_net == null) return _eyes;
        _eyeDevs ??= _net.Devices.Where(d => d.Type is "LeakDetector" or "VibrationMonitor" or "ThermalCamera").Select(d => d.Id).ToArray();
        foreach (int d in _eyeDevs)
        {
            var dev = _net.Devices[d];
            int r = L.Room[dev.Cell];
            if (r < 0 || r >= RoomSeen.Length || RoomSeen[r] == 0 || !_net.Working(d)) continue;
            _eyes[r] |= dev.Type switch { "LeakDetector" => EyeLeak, "VibrationMonitor" => EyeVib, _ => EyeHeat };
        }
        return _eyes;
    }

    /// <summary>그 장치의 방에 돌아가는 진동 센서가 있나.</summary>
    private bool VibSeen(int device) => _net != null && L.Room[_net.Devices[device].Cell] is int r && r >= 0 && (Eyes()[r] & EyeVib) != 0;

    /// <summary>주컴퓨터가 그 장치의 풀림을 아는 문턱: 진동 센서가 있으면 반쯤 풀렸을 때 · 없으면 덜컹거릴 때 (소리).</summary>
    private float LooseKnownAt(int device) => VibSeen(device) ? Network.LooseSensed : 1f;

    /// <summary>누출 센서가 물기를 잡은 방 (그 갈래 관의 모르는 샘 — 없으면 -1 · 작은 번호부터).</summary>
    private int SensedLeakRoom(LineKind k, byte[] eyes)
    {
        if (_net == null) return -1;
        for (int r = 0; r < eyes.Length; r++)
            if ((eyes[r] & EyeLeak) != 0 && _net.UnknownLeakIn(k, r)) return r;
        return -1;
    }

    /// <summary>열화상: 그 방에서 가장 뜨거운 겉면 · 배터리 셀 묶음 (늘 뜨거운 설비는 빼고) — 60℃ 넘으면 그 칸과 온도.</summary>
    private bool HotSpot(int room, out int spot, out double kelvin)
    {
        spot = -1; kelvin = 0;
        if (_hotFurn == null)
        {
            _hotFurn = new bool[L.N];
            foreach (var fu in L.Furniture) if (T.Equip(fu.Type).Heat >= 1000) foreach (int c in fu.Cells) _hotFurn[c] = true;
        }
        foreach (int c in L.Rooms[room].Cells)
        {
            if (_hotFurn[c]) continue;
            double t = F.Ts[c];
            if (t > kelvin) { kelvin = t; spot = c; }
        }
        if (_net != null)
            foreach (var dev in _net.Devices)
            {
                if (!_net.HasCells(dev) || L.Room[dev.Cell] != room) continue;
                for (int k = 0; k < Network.CellGroups; k++)
                {
                    double t = _net.CellK[dev.Id * Network.CellGroups + k];
                    if (t > kelvin) { kelvin = t; spot = dev.Cell; }
                }
            }
        return spot >= 0 && kelvin > HotSurfaceK;
    }
}
