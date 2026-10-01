using System;

namespace ShipSim.Core;

// v16.3 재질 표: 바닥재 · 벽 층 · (v16.4) 물건이 같은 표를 쓴다.
// 성질은 물건마다 따로 짜지 않고 재질로 정한다 — 젖음 · 마찰 · 소리 · 물 빠짐 · 절연 · 청소 · 작은 물건 빠짐.
// 새 재질은 enum 끝에 하나 + 표에 한 줄. 새 성질은 MaterialSpec에 init 속성 하나 (기본값이 있으니 다른 줄은 그대로).

public enum Material : byte
{
    None,
    // 바닥재
    Grate, Rubber, Tile, Carpet, MetalPlate,
    // 벽 층
    HullPlate, Insulation, Panel, Partition, Glass,
    // v16.4 물건이 쓸 몫 (자리만)
    Plastic, Fabric, Wood, Ceramic, Paper,
}

/// <summary>재질 하나의 성질 (모두 0~1, 1이 "많이").</summary>
public sealed record MaterialSpec(Material Id, string Name)
{
    /// <summary>마른 상태의 미끄러움.</summary>
    public float Slip { get; init; } = 0.1f;
    /// <summary>젖었을 때의 미끄러움 (물 · 서리 위).</summary>
    public float WetSlip { get; init; } = 0.4f;
    /// <summary>발소리 · 부딪는 소리 크기.</summary>
    public float Loud { get; init; } = 0.4f;
    /// <summary>물이 아래로 빠지는 정도 (격자).</summary>
    public float Drain { get; init; }
    /// <summary>물을 머금는 정도 (카펫 — 늦게 마르고 냄새).</summary>
    public float Absorb { get; init; }
    /// <summary>열 · 전기 절연.</summary>
    public float Insulate { get; init; } = 0.3f;
    /// <summary>청소 난이도 (닦는 시간 배율의 몫).</summary>
    public float CleanHard { get; init; } = 0.3f;
    /// <summary>작은 물건(나사 · 알약)이 틈으로 빠지는 정도.</summary>
    public float SmallLoss { get; init; }
    /// <summary>닳는 빠르기.</summary>
    public float Wear { get; init; } = 0.3f;
    /// <summary>소리를 막는 정도 (벽 층 — 칸막이는 낮다).</summary>
    public float SoundBlock { get; init; } = 0.5f;
    /// <summary>전기가 통하는 정도 (v16.4 물건 · 누전).</summary>
    public float Conduct { get; init; }
    /// <summary>타는 정도.</summary>
    public float Burn { get; init; } = 0.1f;
    /// <summary>단단함 (넘어지면 아프다 · 떨어진 물건이 깨진다).</summary>
    public float Hard { get; init; } = 0.5f;
    /// <summary>화면 색조 (0xRRGGBB) — 화면은 이 값을 읽기만 한다.</summary>
    public uint Tint { get; init; } = 0x808080;
}

public static class Materials
{
    private static readonly MaterialSpec[] Table = Build();

    private static MaterialSpec[] Build()
    {
        var list = new MaterialSpec[]
        {
            new(Material.None, "없음") { Slip = 0f, WetSlip = 0f, Loud = 0f, SoundBlock = 0f, Hard = 0f, Burn = 0f },
            // ── 바닥재 ──
            new(Material.Grate, "격자") { Slip = 0.05f, WetSlip = 0.12f, Loud = 0.8f, Drain = 0.9f, Insulate = 0.1f, CleanHard = 0.6f, SmallLoss = 0.6f, Wear = 0.15f, Conduct = 0.9f, Burn = 0f, Hard = 0.9f, Tint = 0x5a6670 },
            new(Material.Rubber, "고무") { Slip = 0.03f, WetSlip = 0.15f, Loud = 0.2f, Insulate = 0.9f, CleanHard = 0.3f, Wear = 0.6f, Conduct = 0f, Burn = 0.5f, Hard = 0.3f, Tint = 0x3c4038 },
            new(Material.Tile, "타일") { Slip = 0.1f, WetSlip = 0.7f, Loud = 0.5f, Insulate = 0.5f, CleanHard = 0.15f, Wear = 0.3f, Conduct = 0.05f, Burn = 0f, Hard = 0.85f, Tint = 0xb8c2c8 },
            new(Material.Carpet, "카펫") { Slip = 0.02f, WetSlip = 0.06f, Loud = 0.1f, Absorb = 0.8f, Insulate = 0.7f, CleanHard = 0.8f, SmallLoss = 0.15f, Wear = 1f, Conduct = 0f, Burn = 0.8f, Hard = 0.15f, Tint = 0x6a4a5a },
            new(Material.MetalPlate, "금속판") { Slip = 0.12f, WetSlip = 0.55f, Loud = 0.7f, Insulate = 0.1f, CleanHard = 0.25f, Wear = 0.2f, Conduct = 0.95f, Burn = 0f, Hard = 0.95f, Tint = 0x7a8590 },
            // ── 벽 층 ──
            new(Material.HullPlate, "외판") { Loud = 0.7f, Insulate = 0.05f, SoundBlock = 0.95f, Conduct = 0.95f, Burn = 0f, Hard = 1f, Tint = 0x4a5560 },
            new(Material.Insulation, "단열재") { Loud = 0.05f, Absorb = 0.6f, Insulate = 1f, SoundBlock = 0.7f, Burn = 0.4f, Hard = 0.05f, Tint = 0xd8c878 },
            new(Material.Panel, "안쪽 패널") { Loud = 0.3f, Insulate = 0.4f, CleanHard = 0.2f, SoundBlock = 0.6f, Conduct = 0.1f, Burn = 0.2f, Hard = 0.6f, Tint = 0x9aa4ae },
            new(Material.Partition, "얇은 칸막이") { Loud = 0.4f, Insulate = 0.3f, SoundBlock = 0.25f, Conduct = 0.1f, Burn = 0.3f, Hard = 0.4f, Tint = 0xa8a8a0 },
            new(Material.Glass, "유리") { Slip = 0.15f, WetSlip = 0.6f, Loud = 0.6f, Insulate = 0.4f, CleanHard = 0.1f, SoundBlock = 0.5f, Conduct = 0f, Burn = 0f, Hard = 0.9f, Tint = 0x9cc8e0 },
            // ── 물건 (v16.4에서 쓴다) ──
            new(Material.Plastic, "플라스틱") { Loud = 0.3f, Insulate = 0.8f, Burn = 0.6f, Hard = 0.5f, Tint = 0xc8c8c0 },
            new(Material.Fabric, "천") { Loud = 0.05f, Absorb = 0.9f, Insulate = 0.7f, CleanHard = 0.6f, Burn = 0.9f, Hard = 0.05f, Tint = 0x8888a0 },
            new(Material.Wood, "나무") { Loud = 0.45f, Absorb = 0.3f, Insulate = 0.7f, Burn = 0.85f, Hard = 0.6f, Tint = 0x9a7048 },
            new(Material.Ceramic, "사기") { Loud = 0.6f, Insulate = 0.6f, Burn = 0f, Hard = 0.95f, Tint = 0xe8e8e0 },
            new(Material.Paper, "종이") { Loud = 0.05f, Absorb = 1f, Insulate = 0.5f, Burn = 1f, Hard = 0f, Tint = 0xf0ead8 },
        };
        var table = new MaterialSpec[Enum.GetValues<Material>().Length];
        foreach (var s in list) table[(int)s.Id] = s;
        for (int i = 0; i < table.Length; i++) table[i] ??= new MaterialSpec((Material)i, ((Material)i).ToString());
        return table;
    }

    public static MaterialSpec Of(Material m) => Table[(int)m];
    public static string Name(Material m) => Table[(int)m].Name;

    /// <summary>방 종류에 맞는 기본 바닥재 (설계도를 바꾸지 않고 모든 배 · 생성 배에서).</summary>
    public static Material FloorFor(RoomType kind) => kind switch
    {
        RoomType.Engine or RoomType.Reactor or RoomType.Cooling or RoomType.PumpRoom or RoomType.HvacRoom or RoomType.Crusher
            or RoomType.PropellantTank or RoomType.HeatStorage or RoomType.GasStorage or RoomType.Recycling => Material.Grate,
        RoomType.Power or RoomType.Substation or RoomType.BatteryRoom or RoomType.FuelCell or RoomType.ServerRoom or RoomType.ElectronicsLab
            or RoomType.Bridge or RoomType.BackupBridge or RoomType.Comms or RoomType.Navigation or RoomType.CraneControl or RoomType.Calibration => Material.Rubber,
        RoomType.Galley or RoomType.Medbay or RoomType.Hydroponics or RoomType.Laundry or RoomType.WaterPlant or RoomType.Decon or RoomType.Quarantine
            or RoomType.QuarantineLock or RoomType.Triage or RoomType.Morgue or RoomType.Lab or RoomType.AlgaeLab or RoomType.ProteinFarm
            or RoomType.Freezer or RoomType.Hyperbaric or RoomType.Mess or RoomType.SeedVault => Material.Tile,
        RoomType.Quarters or RoomType.PrivateCabins or RoomType.QuietQuarters or RoomType.Lounge or RoomType.Chapel or RoomType.Meditation
            or RoomType.Theater or RoomType.School or RoomType.MeetingRoom or RoomType.Archive or RoomType.WaterWallCabin or RoomType.Observatory => Material.Carpet,
        _ => Material.MetalPlate, // 통로 · 창고 · 정비실 · 에어락 · 화물칸 …
    };

    /// <summary>젖음(0~1) · 기름 · 서리 · 닳음을 더한 지금의 미끄러움 (0~1).</summary>
    public static float SlipNow(Material m, float wet, float oil, float frost, float wear)
    {
        var s = Of(m);
        float slip = s.Slip + (s.WetSlip - s.Slip) * MathF.Min(1f, wet + frost * 1.3f);
        slip += oil * (0.5f + 0.4f * (1f - s.Drain)); // 기름은 격자에서도 미끄럽다
        slip *= 1f + 0.25f * wear * (1f - s.Absorb); // 닳아 반들반들 (카펫은 오히려 덜)
        return MathF.Min(1f, slip);
    }
}
