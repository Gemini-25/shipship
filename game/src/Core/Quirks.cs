namespace ShipSim.Core;

// v12.5 로봇·드론 개체마다 버릇: 같은 기종이라도 한 대 한 대가 조금씩 다르다 (번호로 정해진다 — 결정론).
public sealed record Quirk(string Name, string Note, float Speed, float Drain, float Work);

public static class Quirks
{
    public static readonly Quirk[] All =
    {
        new("멀쩡함", "특별한 버릇이 없다", 1f, 1f, 1f),
        new("왼쪽으로 치우침", "바퀴 정렬이 틀어져 왼쪽으로 조금씩 쏠린다 — 느리다", 0.92f, 1f, 1f),
        new("배터리를 아낌", "충전 회로가 새것이다 — 오래 버틴다", 1f, 0.85f, 1f),
        new("급함", "구동기가 빠르지만 배터리를 많이 먹는다", 1.1f, 1.15f, 1f),
        new("꼼꼼함", "일할 때 한 번 더 확인한다 — 느리지만 확실하다", 1f, 1f, 0.9f),
        new("삐걱임", "관절이 닳았다 — 느리고 일도 굼뜨다", 0.9f, 1.05f, 0.9f),
        new("손이 좋음", "공구 팔이 새로 교정됐다 — 일을 빨리 끝낸다", 1f, 1f, 1.12f),
    };

    public static Quirk Of(int id, int kind) => All[(int)((uint)(id * 2654435761u + kind * 40503u) >> 16) % All.Length];
}
