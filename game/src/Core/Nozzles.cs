using System;
using System.Linq;

namespace ShipSim.Core;

// v23 주컴퓨터 대개편 2단계: 방마다 천장 자동 소화 설비 (물안개).
//   주 컴퓨터(또는 보조 두뇌)가 그 방 불을 알면 바로 연다. 컴퓨터가 멎었거나 모르면 천장의 열 퓨즈가 녹을 만큼 뜨거워져야 저절로 열린다.
//   탱크에 든 만큼만 뿌린다 (기본 12분 · 보강하면 18분) — 바닥나면 다시 채워야 한다. 펌프는 전기로 돈다 (정전이면 못 뿌린다).
//   밸브는 열 때마다 낡고, 낡은 밸브는 안 열릴 수 있다. 불길 · 폭발에 분사구가 녹거나 터진다.
//   채우기 · 고치기는 로봇이 먼저 하고, 로봇이 못 하면 사람이 한다 (작업: 소화 설비 손보기).

public sealed class NozzleStats
{
    public int Opened, ByComputer, ByFuse, Stuck, Melted, Blasted, Empty, NoPower, Out, Refilled, Fixed;
    public float Liters;
    public string Line() => $"연 횟수 {Opened} (컴퓨터 {ByComputer} · 열 퓨즈 {ByFuse}) · 안 열림 {Stuck} · 녹음 {Melted} · 폭발 {Blasted} · 바닥남 {Empty} · 정전 {NoPower} · 불 끔 {Out} · 채움 {Refilled} · 고침 {Fixed} · 뿌린 물 {Liters:0}L";
}

public sealed class NozzleSystem
{
    /// <summary>시험: 끄면 예전처럼 (보강한 방만 · 늘 뿌림 없음).</summary>
    public static bool On = true;
    /// <summary>새 밸브가 안 열릴 확률 (시험이 0 · 1로 바꾼다) — 낡을수록 더 잘 걸린다.</summary>
    public static float StickBase = 0.03f;
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7177 + 311));
    public NozzleStats Stats { get; } = new();

    public NozzleSystem(World w) => _w = w;

    /// <summary>탱크 (L) · 분사 세기 (불 세기/시간) · 한 탱크로 뿌리는 시간 · 열 퓨즈가 녹는 온도.</summary>
    public static float Tank(Room r) => r.Suppression ? 70f : 40f;
    public static float Rate(Room r) => r.Suppression ? 16f : 12f; // 활활 타는 불(시간당 +3.2)을 5~6분이면 누른다
    public static float SprayHours(Room r) => r.Suppression ? 0.3f : 0.2f;
    public static float FuseTemp(Room r) => r.Suppression ? 57f : 68f;

    public bool Has(Room r) => On && r.NozzleFitted && !r.Detached;
    public bool Ready(Room r) => Has(r) && !r.NozzleBroken && r.NozzleAgent > 0.02f;
    /// <summary>지금 열면 뿌릴 수 있다 (설비 · 약제 · 밸브 · 전기).</summary>
    public bool Works(Room r) => Ready(r) && r.Powered;
    public bool Spraying(Room r) => r.NozzleOn && Works(r);
    /// <summary>이 방 불을 시간당 얼마나 누르나 (불 계산이 쓴다).</summary>
    public float Knock(Room r) => Spraying(r) ? Rate(r) : !On && r.Suppression && r.Powered ? 4f : 0f;
    /// <summary>이 방 불을 장치가 맡을 수 있다 (예전 판단들이 쓴다: 사람을 덜 보낸다 · 해법 고르기).</summary>
    public bool Auto(Room r) => On ? Works(r) : r.Suppression && r.Powered;
    /// <summary>최근에 뿌렸다 (불이 꺼진 까닭을 따질 때).</summary>
    public bool SprayedWithin(Room r, long ticks) => r.NozzleLast >= 0 && _w.Tick - r.NozzleLast <= ticks;
    /// <summary>손볼 것이 있다 (망가졌거나 반 넘게 비었다).</summary>
    public bool NeedsService(Room r) => Has(r) && !r.NozzleOn && (r.NozzleBroken || r.NozzleAgent < 0.6f || r.NozzleWear > 0.7f);

    public string State(Room r) => !Has(r) ? "없음" : r.NozzleBroken ? "고장" : r.NozzleAgent <= 0.02f ? "바닥남"
        : r.NozzleOn ? (r.Powered ? "뿌리는 중" : "정전 · 못 뿌림") : $"대기 {r.NozzleAgent * 100f:0}%";

    /// <summary>시스템 주기마다: 열기 · 뿌리기 · 닫기.</summary>
    public void Update(float dt)
    {
        var w = _w;
        if (!On) return;
        foreach (var room in w.Ship.Rooms)
        {
            if (!Has(room)) { room.NozzleOn = false; continue; }
            int fires = w.Fire.Count == 0 ? 0 : w.Fire.CountIn(room);
            if (!room.NozzleOn)
            {
                if (fires == 0) continue;
                bool computer = w.Automation.DampersIn(room) && (w.Fire.IsKnown(room) || w.Automation.Belief.Reading(room) && w.Automation.Belief.Of(room).Fire);
                bool fuse = room.Air.Temperature >= FuseTemp(room);
                if (computer || fuse) Open(room, !computer);
                continue;
            }
            // 불이 없어지면 1분 더 뿌리고 닫는다 (다시 붙지 않게)
            if (fires == 0)
            {
                if (room.NozzleClearSince < 0) room.NozzleClearSince = w.Tick;
                else if (w.Tick - room.NozzleClearSince >= SimTime.Minutes(1)) { Close(room, true); continue; }
            }
            else room.NozzleClearSince = -1;
            if (room.NozzleBroken) { room.NozzleOn = false; continue; }
            if (!room.Powered)
            {
                if (room.NozzleSince >= 0) { Stats.NoPower++; room.NozzleSince = -1; Say(room, $"{room.Name} 소화 펌프에 전기가 없다 — 뿌리지 못한다", LogKind.Warning); }
                continue;
            }
            if (room.NozzleSince < 0) room.NozzleSince = w.Tick;
            room.NozzleLast = w.Tick;
            float used = MathF.Min(room.NozzleAgent, dt / SprayHours(room));
            room.NozzleAgent -= used;
            float liters = used * Tank(room);
            Stats.Liters += liters;
            // 물안개: 공기를 식히고 연기를 조금 씻어 내린다 · 바닥이 젖는다 (젖은 바닥 · 접속부 합선은 물질 계산이 이어 받는다)
            room.Air.Temperature = MathF.Max(18f, room.Air.Temperature - 30f * dt);
            room.Air.Smoke = MathF.Max(0f, room.Air.Smoke - 0.4f * dt);
            var wet = fires > 0 ? w.Fire.Fires.Keys.Where(c => w.Ship.RoomAt(c) == room).OrderBy(c => c.X).ThenBy(c => c.Y).ToList() : room.Cells.Where(w.Ship.IsOpenFloor).ToList();
            if (wet.Count > 0) w.Matter.Pour(R.Pick(wet), Material.Liquid, liters * 0.3f, $"{room.Name} 천장 소화 설비가 뿌린 물");
            // 불길이 분사구를 녹인다 (센 불일수록)
            if (fires > 0)
            {
                float hot = room.Cells.Max(c => w.Fire.At(c));
                if (R.Chance(0.25f * hot * fires / MathF.Max(4f, room.Cells.Count / 4f) * dt * 60f / 12f)) Break(room, "불길에 분사구가 녹았다", melted: true);
            }
            if (room.NozzleAgent <= 0.02f)
            {
                room.NozzleAgent = 0f;
                room.NozzleOn = false;
                Stats.Empty++;
                Say(room, $"{room.Name} 천장 소화 설비 약제가 바닥났다{(fires > 0 ? $" — 불이 아직 {fires}칸" : "")}", fires > 0 ? LogKind.Warning : LogKind.Ship);
            }
        }
    }

    /// <summary>밸브를 연다 — 낡은 밸브는 안 열릴 수 있다.</summary>
    public bool Open(Room room, bool byFuse)
    {
        var w = _w;
        if (!Has(room) || room.NozzleOn) return room.NozzleOn;
        if (room.NozzleBroken) return false;
        if (room.NozzleAgent <= 0.02f) return false;
        room.NozzleWear = MathF.Min(1f, room.NozzleWear + 0.08f);
        // 방 · 때마다 따로 뽑는다 (한 줄 난수의 첫 뽑기가 시드마다 모든 방에 같은 운을 주지 않게)
        var roll = new Rng(unchecked(_w.Seed * 7177 + room.Id * 9157 + (int)(_w.Tick % 1_000_003) * 31 + Stats.Opened));
        if (roll.Chance(StickBase + 0.3f * room.NozzleWear * room.NozzleWear))
        {
            Stats.Stuck++;
            Break(room, "밸브가 열리지 않는다", melted: false);
            return false;
        }
        room.NozzleOn = true;
        room.NozzleByFuse = byFuse;
        room.NozzleSince = room.Powered ? w.Tick : -1;
        room.NozzleClearSince = -1;
        Stats.Opened++;
        if (byFuse) Stats.ByFuse++; else Stats.ByComputer++;
        Say(room, byFuse ? $"{room.Name} 천장 열 퓨즈가 녹아 소화 설비가 저절로 열렸다" : $"{room.Name} 천장 소화 설비를 연다", LogKind.Ship);
        if (!room.Powered) { Stats.NoPower++; room.NozzleSince = -1; Say(room, $"{room.Name} 소화 펌프에 전기가 없다 — 뿌리지 못한다", LogKind.Warning); }
        return true;
    }

    public void Close(Room room, bool fireOut)
    {
        var w = _w;
        if (!room.NozzleOn) return;
        room.NozzleOn = false;
        room.NozzleSince = -1;
        room.NozzleClearSince = -1;
        if (fireOut)
        {
            Stats.Out++;
            Say(room, $"{room.Name} 불이 꺼졌다 — 천장 소화 설비를 잠근다 (약제 {room.NozzleAgent * 100f:0}% 남음)", LogKind.Ship);
        }
    }

    /// <summary>분사구 · 밸브가 망가졌다 (불길 · 폭발 · 안 열림).</summary>
    public void Break(Room room, string why, bool melted)
    {
        if (!Has(room) || room.NozzleBroken) return;
        room.NozzleBroken = true;
        room.NozzleOn = false;
        if (melted) Stats.Melted++;
        MarkLog.Add(room.Marks, _w.Tick, $"천장 소화 설비 — {why}");
        Say(room, $"{room.Name} 천장 소화 설비 — {why}", LogKind.Warning);
        _w.Board.RequestScan();
    }

    /// <summary>폭발이 천장을 흔들었다 (깨진 조명의 비율만큼).</summary>
    public void Blast(Room room, float share)
    {
        if (!Has(room) || room.NozzleBroken) return;
        if (!R.Chance(MathF.Min(0.8f, share))) return;
        Stats.Blasted++;
        Break(room, "폭발에 배관이 터졌다", melted: false);
    }

    /// <summary>손보기가 끝났다: 탱크를 채우고 (배의 물에서) · 망가진 분사구는 퓨즈 하나로 갈고 · 낡은 밸브를 다듬는다.</summary>
    public string Service(Room room, bool fuseUsed)
    {
        var w = _w;
        var bits = new System.Collections.Generic.List<string>();
        if (room.NozzleBroken && fuseUsed) { room.NozzleBroken = false; Stats.Fixed++; bits.Add("분사구를 갈았다"); }
        float need = (1f - room.NozzleAgent) * Tank(room);
        float got = MathF.Min(need, MathF.Max(0f, w.Water.Level - 20f)); // 마실 물은 남긴다
        if (got > 0.5f)
        {
            w.Water.Level -= got;
            room.NozzleAgent = MathF.Min(1f, room.NozzleAgent + got / Tank(room));
            Stats.Refilled++;
            bits.Add($"약제 탱크를 채웠다 ({got:0}L)");
        }
        else if (need > 0.5f) bits.Add("채울 물이 모자라다");
        if (room.NozzleWear > 0f) { room.NozzleWear = MathF.Max(0f, room.NozzleWear - 0.6f); bits.Add("밸브를 다듬었다"); }
        return string.Join(" · ", bits);
    }

    private void Say(Room room, string text, LogKind kind) => _w.Log.Add(_w.Tick, kind, $"{_w.Automation.Voice.Call}: {text}");
}
