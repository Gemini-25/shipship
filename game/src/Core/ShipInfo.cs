using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.9 배의 내력표: 용도 · 뼈대 · 설계사 · 시작 상태 · 연식 · 시작 화물 · 예전 이름 · 겪은 사건 · 장단점 · 난이도.
// 기존 배(제비 · 미리내 · 한빛 · 은하 · 천마)와 옛 생성 키("gen:인원:시드")는 내력이 없다 — 예전 그대로 돈다.

/// <summary>배의 용도: 방 고르기와 기본 방의 크기를 바꾼다.</summary>
public enum ShipPurpose { General, Mining, Colony, Hospital, Research, Supply, Courier }

/// <summary>배의 뼈대: 통로가 어떻게 이어지나.</summary>
public enum ShipFrame { Linear, Ring, Spine, Twin, Cargo, Patchwork, Courier }

/// <summary>설계사: 군용(격벽 · 이중 배선 · 좁음) · 민간(넓음 · 단일 고장점 · 싼 부품) · 개척민(비표준 · 임시 개조).</summary>
public enum ShipDesigner { Civilian, Military, Settler }

/// <summary>시작 상태: 새 배 · 중고 · 고물 · 전쟁 상흔 · 버려졌다 다시 띄운 배.</summary>
public enum ShipStart { New, Used, Junk, WarScarred, Derelict }

/// <summary>배 하나의 내력. 템플릿의 선택 인자로 붙는다 (없으면 예전 배).</summary>
public sealed record ShipInfo(ShipPurpose Purpose, ShipFrame Frame, ShipDesigner Designer, ShipStart Start, int Built,
    (ItemKind kind, int count)[]? Cargo = null, string? FormerName = null, string[]? Events = null,
    string Pros = "", string Cons = "", int Difficulty = 3, string Story = "", bool SharedBed = false)
{
    public (ItemKind kind, int count)[] CargoItems => Cargo ?? Array.Empty<(ItemKind, int)>();
    public string[] Past => Events ?? Array.Empty<string>();
}

public static class ShipInfos
{
    /// <summary>항해의 해 (연식 계산용).</summary>
    public const int Year = 2241;

    public static string Name(ShipPurpose p) => p switch
    {
        ShipPurpose.Mining => "채굴선", ShipPurpose.Colony => "이민선", ShipPurpose.Hospital => "병원선", ShipPurpose.Research => "연구선",
        ShipPurpose.Supply => "보급선", ShipPurpose.Courier => "우편선", _ => "일반선",
    };

    public static string Name(ShipFrame f) => f switch
    {
        ShipFrame.Ring => "고리형", ShipFrame.Spine => "척추형", ShipFrame.Twin => "쌍동선", ShipFrame.Cargo => "화물선형",
        ShipFrame.Patchwork => "누더기형", ShipFrame.Courier => "소형 쾌속", _ => "직선형",
    };

    public static string FrameNote(ShipFrame f) => f switch
    {
        ShipFrame.Ring => "통로가 한 바퀴 돈다 — 어디든 두 갈래 길",
        ShipFrame.Spine => "긴 중앙 통로에 모듈이 가지처럼 — 모듈마다 기밀문 하나, 통째 봉쇄 · 분리",
        ShipFrame.Twin => "선체 둘을 연결 통로로 — 끊겨도 반쪽씩 버틴다",
        ShipFrame.Cargo => "가운데 큰 화물칸 · 생활 구역은 뒤쪽에 작게",
        ShipFrame.Patchwork => "시대가 다른 모듈을 이어 붙였다 — 통로 폭이 제각각",
        ShipFrame.Courier => "엔진이 배의 절반 · 비좁다",
        _ => "엔진실 → 층마다 방 줄 → 뱃머리 함교",
    };

    public static string Name(ShipDesigner d) => d switch { ShipDesigner.Military => "군용", ShipDesigner.Settler => "개척민", _ => "민간" };

    public static string DesignerNote(ShipDesigner d) => d switch
    {
        ShipDesigner.Military => "격벽 · 이중 배선 · 좋은 부품 · 좁다",
        ShipDesigner.Settler => "비표준 부품 · 임시 개조 · 손으로 만든 것",
        _ => "넓다 · 단일 고장점 · 싼 부품",
    };

    public static string Name(ShipStart s) => s switch
    {
        ShipStart.New => "새 배", ShipStart.Used => "중고", ShipStart.Junk => "고물", ShipStart.WarScarred => "전쟁 상흔", _ => "버려졌다 다시 띄운 배",
    };

    public static string StartNote(ShipStart s) => s switch
    {
        ShipStart.New => "부품이 새것 · 흠이 없다",
        ShipStart.Used => "적당히 닳았다 · 전 승무원의 흔적",
        ShipStart.Junk => "많이 닳았다 · 용접 자국 · 녹",
        ShipStart.WarScarred => "그을음 · 땜질한 파공 · 막힌 구역",
        _ => "오래 비어 있었다 · 막힌 구역 · 먼지 · 숨은 물건이 많다",
    };

    /// <summary>시작 상태가 얼마나 거친가 (0 새 배 ~ 1).</summary>
    public static float Rough(ShipStart s) => s switch { ShipStart.New => 0f, ShipStart.Used => 0.35f, ShipStart.WarScarred => 0.6f, ShipStart.Junk => 0.8f, _ => 0.75f };

    public static string Stars(int difficulty) => new string('★', Math.Clamp(difficulty, 1, 5)) + new string('☆', 5 - Math.Clamp(difficulty, 1, 5));

    public static string CargoText(ShipInfo i) => i.CargoItems.Length == 0 ? "없음" : string.Join(" · ", i.CargoItems.Select(c => $"{ItemKinds.Name(c.kind)} {c.count}"));

    public static readonly ShipPurpose[] GenPurposes = { ShipPurpose.General, ShipPurpose.Mining, ShipPurpose.Colony, ShipPurpose.Hospital, ShipPurpose.Research, ShipPurpose.Supply };
    public static readonly ShipFrame[] GenFrames = { ShipFrame.Linear, ShipFrame.Ring, ShipFrame.Spine };

    public static string Key(ShipPurpose p) => p.ToString().ToLowerInvariant();
    public static string Key(ShipFrame f) => f.ToString().ToLowerInvariant();

    public static ShipPurpose? ParsePurpose(string s) => Enum.TryParse<ShipPurpose>(s, true, out var p) ? p : null;
    public static ShipFrame? ParseFrame(string s) => Enum.TryParse<ShipFrame>(s, true, out var f) ? f : null;
}
