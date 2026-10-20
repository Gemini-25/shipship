namespace ShipEngine;

/// <summary>
/// ⑩ 주컴퓨터 — 방송 (소리 단계 S6 · docs/SOUND.md 결정 7 '알림음 + 자막'): 아침 방송 · 항해 달력 알림을 배 안 화면에 자막으로 띄우고 ·
/// 방마다 스피커(방 가운데)에서 띵동 알림음을 울린다 — 깨어 있는 이가 있고 자는 이가 없는 방에서만 (주컴퓨터의 판단: 자는 이를 깨우지 않는다 · 자막은 그대로 남는다).
/// 경보(위험)는 따로 — 자는 이도 깨운다 (<see cref="Mind.Announce"/>).
/// </summary>
public sealed partial class ShipMind
{
    /// <summary>알림음 크기 (스피커 1m · dB).</summary>
    public const float ChimeDb = 60;
    /// <summary>방송 (때 · 자막) — 화면이 띄운다 (마지막 40개).</summary>
    public readonly List<(long tick, string text)> Broadcasts = new();
    /// <summary>울린 알림음 · 자는 이가 있어 울리지 않은 방 (확인용 · 찍는다).</summary>
    public long Chimes, ChimesHeld;

    /// <param name="logLine">주컴퓨터 기록에 남길 줄 (없으면 방송 글 그대로).</param>
    public void Broadcast(Sim sim, string text, string? logLine = null)
    {
        Broadcasts.Add((sim.Tick, text));
        if (Broadcasts.Count > 40) Broadcasts.RemoveAt(0);
        Say(sim, logLine ?? text);
        var snd = sim.Find<Sound>();
        if (snd == null || _mind == null || _life == null) return;
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            if (RoomSeen[r] == 0 || _roomVent[r] < 0) continue;   // 전원 · 선이 닿지 않는 방은 스피커도 없다
            bool awake = false, asleep = false;
            foreach (var a in _mind.Agents)
            {
                if (!a.IsPerson || _mind.RoomOf(a.C.Cell) != r) continue;
                ref var b = ref _life[a.C.Body];
                if (b.Alive == 0) continue;
                if (b.Asleep != 0) asleep = true; else if (b.Conscious != 0) awake = true;
            }
            if (!awake) continue;
            if (asleep) { ChimesHeld++; continue; }
            snd.Emit(sim, _roomVent[r], ChimeDb, SoundKind.Chime, 0, Mind.Computer);
            Chimes++;
        }
    }

    private void SaveBroadcasts(StateWriter w)
    {
        w.Tag("방송"); w.I32(Broadcasts.Count);
        foreach (var (t, s) in Broadcasts) { w.I64(t); w.Str(s); }
        w.I64(Chimes); w.I64(ChimesHeld);
    }
    private void LoadBroadcasts(StateReader r)
    {
        r.Tag("방송"); Broadcasts.Clear(); int n = r.I32();
        for (int i = 0; i < n; i++) Broadcasts.Add((r.I64(), r.Str() ?? ""));
        Chimes = r.I64(); ChimesHeld = r.I64();
    }
}
