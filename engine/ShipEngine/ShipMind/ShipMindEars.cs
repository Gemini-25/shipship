namespace ShipEngine;

/// <summary>
/// ⑩ 주컴퓨터 — 방마다 마이크 (소리 단계 S4 · docs/SOUND.md 결정 13 '나눔': 감각은 주컴퓨터의 마이크 · 판단은 주컴퓨터가 살핀다).
/// 전원 · 센서 선이 닿는 방(읽히는 방)의 가운데 마이크가 매 틱 듣는다 (③ 소리 장의 그 칸 — 바탕 소음이 가린 만큼 덜). 소리의 종류 · 크기 · 난 자리만 안다 (누가 냈는지는 모른다).
/// 비명(35dB 넘게) · 큰 쾅(65dB 넘게)을 들으면 '큰 소리' 사건을 올리고 로봇(드론도)을 그 자리로 보내 살핀다 — 로봇이 2칸 곁에 닿으면(로봇은 둘레 두 칸을 둘러본다) · 10분이 지나면 끝.
/// 이미 아는 일(불 · 공기 · 쓰러진 사람 · 다친 사람 · 문 · 금)이 그 방에 있으면 새로 올리지 않는다 (그 일의 소리다).
/// 자는 이 곁(1칸)에서 난 비명은 잠결의 소리(악몽)로 본다 — 로봇을 보내 자는 이들을 깨우지 않는다 (몸 감시가 숨 · 맥을 본다).
/// </summary>
public sealed partial class ShipMind
{
    public const float MicScream = 35, MicBang = 65;
    /// <summary>'큰 소리' 사건을 붙잡는 시간 (틱 — 10분).</summary>
    public static readonly long HeardHold = Time.Minutes(10);
    private int _micUpTo;
    private int[] _micCell = Array.Empty<int>();
    private readonly HashSet<int> _micDone = new();   // 이번에 다룬 소리 (여러 방 마이크에 같은 소리가 잡혀도 한 번)
    /// <summary>마이크가 들은 큰 소리 수 (확인용 · 찍는다).</summary>
    public long MicHeard;
    /// <summary>잠결의 비명으로 본 수 (확인용 · 찍지 않는다).</summary>
    public long MicSleepCries;

    private int MicCell(int r)
    {
        if (_micCell.Length != L.Rooms.Count)
        {
            _micCell = new int[L.Rooms.Count];
            for (int k = 0; k < L.Rooms.Count; k++)
            {
                var cells = L.Rooms[k].Cells;
                if (cells.Length == 0) { _micCell[k] = -1; continue; }
                double cx = cells.Average(L.X), cy = cells.Average(L.Y);
                _micCell[k] = cells.OrderBy(c => (L.X(c) - cx) * (L.X(c) - cx) + (L.Y(c) - cy) * (L.Y(c) - cy)).ThenBy(c => c).First();
            }
        }
        return _micCell[r];
    }

    /// <summary>마이크로 듣는다 (매 틱 — 들린 소리는 두 틱만 남는다).</summary>
    private void Listen(Sim sim)
    {
        var snd = sim.Find<Sound>();
        if (snd == null) return;
        int upTo = snd.LastEvent;
        if (upTo <= _micUpTo) return;
        _micDone.Clear();
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            if (RoomSeen[r] == 0) continue;
            int mic = MicCell(r);
            if (mic < 0) continue;
            float mask = -1;
            foreach (var h in snd.At(mic))
            {
                if (h.Event <= _micUpTo || h.Kind is not (SoundKind.Scream or SoundKind.Bang)) continue;
                if (mask < 0) mask = Math.Max(0, snd.Ambient(mic) - Sound.MaskFrom);
                float db = h.Db - mask;
                if (db < (h.Kind == SoundKind.Scream ? MicScream : MicBang) || !_micDone.Add(h.Event)) continue;
                if (h.Kind == SoundKind.Scream && SleeperNear(h.Source)) { MicSleepCries++; continue; }
                int src = _mind?.RoomOf(h.Source) ?? -1;
                if (src < 0) src = r;
                if (Explained(src)) continue;
                MicHeard++;
                string what = h.Kind == SoundKind.Scream ? "비명이 들렸다" : $"쾅 소리가 났다 ({db:0}dB)";
                int cause = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 {RoomName(src)}에서 {what}", sim.Tick, h.Source, h.Cause);
                Raise(sim, IncidentKind.Heard, src, h.Source, db, -1, cause, $"{what} — 로봇을 보내 살핀다");
            }
        }
        _micUpTo = upTo;
    }

    /// <summary>그 자리 1칸 곁에 자는 사람이 있나 (주컴퓨터의 몸 감시가 보는 것).</summary>
    private bool SleeperNear(int cell)
    {
        if (_mind == null || _life == null) return false;
        foreach (var a in _mind.Agents)
        {
            if (!a.IsPerson) continue;
            ref var b = ref _life[a.C.Body];
            if (b.Alive != 0 && b.Asleep != 0 && b.Cell >= 0 && _mind.Dist(b.Cell, cell) <= 1) return true;
        }
        return false;
    }

    /// <summary>그 방에 이미 아는 일이 있나 (그 일의 소리다 — 새 사건을 올리지 않는다).</summary>
    private bool Explained(int room)
    {
        foreach (var x in Incidents)
            if (x.Active != 0 && x.Room == room && x.Kind is not (IncidentKind.Heard or IncidentKind.DeviceBroken or IncidentKind.SensorLost or IncidentKind.PowerShort)) return true;
        return false;
    }

    /// <summary>'큰 소리' 사건이 아직 살필 일인가: 10분 안 · 로봇(드론)이 아직 그 자리 2칸 곁에 닿지 않았다 (둘러볼 수 있는 거리).</summary>
    private bool HeardPending(Sim sim, in Incident x)
    {
        if (sim.Tick - x.Since > HeardHold || _mind == null) return false;
        foreach (var a in _mind.Agents)
            if (!a.IsPerson && a.C.Up != 0 && x.Cell >= 0 && _mind.Dist(a.C.Cell, x.Cell) <= 2) return false;
        return true;
    }

    private void SaveEars(StateWriter w) { w.Tag("마이크"); w.I32(_micUpTo); w.I64(MicHeard); }
    private void LoadEars(StateReader r) { r.Tag("마이크"); _micUpTo = r.I32(); MicHeard = r.I64(); }
}
