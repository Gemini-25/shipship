namespace ShipEngine;

/// <summary>
/// ⑫ 자연 사건 — 정비 손상 (docs/ENGINE.md ⑫ '배 안의 일은 모두 세상 법칙에서'): 손보던 이가 그 장치의 다른 부품을 상하게 할 몫을 세상이 계산한다 —
/// 솜씨가 모자랄수록 · 졸릴수록 · 서두를수록. 상한 부품은 몇 시간 뒤 숨은 한계에 닿는다 (사실 '실수' · 크기 <see cref="HandledSize"/> — 그 사람의 손 원인에 잇는다).
/// 알아챘는지 · 말할지는 그 사람의 판단이다 (⑧ <c>Mind.Handled</c>). 몫은 기능 확인용 첫 값 — 맨 끝에 다듬는다.
/// </summary>
public sealed partial class Network
{
    /// <summary>사실 '실수'의 크기 — 손보다 부품이 상했다 (1.5) · 입을 다물었다 (2) · 고장 난 뒤에도 말하지 않았다 (3) · 켜 둔 조리대 (1 아래).</summary>
    public const float HandledSize = 1.5f;
    /// <summary>솜씨 보통(0.4) · 맑은 정신 · 차분할 때 한 번 손볼 때 다른 부품을 상하게 할 몫 (세계마다 · 시험이 바꾼다 · 찍지 않는다).</summary>
    public double HandledChance = 0.02;
    public long Mishandled;

    /// <summary>
    /// ⑧의 요청: 사람이 장치의 한 부품을 손봤다 (고침 · 미리 갈기) — 솜씨(0~1) · 졸림(0~1) · 서두름(0~1)으로 다른 부품을 상하게 했는지 계산한다.
    /// 상하게 했으면 그 원인 마디 (아니면 0).
    /// </summary>
    public int Handled(Sim sim, int device, Part worked, float skill, float sleepy, float hasty, int actor, string name, int workCause)
    {
        if (device < 0 || device >= Devices.Count) return 0;
        double p = HandledChance * Math.Max(0.1, 1.4 - skill) * (1 + 1.5 * sleepy) * (1 + hasty);
        if (Rng.Unit(_seed, Rng.Key("손보다 상함"), sim.Tick, device * 4 + (int)worked) >= p) return 0;
        var dev = Devices[device];
        var parts = Enum.GetValues<Part>().Where(q => q != worked && dev.Life(q) > 0 && !IsBroken(device, q)).ToList();
        if (parts.Count == 0) return 0;
        var part = parts[(int)(Rng.Unit(_seed, Rng.Key("상한 부품"), sim.Tick, device) * parts.Count) % parts.Count];
        double hours = 2 + 8 * Rng.Unit(_seed, Rng.Key("상한 부품 시간"), sim.Tick, device);
        int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(name)} {Ko.Eul(dev.Name)} 손보다 {Device.PartName(part)} 부품을 상하게 했다", sim.Tick, dev.Cell, workCause, 0, actor);
        AgeLeft(device, part, hours, cause);
        Mishandled++;
        sim.Facts.Emit(sim.Tick, FactKind.Mistake, dev.Cell, HandledSize, cause, actor, device);
        return cause;
    }
}
