using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// 압축-마 사고 70 → 100: v16~v18 에 들어온 시스템(무중력 · 고양이 · 화분 · 바구미 · 배수 · 기동 · 블랙박스 · 소리 · 회의 · 꾸미는 일 · 조리 · 습도 · 온도)을 쓰는 사고 30.
// 한 줄 = 사고 하나: 이름 · 대상 · 무게 · 설명 + 규모 · 원인 · 전조 · 대응 갈래(해법 표 Snag) · 흔적 · 기억.
// 원인이 무르익으면(Pressure) 전조가 먼저 보인다 — 사람이 보거나(그 방) 컴퓨터가 잰다(센서) → 누군가 손을 쓰면 막고, 못 막으면 사고가 난다.
// 사고는 흔적(Trace)을 남기고 겪은 사람은 그 방을 기억한다 — 같은 전조를 두 번째 보면 더 빨리 알아챈다.

/// <summary>사고 한 줄의 나머지: 규모 · 원인 · 전조 · 대응 · 흔적 · 기억 · 컴퓨터가 잴 수 있나.</summary>
public sealed record HazardMore(HazardKind Kind, IncidentScale Scale, string Cause, string Omen, Snag? Way, string Trace, string Remember, bool Sensor = false);

public static class HazardsV18
{
    public const HazardKind First = HazardKind.CatScratch;
    public static bool Is(HazardKind k) => k >= First;

    public static readonly HazardSpec[] Specs =
    {
        // ① 개인
        new(HazardKind.CatScratch, "고양이 할큄", HazardTarget.Ship, 2f, "고양이 할큄 — 겁먹은 고양이를 안아 올리다 손등을 긁혔다 · 고양이는 한동안 그 사람을 피한다"),
        new(HazardKind.StaticZap, "옷 정전기", HazardTarget.Crew, 2f, "옷 정전기 — 승무원을 클릭: 메마른 공기에 옷이 정전기를 머금었다가 콘솔에 손을 대는 순간 튄다"),
        new(HazardKind.HeatExhaustion, "열탈진", HazardTarget.Crew, 2f, "열탈진 — 승무원을 클릭: 더운 방에서 방열복 없이 오래 일하다 어지러워 주저앉는다"),
        new(HazardKind.SpaceSick, "멀미", HazardTarget.Ship, 2f, "멀미 — 무게가 사라지거나 배가 흔들린 뒤 속이 뒤집혀 토한다 · 방이 더러워진다"),
        new(HazardKind.HatchFall, "점검 뚜껑 낙상", HazardTarget.Crew, 2f, "점검 뚜껑 낙상 — 승무원을 클릭: 열어 둔 바닥 점검 뚜껑에 발이 빠졌다"),
        new(HazardKind.ScaldSpill, "국 엎음", HazardTarget.Ship, 2f, "국 엎음 — 끓는 국 냄비를 옮기다 엎어 손과 바닥을 데었다"),
        // ② 방
        new(HazardKind.PotFire, "냄비 화재", HazardTarget.Ship, 2.5f, "냄비 화재 — 불 위에 올려 둔 채 자리를 비운 냄비가 타올라 불이 붙는다"),
        new(HazardKind.FermentBurst, "발효 항아리 터짐", HazardTarget.Ship, 2f, "발효 항아리 터짐 — 김을 빼 주지 않은 항아리가 부풀어 뚜껑이 날아간다 · 시큼한 국물이 바닥에 번진다"),
        new(HazardKind.HeaterOverload, "이동식 난로 과부하", HazardTarget.Ship, 2f, "이동식 난로 과부하 — 추운 방의 난로가 회로를 넘겨 차단기가 떨어지고 전선이 그을린다"),
        new(HazardKind.PumpShock, "양수기 감전", HazardTarget.Ship, 2f, "양수기 감전 — 물이 고인 바닥에서 양수기를 옮기다 찌릿 감전된다"),
        new(HazardKind.DrainBackflow, "배수구 역류", HazardTarget.Ship, 2.5f, "배수구 역류 — 막혀 가던 배수구가 거꾸로 차오른다 · 냄새가 번지고 바닥이 더러워진다"),
        new(HazardKind.WeevilSwarm, "바구미 번짐", HazardTarget.Ship, 2f, "바구미 번짐 — 곡물 자루 속 바구미가 옆 선반까지 번진다"),
        new(HazardKind.PlantTopple, "화분 엎어짐", HazardTarget.Ship, 2f, "화분 엎어짐 — 고정하지 않은 화분이 흔들림에 떨어져 깨진다 · 흙이 바닥에 쏟아진다"),
        new(HazardKind.MoonshineFire, "밀주 통 불", HazardTarget.Ship, 1.5f, "밀주 통 불 — 누군가 몰래 둔 증류 통이 데워지다 알코올 김에 불이 붙는다"),
        new(HazardKind.PartitionFall, "칸막이 넘어짐", HazardTarget.Ship, 2f, "칸막이 넘어짐 — 덜 조인 칸막이 판이 넘어져 곁의 사람을 덮친다"),
        new(HazardKind.LockedIn, "문 잠겨 갇힘", HazardTarget.Ship, 2f, "문 잠겨 갇힘 — 문 구동기가 걸려 안에 있던 사람이 나오지 못한다"),
        new(HazardKind.BearingWhine, "베어링 울음", HazardTarget.Machine, 2.5f, "베어링 울음 — 설비를 클릭: 기름이 마른 베어링이 밤새 높은 소리로 운다 · 곁에서 자는 사람이 잠을 설친다"),
        new(HazardKind.MeetingBrawl, "회의 몸싸움", HazardTarget.Ship, 1.5f, "회의 몸싸움 — 날 선 말이 오가다 멱살잡이가 된다"),
        // ③ 계통 · 구역
        new(HazardKind.GravityHiccup, "중력 끊김", HazardTarget.Ship, 1.5f, "중력 끊김 — 중력 장치가 몇 분 끊겼다가 돌아온다 · 그 사이 물건이 뜨고 사람이 휘청인다"),
        new(HazardKind.ManeuverJolt, "예고 없는 분사", HazardTarget.Ship, 1.5f, "예고 없는 분사 — 자세 제어 분사가 예고 없이 터져 선반의 물건이 쏟아지고 사람이 넘어진다"),
        new(HazardKind.RebootGlitch, "재부팅 중 사고", HazardTarget.Ship, 1.5f, "재부팅 중 사고 — 주컴퓨터가 다시 켜지는 동안 조명과 문 제어가 엉킨다"),
        new(HazardKind.BlackboxGap, "기록 끊김", HazardTarget.Ship, 1f, "기록 끊김 — 블랙박스 기록 몇 시간이 비었다 · 누가 지웠는지 수군댄다"),
        new(HazardKind.GreywaterJam, "회색수 계통 막힘", HazardTarget.Ship, 1.5f, "회색수 계통 막힘 — 개수대 · 샤워 · 세탁 배수가 한꺼번에 느려진다"),
        new(HazardKind.CatLost, "고양이 실종", HazardTarget.Ship, 1.5f, "고양이 실종 — 놀란 고양이가 어딘가 숨어 나오지 않는다 · 여럿이 찾아 나선다"),
        // ④ 배 전체
        new(HazardKind.GravityFailure, "중력 장치 고장", HazardTarget.Ship, 1f, "중력 장치 고장 — 중력이 꺼진 채 돌아오지 않는다 · 고칠 때까지 배 전체가 떠다닌다"),
        new(HazardKind.DockSealFail, "도킹 기밀 실패", HazardTarget.Ship, 1f, "도킹 기밀 실패 — 접안 고리의 씰이 맞물리지 않아 에어락 쪽 외벽이 갈라지고 공기가 샌다"),
        new(HazardKind.WreckDrift, "난파선 잔해 떼", HazardTarget.Ship, 1f, "난파선 잔해 떼 — 부서진 배의 조각들이 흩어져 날아든다"),
        // ⑤ 우주급
        new(HazardKind.GammaFlash, "감마선 섬광", HazardTarget.Ship, 0.5f, "감마선 섬광 — 먼 별의 섬광이 배를 훑는다 · 모두가 조금씩 쬐고 감지기가 하얗게 탄다"),
        new(HazardKind.TidalPull, "조석 당김", HazardTarget.Ship, 0.5f, "조석 당김 — 무거운 천체 곁을 지나며 배가 길게 당겨진다 · 골조가 신음한다"),
        new(HazardKind.MagnetarPulse, "자기 폭풍 파동", HazardTarget.Ship, 0.5f, "자기 폭풍 파동 — 강한 자기장 파동에 전자 장비가 튀고 쇠붙이가 벽에 들러붙는다"),
    };

    public static readonly HazardMore[] More =
    {
        new(HazardKind.CatScratch, IncidentScale.Personal, "겁먹은 고양이", "꼬리를 부풀리고 낮게 운다", Snag.Injured, "손등의 할퀸 자국 · 바닥의 털 뭉치", "고양이가 무섭다"),
        new(HazardKind.StaticZap, IncidentScale.Personal, "메마른 공기 · 합성 옷", "옷이 몸에 달라붙고 머리카락이 뜬다", Snag.Spark, "콘솔 모서리의 탄 점", "콘솔에 손대기 전에 머뭇거린다", Sensor: true),
        new(HazardKind.HeatExhaustion, IncidentScale.Personal, "더운 방 · 방열복 없이", "땀이 비 오듯 하고 말이 느려진다", Snag.Injured, "벗어 던진 장갑과 빈 물병", "더운 방이 싫다", Sensor: true),
        new(HazardKind.SpaceSick, IncidentScale.Personal, "무게가 사라짐 · 흔들림", "얼굴이 하얗게 질리고 입을 막는다", Snag.Spill, "토한 자국과 구겨진 봉지", "그 방 냄새가 떠오른다"),
        new(HazardKind.HatchFall, IncidentScale.Personal, "열어 둔 점검 뚜껑", "뚜껑 곁에 표시 고깔이 없다", Snag.Injured, "열린 뚜껑과 넘어진 고깔", "바닥을 보며 걷는다"),
        new(HazardKind.ScaldSpill, IncidentScale.Personal, "끓는 냄비를 맨손으로", "냄비 손잡이가 달아올랐다", Snag.Spill, "바닥의 국 얼룩과 김", "뜨거운 냄비를 조심한다"),
        new(HazardKind.PotFire, IncidentScale.Room, "켜 둔 채 비운 조리대", "탄내가 조금씩 짙어진다", Snag.Fire, "새카맣게 탄 냄비", "조리대를 비우지 않는다", Sensor: true),
        new(HazardKind.FermentBurst, IncidentScale.Room, "김을 빼지 않은 발효 항아리", "뚜껑이 들썩이고 시큼한 냄새", Snag.Spill, "깨진 항아리 조각과 붉은 국물", "항아리 뚜껑을 날마다 연다", Sensor: true),
        new(HazardKind.HeaterOverload, IncidentScale.Room, "추운 방에 난로 여럿", "난로 전선이 따뜻하다", Snag.Spark, "그을린 난로와 녹은 전선", "한 콘센트에 둘을 꽂지 않는다"),
        new(HazardKind.PumpShock, IncidentScale.Room, "물 고인 바닥 · 젖은 손", "양수기 손잡이에서 찌르르", Snag.Spark, "물웅덩이 속 그을린 전선", "젖은 바닥의 전기를 먼저 내린다"),
        new(HazardKind.DrainBackflow, IncidentScale.Room, "음식물 찌꺼기로 막혀 가는 배수구", "물이 빙글빙글 늦게 빠지고 꾸르륵 소리", Snag.Clog, "배수구 둘레의 갈색 고리", "배수구에 찌꺼기를 흘리지 않는다", Sensor: true),
        new(HazardKind.WeevilSwarm, IncidentScale.Room, "열어 둔 곡물 자루", "자루에 작은 구멍 · 가루", Snag.Food, "선반 위 바구미 줄", "곡물 자루를 꼭 묶는다"),
        new(HazardKind.PlantTopple, IncidentScale.Room, "고정하지 않은 화분", "화분이 선반 끝으로 밀려나 있다", Snag.Spill, "깨진 화분과 흙더미", "화분을 끈으로 묶는다"),
        new(HazardKind.MoonshineFire, IncidentScale.Room, "몰래 둔 증류 통 · 곁의 열", "달큰한 알코올 냄새", Snag.Fire, "터진 통과 그을린 관", "몰래 하던 일이 들통날까 조마조마"),
        new(HazardKind.PartitionFall, IncidentScale.Room, "덜 조인 칸막이", "칸막이가 기울어 흔들린다", Snag.Injured, "넘어진 판과 빠진 볼트", "칸막이 곁을 피해 걷는다"),
        new(HazardKind.LockedIn, IncidentScale.Room, "닳은 문 구동기", "문이 덜컥거리며 늦게 열린다", Snag.Trapped, "문틈의 지렛대 자국", "문이 닫히면 숨이 막힌다"),
        new(HazardKind.BearingWhine, IncidentScale.Room, "기름이 마른 베어링", "낮은 웅웅 소리가 점점 높아진다", null, "설비 밑 기름 방울", "그 소리가 귀에 남는다", Sensor: true),
        new(HazardKind.MeetingBrawl, IncidentScale.Room, "쌓인 불만 · 지친 사람들", "말끝이 날카롭고 목소리가 커진다", Snag.Injured, "넘어진 의자와 흩어진 종이", "그 사람과 눈을 마주치지 않는다"),
        new(HazardKind.GravityHiccup, IncidentScale.System, "중력 장치 전원의 떨림", "발밑이 잠깐 가벼워진다", Snag.CargoLoose, "흩어진 물건과 벽의 찍힌 자국", "물건을 놓을 때 한 번 더 묶는다"),
        new(HazardKind.ManeuverJolt, IncidentScale.System, "자세 제어 밸브 오작동", "분사구에서 칙칙 새는 소리", Snag.CargoLoose, "바닥의 미끄러진 자국", "선반의 걸쇠를 확인한다"),
        new(HazardKind.RebootGlitch, IncidentScale.System, "쌓인 제어 오류", "화면이 잠깐씩 멈춘다", Snag.Dark, "콘솔의 다시 켜짐 화면", "컴퓨터가 멎으면 손으로 문을 연다"),
        new(HazardKind.BlackboxGap, IncidentScale.System, "기록 장치 전원 끊김 · 누군가의 손", "기록등이 깜빡이다 꺼진다", null, "블랙박스 위의 빈 기록 띠", "누가 지웠을까 서로 본다"),
        new(HazardKind.GreywaterJam, IncidentScale.System, "배수관 전체의 기름때", "여러 곳에서 물이 늦게 빠진다", Snag.Clog, "개수대 · 샤워 바닥의 고인 물", "기름을 따로 모은다", Sensor: true),
        new(HazardKind.CatLost, IncidentScale.System, "크게 놀란 고양이", "고양이가 밥을 남기고 구석만 찾는다", null, "구석에 놓인 밥그릇과 쪽지", "이름을 부르며 찾던 밤"),
        new(HazardKind.GravityFailure, IncidentScale.Ship, "중력 장치 고장", "중력 장치가 높게 웅웅거린다", Snag.CargoLoose, "떠다니다 부딪힌 자국들", "중력이 또 꺼질까 손잡이를 쥔다", Sensor: true),
        new(HazardKind.DockSealFail, IncidentScale.Ship, "맞물리지 않은 접안 고리 씰", "접안 고리에서 쉭 소리", Snag.Breach, "서리 낀 씰 고리", "접안할 때 숨을 참는다", Sensor: true),
        new(HazardKind.WreckDrift, IncidentScale.Ship, "근처의 부서진 배", "감지기에 작은 점이 여럿 뜬다", Snag.Breach, "함교 화면의 잔해 궤적", "부서진 배를 보면 조용해진다", Sensor: true),
        new(HazardKind.GammaFlash, IncidentScale.Cosmic, "먼 별의 폭발", "감지기가 잠깐 하얗게 질린다", Snag.CommsDown, "창에 남은 하얀 줄", "그 빛을 본 날"),
        new(HazardKind.TidalPull, IncidentScale.Cosmic, "무거운 천체 곁의 항로", "골조가 낮게 신음한다", Snag.CargoLoose, "바닥의 당김 금", "배가 늘어나는 듯하던 밤"),
        new(HazardKind.MagnetarPulse, IncidentScale.Cosmic, "자기성의 파동", "나침반 바늘이 돈다", Snag.Spark, "벽에 들러붙은 공구 뭉치", "쇠붙이가 날아가던 순간"),
    };

    private static readonly Dictionary<HazardKind, HazardMore> ByKind = More.ToDictionary(m => m.Kind);
    public static HazardMore? Of(HazardKind k) => ByKind.TryGetValue(k, out var m) ? m : null;

    /// <summary>설비를 고르는 사고: 그 설비에 걸 수 있나.</summary>
    public static bool Fits(HazardKind k, Furniture f) => k switch
    {
        HazardKind.BearingWhine => Rotating(f.Type),
        _ => true,
    };

    internal static bool Rotating(FurnitureType t) => t is FurnitureType.CoolantPump or FurnitureType.WaterRecycler or FurnitureType.OxygenGenerator or FurnitureType.Scrubber
        or FurnitureType.HeatExchanger or FurnitureType.AuxGenerator or FurnitureType.EngineCore or FurnitureType.Treadmill or FurnitureType.WashingMachine or FurnitureType.Compactor;

    /// <summary>원인이 얼마나 무르익었나 0~1 (그리고 그 방 · 설비). 0 이면 전조가 없다 — 그 사고는 무작위로만 난다.</summary>
    public static (float p, Room? room, int refId) Pressure(World w, HazardKind k)
    {
        var ship = w.Ship;
        switch (k)
        {
            case HazardKind.CatScratch:
                return w.Eco.Cat is ShipCat cat && cat.State != CatState.Dead && cat.Fear > 0.5f ? ((cat.Fear - 0.5f) * 2f, ship.Rooms.ElementAtOrDefault(cat.RoomId), -1) : (0f, null, -1);
            case HazardKind.StaticZap:
            {
                Room? best = null; float lo = 1f;
                foreach (var r in ship.Rooms) if (!r.Detached && r.Humidity < lo && r.Furniture.Any(f => f.Type is FurnitureType.Console or FurnitureType.MainComputer)) { lo = r.Humidity; best = r; }
                return (Math.Clamp((0.3f - lo) / 0.15f, 0f, 1f), best, -1);
            }
            case HazardKind.HeatExhaustion:
            {
                Room? best = null; float hi = 0f;
                foreach (var r in ship.Rooms) if (!r.Detached && r.Type != RoomType.Corridor && r.Air.Temperature > hi) { hi = r.Air.Temperature; best = r; }
                return (Math.Clamp((hi - 30f) / 8f, 0f, 1f) * FittingSystem.HeatSuitMul(best), best, -1);
            }
            case HazardKind.SpaceSick:
            {
                if (!w.ZeroG.Weightless) return (0f, null, -1);
                float q = 0f; int who = -1;
                foreach (var (id, v) in w.ZeroG.Queasy) if (v > q) { q = v; who = id; }
                return (Math.Clamp((q - 0.4f) / 0.5f, 0f, 1f), w.Crew.FirstOrDefault(c => c.Id == who)?.Room, who);
            }
            case HazardKind.PotFire:
                foreach (var st in ship.FurnitureOf(FurnitureType.Stove))
                    if (w.Maneuver.PotOn(st) && !w.Fittings.Quiet.ContainsKey(st.Id) && !w.Crew.Any(c => !c.Dead && c.Room == st.Room && c.IsAwake)) return (0.8f, st.Room, st.Id);
                return (0f, null, -1);
            case HazardKind.FermentBurst:
            {
                var (p, f) = w.Fittings.WorstCrock();
                return (p, f?.Room, f?.Id ?? -1);
            }
            case HazardKind.DrainBackflow:
            {
                Drain? worst = null;
                foreach (var d in w.Drains.Drains) if (!d.Backflow && (worst == null || d.Clog > worst.Clog)) worst = d;
                return worst == null ? (0f, null, -1) : (Math.Clamp((worst.Clog - 0.55f) / 0.35f, 0f, 1f) * FittingSystem.DrainMul(w, ship.Rooms.ElementAtOrDefault(worst.RoomId)), ship.Rooms.ElementAtOrDefault(worst.RoomId), worst.Id);
            }
            case HazardKind.GreywaterJam:
            {
                if (w.Drains.Drains.Count < 2) return (0f, null, -1);
                float s = 0f; foreach (var d in w.Drains.Drains) s += d.Clog;
                return (Math.Clamp((s / w.Drains.Drains.Count - 0.45f) / 0.3f, 0f, 1f), null, -1);
            }
            case HazardKind.WeevilSwarm:
            {
                Weevils? top = null;
                foreach (var x in w.Eco.Pests) if (!x.Found && !x.Treated && (top == null || x.Pop > top.Pop)) top = x;
                return top == null ? (0f, null, -1) : (Math.Clamp((top.Pop - 0.25f) / 0.35f, 0f, 1f) * FittingSystem.PestMul(ship.Rooms.ElementAtOrDefault(top.RoomId)), ship.Rooms.ElementAtOrDefault(top.RoomId), top.Id);
            }
            case HazardKind.PlantTopple:
            {
                if (!w.ZeroG.Weightless && w.Maneuver.Recent.Count == 0) return (0f, null, -1);
                var pl = w.Eco.Plants.FirstOrDefault(p => !p.Dead && !p.Fixed && !p.Spilled);
                return pl == null ? (0f, null, -1) : (w.ZeroG.Weightless ? 0.7f : 0.4f, ship.Rooms.ElementAtOrDefault(pl.RoomId), pl.Id);
            }
            case HazardKind.MoonshineFire:
            {
                var s = w.Schemes.All.FirstOrDefault(x => x.Active && x.Spec.Key == "moonshine");
                return s == null || w.Fittings.Quiet.ContainsKey(-1 - s.RoomId) ? (0f, null, -1) : (ship.Rooms.ElementAtOrDefault(s.RoomId)?.Furniture.Any(f => f.Type == FurnitureType.LabStill) == true ? 0.8f : 0.6f, ship.Rooms.ElementAtOrDefault(s.RoomId), s.Id);
            }
            case HazardKind.BearingWhine:
            {
                Machine? worst = null;
                foreach (var m in ship.Machines) if (HazardsV18.Rotating(m.Body.Type) && !m.Body.Room.Detached && m.Faults.Count == 0 && (worst == null || m.Wear > worst.Wear)) worst = m;
                return worst == null ? (0f, null, -1) : (Math.Clamp((worst.Wear - 0.7f) / 0.25f, 0f, 1f), worst.Body.Room, worst.Body.Id);
            }
            case HazardKind.MeetingBrawl:
            {
                float s = 0f; int n = 0;
                foreach (var c in w.Crew) if (!c.Dead && !c.IsChild) { s += c.Needs.Stress; n++; }
                return n < 4 ? (0f, null, -1) : (Math.Clamp((s / n - 0.55f) / 0.3f, 0f, 1f) * FittingSystem.BoardMul(w), null, -1);
            }
            case HazardKind.CatLost:
                return w.Eco.Cat is ShipCat c2 && c2.State != CatState.Dead && c2.State != CatState.Hide && c2.Fear > 0.85f ? (0.5f, ship.Rooms.ElementAtOrDefault(c2.RoomId), -1) : (0f, null, -1);
        }
        return (0f, null, -1);
    }

    /// <summary>원인을 잴 수 있는 사고 (나머지는 전조가 서면 때가 될 때까지 간다).</summary>
    public static bool Measured(HazardKind k) => k is HazardKind.CatScratch or HazardKind.StaticZap or HazardKind.HeatExhaustion or HazardKind.SpaceSick or HazardKind.PotFire
        or HazardKind.FermentBurst or HazardKind.DrainBackflow or HazardKind.GreywaterJam or HazardKind.WeevilSwarm or HazardKind.PlantTopple or HazardKind.MoonshineFire
        or HazardKind.BearingWhine or HazardKind.MeetingBrawl or HazardKind.CatLost;

    /// <summary>손을 썼다: 원인을 누그러뜨린다 (전조를 본 사람이 하는 일). 무엇을 했는지 돌려준다.</summary>
    public static string Mitigate(World w, HazardKind k, Room? room, int refId, CrewMember by)
    {
        switch (k)
        {
            case HazardKind.CatScratch:
            case HazardKind.CatLost:
                if (w.Eco.Cat is ShipCat cat) { cat.Fear = MathF.Min(cat.Fear, 0.15f); cat.Fond[by.Id] = MathF.Min(1f, EcoSystem.Fond(cat, by.Id) + 0.1f); w.Eco.Stats.Pets++; }
                return "고양이를 천천히 쓰다듬어 달랬다";
            case HazardKind.StaticZap:
                if (room != null) room.Humidity = MathF.Max(room.Humidity, 0.42f);
                return "가습기를 틀고 옷에 물을 뿌렸다";
            case HazardKind.HeatExhaustion:
                if (room != null) room.Air.Temperature = MathF.Max(21f, room.Air.Temperature - 5f);
                return "환풍기를 돌리고 물을 나눴다";
            case HazardKind.SpaceSick:
                foreach (var c in w.Crew) if (!c.Dead && c.Room == room && w.ZeroG.Queasy.ContainsKey(c.Id)) w.ZeroG.Queasy[c.Id] *= 0.5f;
                return "멀미약과 봉지를 나눠 주었다";
            case HazardKind.PotFire:
                w.Fittings.PotOff(refId);
                return "조리대 불을 끄고 냄비를 내렸다";
            case HazardKind.FermentBurst:
                w.Fittings.Burp(refId, by);
                return "항아리 뚜껑을 열어 김을 뺐다";
            case HazardKind.DrainBackflow:
            case HazardKind.GreywaterJam:
                foreach (var d in w.Drains.Drains) if ((room == null || d.RoomId == room.Id) && d.Clog > 0.25f) { d.Clog = 0.2f; d.Food *= 0.5f; d.Cleared = w.Tick; w.Drains.Stats.Cleared++; }
                return "배수구 덮개를 열고 찌꺼기를 긁어냈다";
            case HazardKind.WeevilSwarm:
                foreach (var x in w.Eco.Pests) if (x.Id == refId && !x.Found) { x.Found = true; x.FoundBy = by.Id; w.Eco.Stats.FoundByCrew++; }
                return "곡물 자루를 뒤져 바구미 든 자루를 찾아냈다";
            case HazardKind.PlantTopple:
                foreach (var p in w.Eco.Plants) if (room != null && p.RoomId == room.Id && !p.Dead) p.Fixed = true;
                return "화분을 끈으로 선반에 묶었다";
            case HazardKind.MoonshineFire:
                w.Fittings.Cool(room);
                return "증류 통을 열원에서 떼어 놓았다";
            case HazardKind.BearingWhine:
                if (w.Ship.Furniture.FirstOrDefault(f => f.Id == refId)?.Machine is Machine m) { m.Wear = MathF.Max(0f, m.Wear - 0.12f); MarkLog.Add(m.Marks, w.Tick, $"{by.Name}: 베어링에 기름을 쳤다"); }
                return "베어링에 기름을 쳤다";
            case HazardKind.MeetingBrawl:
                foreach (var c in w.Crew.Where(c => !c.Dead && !c.IsChild).OrderByDescending(c => c.Needs.Stress).ThenBy(c => c.Id).Take(2)) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.15f);
                return "날이 선 둘을 따로 불러 이야기를 들었다";
        }
        return "살펴보았다";
    }
}

// ═══════════════════════════════ 사고를 건다 ═══════════════════════════════

public sealed partial class HazardSystem
{
    /// <summary>압축-마 새 사고 30.</summary>
    internal string? V18(HazardKind k, Cell at, int id)
    {
        var w = _w;
        var ship = w.Ship;
        var rng = w.Rng;
        var signs = w.Signs;
        var focus = signs.TakeFocus();
        var rooms = ship.Rooms.Where(r => !r.Detached && !r.Abandoned && r.Type != RoomType.Corridor).ToList();
        if (rooms.Count == 0) return null;
        Room? Pick(params RoomType[] t) => focus != null && t.Contains(focus.Type) ? focus : rooms.Where(r => t.Contains(r.Type)).OrderBy(r => r.Id).FirstOrDefault();
        Room? Busiest() => focus ?? rooms.OrderByDescending(r => w.Crew.Count(c => !c.Dead && c.Room == r && c.IsAwake)).ThenBy(r => r.Id).FirstOrDefault();
        CrewMember? Who() => w.Crew.FirstOrDefault(c => c.Id == id && !c.Dead && !c.Outside && c.Room != null);
        CrewMember? AwakeIn(Room? r) => r == null ? null : w.Crew.Where(c => !c.Dead && !c.Outside && c.Room == r && c.IsAwake).OrderBy(c => c.Id).FirstOrDefault();
        CrewMember? AnyAwake() => w.Crew.Where(c => !c.Dead && !c.Outside && c.Room != null && c.IsAwake && !c.Down).OrderBy(c => c.Id).FirstOrDefault();
        Cell Floor(Room r, Cell? near = null)
        {
            var cells = r.Cells.Where(ship.IsOpenFloor).ToList();
            if (cells.Count == 0) return r.Cells[0];
            if (near is Cell n) return cells.OrderBy(c => Math.Abs(c.X - n.X) + Math.Abs(c.Y - n.Y)).ThenBy(c => c.Y).ThenBy(c => c.X).First();
            return cells[rng.Range(0, cells.Count)];
        }
        string Inc(string text, Room? room, string ret, AlertLevel? alert = null, bool shipWide = false, IEnumerable<CrewMember>? crew = null)
        {
            if (alert is AlertLevel a) w.RaiseAlert(text, room, a, shipWide);
            w.History.Add(w, HistoryKind.Incident, text, room, crew);
            return ret;
        }
        void Saw(Room? r, float fright, string why) { if (r != null) foreach (var c in w.Crew) if (!c.Dead && c.Room == r && c.IsAwake) { Memory.Frighten(w, c, r, fright, why); signs.Witness(c, k); } }
        void Hurt(CrewMember c, float amt, string why, float shake = 0.05f) { NeedsSystem.AddInjury(c.Vitals, amt, why); if (shake > 0f) Memory.Shake(w, c, shake, why); MarkLog.Add(c.Memory.Marks, w.Tick, why); signs.Witness(c, k); }
        void Grime(Room r, SoilKind s, float v) { var a = w.Soil.RoomSoil(r); a[(int)s] = MathF.Min(1f, a[(int)s] + v); }

        switch (k)
        {
            // ─── ① 개인 ───
            case HazardKind.CatScratch:
            {
                if (w.Eco.Cat is not ShipCat cat || cat.State == CatState.Dead) return null;
                var room = ship.Rooms.ElementAtOrDefault(cat.RoomId);
                var c = AwakeIn(room) ?? w.Crew.FirstOrDefault(x => x.Id == cat.Favorite && !x.Dead && !x.Outside) ?? AnyAwake();
                if (c == null) return null;
                Hurt(c, 0.025f, "고양이에게 할퀴었다", 0.02f);
                cat.Fear = MathF.Max(cat.Fear, 0.75f);
                cat.Fond[c.Id] = MathF.Max(-1f, EcoSystem.Fond(cat, c.Id) - 0.25f);
                w.Eco.Startle("할퀴고 달아났다", c.Cell);
                c.Say(w, "아야 — 미안, 놀랐구나");
                signs.AddTrace(k, c.Room!, c.Cell, c.Id, SimTime.Hours(20));
                return Inc($"{Ko.IGa(c.Name)} 고양이 {cat.Name}에게 손등을 할퀴었다 — 고양이가 달아났다", c.Room, $"고양이 할큄({c.Name})", crew: new[] { c });
            }
            case HazardKind.StaticZap:
            {
                var c = Who() ?? AnyAwake();
                if (c == null) return null;
                var r = c.Room!;
                Hurt(c, 0.015f, "옷 정전기에 손끝이 튀었다", 0.02f);
                var dev = r.Furniture.Where(f => f.Machine is Machine m && m.Faults.Count == 0 && f.Type is FurnitureType.Console or FurnitureType.MainComputer or FurnitureType.SensorArray or FurnitureType.ServerRack).OrderBy(f => f.Id).FirstOrDefault();
                bool broke = dev != null && rng.Chance(r.Humidity < 0.3f ? 0.6f : 0.3f) && w.Machines.Break(dev.Machine!, FaultKind.ControlFault) != null;
                c.Say(w, "따끔!");
                signs.AddTrace(k, r, dev?.Cells[0] ?? c.Cell, c.Id, SimTime.Hours(30));
                return Inc($"{Ko.IGa(c.Name)} 콘솔에 손을 대다 정전기가 튀었다" + (broke ? $" — {dev!.Label} 제어부가 멎었다" : ""), r, $"옷 정전기({c.Name})", broke ? AlertLevel.Warning : null, crew: new[] { c });
            }
            case HazardKind.HeatExhaustion:
            {
                var hot = rooms.OrderByDescending(r => r.Air.Temperature).ThenBy(r => r.Id).First();
                var c = Who() ?? AwakeIn(hot) ?? AnyAwake();
                if (c == null) return null;
                Hurt(c, 0.05f, "더위에 어지러워 주저앉았다", 0.04f);
                c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.3f);
                c.Interrupt(w);
                c.Say(w, "잠깐… 앉아야겠어");
                Saw(c.Room, 0.05f, "열탈진");
                signs.AddTrace(k, c.Room!, c.Cell, c.Id, SimTime.Hours(14));
                return Inc($"{Ko.IGa(c.Name)} 더운 {c.Room!.Name}에서 일하다 어지러워 주저앉았다 ({c.Room.Air.Temperature:0}℃)", c.Room, $"열탈진({c.Name})", AlertLevel.Warning, crew: new[] { c });
            }
            case HazardKind.SpaceSick:
            {
                CrewMember? c = null;
                if (w.ZeroG.Weightless) { float q = -1f; foreach (var x in w.Crew) if (!x.Dead && !x.Outside && x.Room != null && w.ZeroG.Queasy.GetValueOrDefault(x.Id) > q) { q = w.ZeroG.Queasy.GetValueOrDefault(x.Id); c = x; } }
                c ??= AwakeIn(focus) ?? AnyAwake();
                if (c == null) return null;
                var r = c.Room!;
                if (w.ZeroG.Weightless) w.ZeroG.Queasy[c.Id] = 1f;
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.1f);
                Grime(r, SoilKind.Bio, 0.3f);
                w.Smells.Emit(r, SmellKind.Foul, 0.45f);
                MarkLog.Add(c.Memory.Marks, w.Tick, "속이 뒤집혀 토했다");
                Saw(r, 0.03f, "누가 토했다");
                signs.AddTrace(k, r, c.Cell, c.Id, SimTime.Hours(16));
                return Inc($"{Ko.IGa(c.Name)} 멀미를 견디지 못하고 토했다 — {r.Name} 바닥이 더러워졌다", r, $"멀미({c.Name})", crew: new[] { c });
            }
            case HazardKind.HatchFall:
            {
                var c = Who() ?? AnyAwake();
                if (c == null) return null;
                Hurt(c, 0.11f, "열린 점검 뚜껑에 발이 빠졌다", 0.06f);
                c.Interrupt(w);
                Memory.Frighten(w, c, c.Room, 0.12f, "열린 점검 뚜껑");
                c.Say(w, "누가 뚜껑을 열어 뒀어!");
                signs.AddTrace(k, c.Room!, c.Cell, c.Id, SimTime.Hours(10));
                return Inc($"{Ko.IGa(c.Name)} 열어 둔 점검 뚜껑에 발이 빠져 정강이를 다쳤다", c.Room, $"점검 뚜껑 낙상({c.Name})", AlertLevel.Warning, crew: new[] { c });
            }
            case HazardKind.ScaldSpill:
            {
                var r = Pick(RoomType.Galley, RoomType.Mess) ?? Busiest();
                var c = AwakeIn(r) ?? AnyAwake();
                if (c == null || c.Room == null) return null;
                r = c.Room;
                Hurt(c, 0.06f, "끓는 국에 손을 데었다", 0.03f);
                Grime(r, SoilKind.Oil, 0.2f);
                w.Moisture.AddWater(r, 3f);
                c.Say(w, "앗 뜨거!");
                signs.AddTrace(k, r, Floor(r, c.Cell), c.Id, SimTime.Hours(8));
                return Inc($"{Ko.IGa(c.Name)} 국 냄비를 엎어 손을 데었다 — 바닥이 미끄럽다", r, $"국 엎음({c.Name})", crew: new[] { c });
            }
            // ─── ② 방 ───
            case HazardKind.PotFire:
            {
                var st = ship.FurnitureOf(FurnitureType.Stove).OrderBy(f => focus != null && f.Room == focus ? 0 : 1).ThenBy(f => f.Id).FirstOrDefault();
                if (st == null) return null;
                var cell = Floor(st.Room, st.UseSpots.Count > 0 ? st.UseSpots[0] : st.Cells[0]);
                if (!Incidents.Fire(w, cell)) return null;
                w.Smells.Emit(st.Room, SmellKind.Burnt, 0.6f);
                if (st.Machine is Machine sm) MarkLog.Add(sm.Marks, w.Tick, "냄비가 타올랐다");
                signs.AddTrace(k, st.Room, st.Cells[0], -1, SimTime.Hours(36));
                Saw(st.Room, 0.15f, "냄비 화재");
                return Inc($"{st.Room.Name} 조리대 위 냄비가 타올라 불이 붙었다", st.Room, $"냄비 화재({st.Room.Name})", AlertLevel.Critical, true);
            }
            case HazardKind.FermentBurst:
            {
                var crock = ship.FurnitureOf(FurnitureType.Fermenter).OrderBy(f => focus != null && f.Room == focus ? 0 : 1).ThenBy(f => f.Id).FirstOrDefault();
                var r = crock?.Room ?? Pick(RoomType.Galley, RoomType.Storage, RoomType.Mess);
                if (r == null) return null;
                if (crock?.Machine is Machine cm && cm.Faults.Count == 0) w.Machines.Break(cm, FaultKind.SealWorn);
                w.Fittings.Burp(crock?.Id ?? -1, null);
                Grime(r, SoilKind.Bio, 0.35f);
                w.Smells.Emit(r, SmellKind.Foul, 0.6f);
                var near = AwakeIn(r);
                if (near != null && rng.Chance(0.4f)) Hurt(near, 0.03f, "튄 항아리 조각에 베였다", 0.02f);
                Saw(r, 0.06f, "항아리가 터졌다");
                signs.AddTrace(k, r, crock?.Cells[0] ?? Floor(r), near?.Id ?? -1, SimTime.Hours(20));
                return Inc($"{r.Name} 발효 항아리가 부풀어 뚜껑이 날아갔다 — 시큼한 국물이 바닥에 번졌다", r, $"발효 항아리 터짐({r.Name})", AlertLevel.Warning);
            }
            case HazardKind.HeaterOverload:
            {
                var r = focus ?? rooms.OrderBy(x => x.Air.Temperature).ThenBy(x => x.Id).First();
                var panel = ship.FurnitureOf(FurnitureType.PowerPanel).OrderBy(f => f.Id).FirstOrDefault()?.Machine;
                if (panel != null)
                {
                    int ci = rng.Range(0, PowerGrid.CircuitCount);
                    if (!panel.Faults.Any(f => f.Circuit == ci)) { panel.Faults.Add(new Fault { Kind = FaultKind.BreakerTrip, Since = w.Tick, Circuit = ci }); w.Causes.OnFault(panel, panel.Faults[^1]); panel.FaultCount++; }
                }
                var cell = Floor(r);
                bool fire = rng.Chance(0.35f) && Incidents.Fire(w, cell);
                w.Smells.Emit(r, SmellKind.Burnt, 0.3f);
                Saw(r, 0.05f, "난로 전선이 그을렸다");
                signs.AddTrace(k, r, cell, -1, SimTime.Hours(30));
                return Inc($"{r.Name} 이동식 난로가 회로를 넘겨 차단기가 떨어졌다" + (fire ? " — 전선에서 불꽃이 일었다" : " — 전선이 그을렸다"), r, $"난로 과부하({r.Name})", fire ? AlertLevel.Critical : AlertLevel.Warning, fire);
            }
            case HazardKind.PumpShock:
            {
                var r = focus ?? rooms.OrderByDescending(x => x.Humidity).ThenBy(x => x.Id).First();
                var c = AwakeIn(r) ?? AnyAwake();
                if (c == null || c.Room == null) return null;
                r = c.Room;
                w.Moisture.AddWater(r, 8f);
                Hurt(c, 0.07f, "젖은 손으로 양수기를 잡다 감전됐다", 0.06f);
                Memory.Frighten(w, c, r, 0.1f, "물 위의 전기");
                c.Say(w, "윽 — 전기 내려!");
                signs.AddTrace(k, r, Floor(r, c.Cell), c.Id, SimTime.Hours(18));
                return Inc($"{Ko.IGa(c.Name)} 물 고인 {r.Name}에서 양수기를 옮기다 감전됐다", r, $"양수기 감전({c.Name})", AlertLevel.Warning, crew: new[] { c });
            }
            case HazardKind.DrainBackflow:
            {
                var d = w.Drains.Drains.Where(x => !x.Backflow).OrderBy(x => focus != null && x.RoomId == focus.Id ? 0 : 1).ThenByDescending(x => x.Clog).ThenBy(x => x.Id).FirstOrDefault();
                if (d == null || ship.Rooms.ElementAtOrDefault(d.RoomId) is not Room r) return null;
                w.Drains.ForceClog(d, 0.6f);
                Grime(r, SoilKind.Bio, 0.25f);
                Saw(r, 0.04f, "배수구가 역류했다");
                signs.AddTrace(k, r, d.At, -1, SimTime.Hours(24));
                return Inc($"{r.Name} {DrainSystem.Name(d.Kind)}가 거꾸로 차올랐다 — 냄새가 번진다", r, $"배수구 역류({r.Name})", AlertLevel.Warning);
            }
            case HazardKind.WeevilSwarm:
            {
                var shelves = ship.FurnitureOf(FurnitureType.Shelf).Where(f => f.Room.Type is RoomType.Storage or RoomType.Galley or RoomType.Cargo or RoomType.Freezer or RoomType.Mess)
                    .OrderBy(f => focus != null && f.Room == focus ? 0 : 1).ThenBy(f => f.Id).Take(2).ToList();
                if (shelves.Count == 0) return null;
                foreach (var sh in shelves) w.Eco.Seed(sh, rng.Range(0.3f, 0.5f) * FittingSystem.PestMul(sh.Room));
                signs.AddTrace(k, shelves[0].Room, shelves[0].Cells[0], -1, SimTime.Hours(40));
                return Inc($"{shelves[0].Room.Name} 곡물 자루의 바구미가 선반 {shelves.Count}곳으로 번졌다", shelves[0].Room, $"바구미 번짐({shelves[0].Room.Name})");
            }
            case HazardKind.PlantTopple:
            {
                var pl = w.Eco.Plants.Where(p => !p.Dead && !p.Spilled && !p.Fixed).OrderBy(p => focus != null && p.RoomId == focus.Id ? 0 : 1).ThenBy(p => p.Id).FirstOrDefault();
                if (pl == null || ship.Rooms.ElementAtOrDefault(pl.RoomId) is not Room r) return null;
                pl.Spilled = true;
                pl.Health = MathF.Max(0.05f, pl.Health - 0.3f);
                Grime(r, SoilKind.Dust, 0.3f);
                Saw(r, 0.02f, "화분이 깨졌다");
                signs.AddTrace(k, r, pl.At, -1, SimTime.Hours(20));
                return Inc($"{r.Name} {pl.Name} 화분이 떨어져 깨졌다 — 흙이 쏟아졌다", r, $"화분 엎어짐({r.Name})");
            }
            case HazardKind.MoonshineFire:
            {
                var sch = w.Schemes.All.FirstOrDefault(x => x.Active && x.Spec.Key == "moonshine");
                var r = (sch != null ? ship.Rooms.ElementAtOrDefault(sch.RoomId) : null) ?? Pick(RoomType.Engine, RoomType.Storage, RoomType.Cargo, RoomType.Workshop);
                if (r == null || r.Detached) return null;
                var cell = Floor(r);
                if (!Incidents.Fire(w, cell)) return null;
                w.Smells.Emit(r, SmellKind.Burnt, 0.5f);
                Saw(r, 0.12f, "몰래 둔 통에서 불");
                if (sch != null) foreach (var cid in sch.Crew) if (w.Crew.FirstOrDefault(x => x.Id == cid) is CrewMember who) { who.Needs.Stress = MathF.Min(1f, who.Needs.Stress + 0.2f); MarkLog.Add(who.Memory.Marks, w.Tick, "몰래 담그던 통에서 불이 났다"); }
                signs.AddTrace(k, r, cell, sch?.Lead ?? -1, SimTime.Hours(48));
                return Inc($"{r.Name} 구석의 증류 통에서 불이 붙었다 — 달큰한 알코올 냄새", r, $"밀주 통 불({r.Name})", AlertLevel.Critical, true);
            }
            case HazardKind.PartitionFall:
            {
                var r = Busiest();
                var c = AwakeIn(r) ?? AnyAwake();
                if (c == null || c.Room == null) return null;
                r = c.Room;
                Hurt(c, 0.08f, "넘어지는 칸막이 판에 어깨를 맞았다", 0.05f);
                Saw(r, 0.08f, "칸막이가 넘어졌다");
                signs.AddTrace(k, r, Floor(r, c.Cell), c.Id, SimTime.Hours(24));
                return Inc($"{r.Name} 칸막이 판이 넘어져 {Ko.EulReul(c.Name)} 덮쳤다", r, $"칸막이 넘어짐({r.Name})", AlertLevel.Warning, crew: new[] { c });
            }
            case HazardKind.LockedIn:
            {
                Door? door = null; CrewMember? inside = null;
                foreach (var d in ship.Doors.Where(d => !d.IsExternal && !d.MotorBroken).OrderBy(d => d.Cell.Y).ThenBy(d => d.Cell.X))
                {
                    foreach (var dir in Cell.Dirs4)
                        if (ship.RoomAt(d.Cell + dir) is Room rr && rr.Type != RoomType.Corridor && AwakeIn(rr) is CrewMember cc) { door = d; inside = cc; break; }
                    if (door != null && (focus == null || inside!.Room == focus)) break;
                }
                if (door == null || inside == null) return null;
                if (Jam(door) == null) return null;
                inside.Needs.Stress = MathF.Min(1f, inside.Needs.Stress + 0.15f);
                Memory.Frighten(w, inside, inside.Room, 0.15f, "문이 잠겨 갇혔다");
                inside.Say(w, "문이 안 열려! 누구 없어?");
                signs.Witness(inside, k);
                signs.AddTrace(k, inside.Room!, door.Cell, inside.Id, SimTime.Hours(30));
                return Inc($"{inside.Room!.Name} 문 구동기가 걸려 {Ko.IGa(inside.Name)} 갇혔다", inside.Room, $"문 잠겨 갇힘({inside.Name})", AlertLevel.Warning, crew: new[] { inside });
            }
            case HazardKind.BearingWhine:
            {
                var f = Hazards.MachineAt(w, k, at) ?? ship.Machines.Where(m => HazardsV18.Rotating(m.Body.Type) && !m.Body.Room.Detached).OrderByDescending(m => m.Wear).ThenBy(m => m.Body.Id).FirstOrDefault()?.Body;
                if (f?.Machine is not Machine m) return null;
                m.Wear = MathF.Min(0.98f, m.Wear + 0.15f);
                MarkLog.Add(m.Marks, w.Tick, "베어링이 높은 소리로 운다");
                int woke = 0;
                foreach (var c in w.Crew)
                    if (!c.Dead && c.Room != null && (c.Room == f.Room || System.Numerics.Vector2.Distance(c.Room.Center, f.Room.Center) < 9f))
                    {
                        if (c.Pose == Pose.Sleeping) { c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.15f); woke++; }
                        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.04f);
                        signs.Witness(c, k);
                    }
                signs.AddTrace(k, f.Room, f.Cells[0], -1, SimTime.Hours(30));
                return Inc($"{m.Name} 베어링이 높은 소리로 운다" + (woke > 0 ? $" — {woke}명이 잠을 설쳤다" : ""), f.Room, $"베어링 울음({m.Name})");
            }
            case HazardKind.MeetingBrawl:
            {
                var r = Pick(RoomType.MeetingRoom, RoomType.Mess, RoomType.Lounge) ?? Busiest();
                var two = w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Outside && c.Room != null && c.IsAwake).OrderByDescending(c => c.Room == r ? 1 : 0).ThenByDescending(c => c.Needs.Stress).ThenBy(c => c.Id).Take(2).ToList();
                if (two.Count < 2) return null;
                var (a, b) = (two[0], two[1]);
                r = a.Room!;
                Hurt(b, 0.03f, $"{a.Name}에게 멱살을 잡혔다", 0.04f);
                a.Needs.Stress = MathF.Min(1f, a.Needs.Stress + 0.1f);
                w.Relations.Remember(b, a, RelationReason.BlamedMe, "회의 중 멱살을 잡았다");
                a.Say(w, "그만 좀 해!");
                Saw(r, 0.05f, "회의 중 몸싸움");
                signs.AddTrace(k, r, Floor(r, a.Cell), a.Id, SimTime.Hours(20));
                return Inc($"{r.Name}에서 {Ko.WaGwa(a.Name)} {b.Name} 사이에 말이 날카로워지다 멱살잡이가 됐다", r, $"회의 몸싸움({a.Name})", AlertLevel.Warning, crew: two);
            }
            // ─── ③ 계통 ───
            case HazardKind.GravityHiccup:
            {
                if (w.ZeroG.Weightless) return null;
                w.ZeroG.Begin("중력 장치가 잠깐 끊겼다", repair: false);
                w.ZeroG.ScheduleRestore(true, "중력 장치가 다시 붙었다");
                var r = w.ZeroG.RingRoom ?? Pick(RoomType.Engine, RoomType.Power) ?? rooms[0];
                signs.AddTrace(k, r, Floor(r), -1, SimTime.Hours(16));
                return Inc("중력 장치가 몇 분 끊겼다 — 물건이 떠오른다", r, "중력 끊김", AlertLevel.Warning, true);
            }
            case HazardKind.ManeuverJolt:
            {
                var dir = rng.Chance(0.5f) ? new System.Numerics.Vector2(1f, 0f) : new System.Numerics.Vector2(0f, 1f);
                if (rng.Chance(0.5f)) dir = -dir;
                w.Maneuver.Shock(0.55f * FittingSystem.StrapMul(w), dir, null, "예고 없는 자세 제어 분사", false);
                var r = Pick(RoomType.Bridge, RoomType.Engine) ?? rooms[0];
                signs.AddTrace(k, r, Floor(r), -1, SimTime.Hours(20));
                return Inc("자세 제어 분사가 예고 없이 터졌다 — 배가 덜컥 기울었다", r, "예고 없는 분사", AlertLevel.Warning, true);
            }
            case HazardKind.RebootGlitch:
            {
                var core = ship.FurnitureOf(FurnitureType.MainComputer).FirstOrDefault()?.Machine;
                if (core != null && core.Faults.Count == 0) w.Machines.Break(core, FaultKind.FirmwareCrash);
                int dark = 0;
                foreach (var r in rooms.OrderBy(x => x.Id).Where(x => !x.LightsOut).Take(FittingSystem.ServerMul(w) < 1f ? 1 : 2)) if (Lights(r) != null) dark++;
                var cr = core?.Body.Room ?? Pick(RoomType.Bridge) ?? rooms[0];
                signs.AddTrace(k, cr, core?.Body.Cells[0] ?? Floor(cr), -1, SimTime.Hours(12));
                return Inc($"주컴퓨터가 다시 켜지는 사이 조명 제어가 엉켰다 — 방 {dark}곳이 캄캄하다", cr, "재부팅 중 사고", AlertLevel.Warning, true);
            }
            case HazardKind.BlackboxGap:
            {
                var bb = w.Blackbox;
                if (!bb.Recording || bb.Room is not Room br) return null;
                long to = w.Tick, from = Math.Max(0, to - SimTime.Hours(rng.Range(2f, 5f)));
                bool vault = FittingSystem.Vaulted(w);
                if (!vault) bb.Wipes.Add(new BoxWipe { Id = bb.Wipes.Count + 1000, From = from, To = to, Room = -1, At = w.Tick, Wiper = -1, Access = "전원 끊김" });
                bb.Stats.Damaged++;
                foreach (var c in w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).Take(3)) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f);
                signs.AddTrace(k, br, Floor(br), -1, SimTime.Hours(40));
                return Inc(vault ? "블랙박스 전원이 잠깐 끊겼다 — 기록 금고의 사본이 빈자리를 메웠다" : $"블랙박스 기록 {(to - from) / (float)SimTime.TicksPerHour:0}시간이 비었다 — 누가 지웠을까", br, "기록 끊김", AlertLevel.Notice, true);
            }
            case HazardKind.GreywaterJam:
            {
                var ds = w.Drains.Drains.Where(d => !d.Backflow).OrderByDescending(d => d.Clog).ThenBy(d => d.Id).Take(3).ToList();
                if (ds.Count == 0) return null;
                foreach (var d in ds) { d.Clog = MathF.Max(d.Clog, 0.8f); d.Food = MathF.Max(d.Food, 0.4f); }
                w.Drains.ForceClog(ds[0], 0.4f);
                foreach (var d in ds) if (ship.Rooms.ElementAtOrDefault(d.RoomId) is Room dr) signs.AddTrace(k, dr, d.At, -1, SimTime.Hours(24));
                return Inc($"회색수 계통이 막혀 배수 {ds.Count}곳이 한꺼번에 느려졌다", ship.Rooms.ElementAtOrDefault(ds[0].RoomId), "회색수 계통 막힘", AlertLevel.Warning, true);
            }
            case HazardKind.CatLost:
            {
                if (w.Eco.Cat is not ShipCat cat || cat.State is CatState.Dead or CatState.Hide) return null;
                cat.Fear = 1f;
                w.Eco.Startle("크게 놀라 어디론가 사라졌다", null);
                var r = ship.Rooms.ElementAtOrDefault(cat.HideRoom >= 0 ? cat.HideRoom : cat.RoomId) ?? rooms[0];
                var bowlRoom = w.Eco.Bowl is Cell bc ? ship.RoomAt(bc) ?? r : r;
                signs.AddTrace(k, bowlRoom, w.Eco.Bowl ?? Floor(bowlRoom), cat.Favorite, SimTime.Hours(36));
                foreach (var c in w.Crew) if (!c.Dead && EcoSystem.Fond(cat, c.Id) > 0.3f) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.08f);
                return Inc($"고양이 {Ko.IGa(cat.Name)} 크게 놀라 어딘가 숨어 버렸다", bowlRoom, "고양이 실종", AlertLevel.Notice, true);
            }
            // ─── ④ 배 전체 ───
            case HazardKind.GravityFailure:
            {
                if (w.ZeroG.Weightless) return null;
                w.ZeroG.Begin("중력 장치가 고장 났다", repair: true, power: true);
                var r = w.ZeroG.RingRoom ?? Pick(RoomType.Engine, RoomType.Power) ?? rooms[0];
                foreach (var c in w.Crew) if (!c.Dead && c.IsAwake) { Memory.Shake(w, c, 0.04f, "중력이 꺼졌다"); signs.Witness(c, k); }
                signs.AddTrace(k, r, Floor(r), -1, SimTime.Hours(48));
                return Inc("중력 장치가 고장 났다 — 고칠 때까지 배 전체가 떠다닌다", r, "중력 장치 고장", AlertLevel.Critical, true);
            }
            case HazardKind.DockSealFail:
            {
                var r = Pick(RoomType.DockingBay, RoomType.Airlock, RoomType.EvaPrep);
                if (r == null) return null;
                var wall = ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == r).Select(kv => (Cell?)kv.Key).OrderBy(c => c!.Value.Y).ThenBy(c => c!.Value.X).FirstOrDefault();
                float loss = 45f * FittingSystem.ClampMul(w);
                w.Air.Reserve = MathF.Max(0f, w.Air.Reserve - loss);
                string? crack = Crack(wall);
                Saw(r, 0.15f, "접안 고리에서 공기가 샜다");
                signs.AddTrace(k, r, wall ?? Floor(r), -1, SimTime.Hours(48));
                return Inc($"접안 고리 씰이 맞물리지 않았다 — {r.Name} 쪽에서 공기가 샌다" + (crack != null ? " · 외벽이 갈라졌다" : ""), r, "도킹 기밀 실패", AlertLevel.Critical, true);
            }
            case HazardKind.WreckDrift:
            {
                var side = rooms.Where(r => ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == r)).OrderBy(r => r.Id).ToList();
                if (side.Count == 0) return null;
                int n = Math.Min(side.Count, 4);
                for (int i = 0; i < n; i++) Shower.Add((w.Tick + SimTime.Minutes(rng.Range(5f, 40f)), Scenarios.OuterTarget(w, side[rng.Range(0, side.Count)]), rng.Range(0.25f, 0.45f)));
                Shower.Sort((a, b) => a.tick.CompareTo(b.tick));
                var br = Pick(RoomType.Bridge, RoomType.Comms) ?? rooms[0];
                signs.AddTrace(k, br, Floor(br), -1, SimTime.Hours(30));
                return Inc($"부서진 배의 조각 {n}개가 흩어져 날아든다", br, "난파선 잔해 떼", AlertLevel.Critical, true);
            }
            // ─── ⑤ 우주급 ───
            case HazardKind.GammaFlash:
            {
                int sick = 0;
                foreach (var c in w.Crew.Where(c => !c.Dead))
                {
                    float dose = (c.Outside ? 2.5f : 0.5f * (c.Room != null ? w.Ambience.Exposure(c.Room) : 1f)) * TechWeb.Mul(w, "cosmic.expose");
                    c.Dose += dose;
                    if (dose > 1f && rng.Chance(0.5f) && w.Ailments.Catch(c, "radiation", null, "감마선 섬광") != null) sick++;
                    Memory.Shake(w, c, 0.03f, "감마선 섬광");
                }
                foreach (var m in ship.Machines) m.SensorCal = MathF.Max(0.3f, m.SensorCal - 0.3f);
                var r = Pick(RoomType.Observatory, RoomType.Lounge, RoomType.Bridge) ?? rooms[0];
                var hull = ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == r).Select(kv => (Cell?)kv.Key).OrderBy(c => c!.Value.X).FirstOrDefault();
                signs.AddTrace(k, r, hull ?? Floor(r), -1, SimTime.Hours(72));
                return Inc($"감마선 섬광이 배를 훑었다 — 모두가 조금씩 쬐었고 감지기가 하얗게 탔다 (앓는 사람 {sick})", r, "감마선 섬광", AlertLevel.Critical, true);
            }
            case HazardKind.TidalPull:
            {
                foreach (var (_, wall) in ship.Walls.Where(kv => kv.Value.IsHull)) wall.MaxIntegrity = MathF.Max(0.5f, wall.MaxIntegrity - 0.015f);
                w.Maneuver.Shock(0.35f * FittingSystem.StrapMul(w), new System.Numerics.Vector2(0f, 1f), null, "조석 당김", true);
                foreach (var c in w.Crew) if (!c.Dead && c.IsAwake) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f);
                var r = rooms.OrderByDescending(x => x.Cells.Count).ThenBy(x => x.Id).First();
                signs.AddTrace(k, r, Floor(r), -1, SimTime.Hours(60));
                return Inc("무거운 천체 곁을 지나며 배가 길게 당겨졌다 — 골조가 신음한다", r, "조석 당김", AlertLevel.Critical, true);
            }
            case HazardKind.MagnetarPulse:
            {
                int hit = 0;
                foreach (var m in ship.Machines.Where(m => Hazards.IsElectronic(m.Body) && m.Faults.Count == 0).OrderBy(m => m.Body.Id).Take(3)) if (w.Machines.Break(m, FaultKind.ControlFault) != null) hit++;
                foreach (var c in w.Crew) if (!c.Dead && c.Pose == Pose.Sleeping) c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.1f);
                var r = Pick(RoomType.Workshop, RoomType.WeldingShop, RoomType.Storage) ?? rooms[0];
                var wall = ship.Walls.Where(kv => Hull.InsideRoom(ship, kv.Key) == r).Select(kv => (Cell?)kv.Key).OrderBy(c => c!.Value.Y).ThenBy(c => c!.Value.X).FirstOrDefault();
                signs.AddTrace(k, r, wall ?? Floor(r), -1, SimTime.Hours(48));
                return Inc($"자기 폭풍 파동 — 전자 장비 {hit}대가 튀고 공구가 벽에 들러붙었다", r, "자기 폭풍 파동", AlertLevel.Critical, true);
            }
        }
        return null;
    }
}

// ═══════════════════════════════ 전조 · 흔적 · 기억 ═══════════════════════════════

/// <summary>사고가 남긴 흔적 (그림이 사고마다 다르게 그린다 — 시간이 지나면 옅어지고, 치우면 사라진다).</summary>
public sealed class IncidentTrace
{
    public int Id { get; init; }
    public HazardKind Kind { get; init; }
    public int RoomId { get; init; }
    public Cell At { get; init; }
    public long Tick { get; init; }
    public long Until { get; set; }
    public int Who { get; init; } = -1;
    public int ClaimedBy { get; set; } = -1;
    public bool Cleaned { get; set; }
    /// <summary>0~1 남은 정도 (그림의 진하기).</summary>
    public float Left(long now) => Cleaned ? 0f : Math.Clamp((Until - now) / (float)Math.Max(1L, Until - Tick), 0f, 1f);
}

/// <summary>무르익은 원인의 전조: 누가 보았나(사람 · 컴퓨터) · 손쓸 사람 · 언제 터지나.</summary>
public sealed class HazardSign
{
    public int Id { get; init; }
    public HazardKind Kind { get; init; }
    public int RoomId { get; init; } = -1;
    public int Ref { get; init; } = -1;
    public long Since { get; init; }
    public long Due { get; init; }
    public int Claim { get; set; } = -1;
    /// <summary>0 아무도 · 1 사람이 봤다 · 2 컴퓨터가 쟀다.</summary>
    public int Noticed { get; set; }
    public bool Done { get; set; }
}

public sealed class SignStats
{
    public int Signs, ByCrew, ByComputer, Averted, Fired, Traces, Cleaned, Recalled;
    public string Line() => $"전조 {Signs} (사람 {ByCrew} · 컴퓨터 {ByComputer} · 경험 {Recalled}) · 막음 {Averted} · 터짐 {Fired} · 흔적 {Traces} (치움 {Cleaned})";
}

public sealed class HazardSignSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 1013));
    public HazardSignSystem(World w) => _w = w;
    public static bool Off;

    public SignStats Stats { get; } = new();
    public List<IncidentTrace> Traces { get; } = new();
    public List<HazardSign> Signs { get; } = new();
    /// <summary>사람이 겪거나 본 사고 (crew id × 64 + 종류) — 다음엔 그 전조를 더 빨리 알아챈다.</summary>
    public SortedSet<long> Seen { get; } = new();
    private Room? _focus;
    private long _next;
    private int _nextId = 1;

    /// <summary>전조가 터질 때 그 방에서 나게 한다 (V18 이 한 번 꺼내 쓴다).</summary>
    internal Room? TakeFocus() { var f = _focus; _focus = null; return f; }

    private static long SeenKey(int crew, HazardKind k) => (long)crew * 64 + (k - HazardsV18.First);
    public bool HasSeen(CrewMember c, HazardKind k) => Seen.Contains(SeenKey(c.Id, k));
    internal void Witness(CrewMember c, HazardKind k) { if (HazardsV18.Is(k)) Seen.Add(SeenKey(c.Id, k)); }

    public void AddTrace(HazardKind k, Room? r, Cell at, int who, long life)
    {
        r ??= _w.Ship.RoomAt(at); // 통합8 문간에 선 사람은 방이 없다 (c.Room!) — 그 칸의 방으로, 그것도 없으면 흔적을 남기지 않는다
        if (r == null) return;
        Traces.Add(new IncidentTrace { Id = _nextId++, Kind = k, RoomId = r.Id, At = at, Tick = _w.Tick, Until = _w.Tick + life, Who = who });
        Stats.Traces++;
        MarkLog.Add(r.Marks, _w.Tick, $"{Hazards.Name(k)} — {HazardsV18.Of(k)?.Trace}");
        if (Traces.Count > 60) Traces.RemoveAt(0);
    }

    /// <summary>전조를 하나 세운다 (시험 · 이야기꾼도 쓴다). 이미 같은 종류가 서 있으면 그대로.</summary>
    public HazardSign? Raise(HazardKind k, Room? room, int refId, float hours)
    {
        var w = _w;
        if (Signs.Any(s => !s.Done && s.Kind == k)) return null;
        var more = HazardsV18.Of(k);
        if (more == null) return null;
        var s = new HazardSign { Id = _nextId++, Kind = k, RoomId = room?.Id ?? -1, Ref = refId, Since = w.Tick, Due = w.Tick + SimTime.Hours(hours) };
        Signs.Add(s);
        Stats.Signs++;
        // 사람이 본다: 그 방의 깨어 있는 사람 (겪어 본 사람은 거의 늘 · 처음이면 반쯤)
        foreach (var c in w.Crew.Where(c => !c.Dead && c.IsAwake && !c.IsChild && room != null && c.Room == room).OrderBy(c => c.Id))
        {
            bool knew = HasSeen(c, k);
            if (!R.Chance(knew ? 0.9f : 0.5f)) continue;
            s.Noticed = 1; s.Claim = c.Id;
            Stats.ByCrew++;
            if (knew) Stats.Recalled++;
            MarkLog.Add(c.Memory.Marks, w.Tick, $"{more.Omen} — {(knew ? "전에 본 적 있다" : "뭔가 이상하다")}");
            c.Say(w, knew ? $"{more.Omen}… 저번처럼 되겠어" : $"{more.Omen}?");
            w.Log.Add(w.Tick, LogKind.Life, $"{room!.Name}: {more.Omen} — {(knew ? "전에 겪어 본 일이라 바로 알아챘다" : "이상한 낌새를 챘다")}", c.Id);
            break;
        }
        // 컴퓨터가 잰다: 센서로 보이는 것 (사람이 못 봤으면 사람을 부른다)
        if (more.Sensor && w.Automation.MainOnline)
        {
            if (s.Noticed == 0) Stats.ByComputer++;
            s.Noticed = Math.Max(s.Noticed, 2);
            w.RaiseAlert($"{(room != null ? room.Name + " — " : "")}{more.Omen} ({more.Cause} · 손보지 않으면 {Hazards.Name(k)})", room, AlertLevel.Notice, false);
            if (s.Claim < 0)
            {
                var pick = w.Crew.Where(c => !c.Dead && c.IsAwake && !c.IsChild && !c.Down && !c.Outside && c.Room != null)
                    .OrderBy(c => c.Job?.Activity is OmenCheckActivity ? 1 : 0).ThenBy(c => room == null ? 0f : System.Numerics.Vector2.Distance(c.Room!.Center, room.Center)).ThenBy(c => c.Id).FirstOrDefault();
                if (pick != null) { s.Claim = pick.Id; w.Log.Add(w.Tick, LogKind.Ship, $"주컴퓨터: {pick.Name}, {(room != null ? room.Name + "의 " : "")}{Ko.EulReul(more.Cause)} 봐 주세요", pick.Id); }
            }
        }
        return s;
    }

    /// <summary>전조를 본 사람이 손을 썼다.</summary>
    public void Resolve(HazardSign s, CrewMember by)
    {
        if (s.Done) return;
        var w = _w;
        var room = s.RoomId >= 0 ? w.Ship.Rooms.ElementAtOrDefault(s.RoomId) : by.Room;
        string what = HazardsV18.Mitigate(w, s.Kind, room, s.Ref, by);
        s.Done = true;
        Stats.Averted++;
        Witness(by, s.Kind);
        Memory.Steady(w, by, 0.03f);
        MarkLog.Add(by.Memory.Marks, w.Tick, $"{what} — {Ko.EulReul(Hazards.Name(s.Kind))} 막았다");
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(by.Name)} {what} ({Ko.EulReul(Hazards.Name(s.Kind))} 막았다)", by.Id);
    }

    public void CleanTrace(IncidentTrace t, CrewMember by)
    {
        if (t.Cleaned) return;
        t.Cleaned = true;
        Stats.Cleaned++;
        if (_w.Ship.Rooms.ElementAtOrDefault(t.RoomId) is Room r)
        {
            var a = _w.Soil.RoomSoil(r);
            for (int i = 0; i < a.Length; i++) a[i] = MathF.Max(0f, a[i] - 0.15f);
        }
        _w.Log.Add(_w.Tick, LogKind.Work, $"{Ko.IGa(by.Name)} {Ko.EulReul(HazardsV18.Of(t.Kind)?.Trace ?? "흔적")} 치웠다", by.Id);
    }

    /// <summary>치울 만한 흔적 (바닥에 남은 것).</summary>
    public static bool Messy(HazardKind k) => k is HazardKind.SpaceSick or HazardKind.ScaldSpill or HazardKind.FermentBurst or HazardKind.DrainBackflow or HazardKind.PlantTopple
        or HazardKind.PartitionFall or HazardKind.MeetingBrawl or HazardKind.GreywaterJam or HazardKind.HatchFall;

    public void Update(float dt)
    {
        if (Off) return;
        var w = _w;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(20);
        // 흔적: 옅어진 것은 지운다
        for (int i = Traces.Count - 1; i >= 0; i--) if (Traces[i].Cleaned || w.Tick > Traces[i].Until) Traces.RemoveAt(i);
        // 전조: 손쓴 것 · 원인이 사라진 것 · 때가 된 것
        foreach (var s in Signs)
        {
            if (s.Done) continue;
            var (p, _, _) = HazardsV18.Pressure(w, s.Kind);
            if (p < 0.15f && HazardsV18.Measured(s.Kind)) { s.Done = true; Stats.Averted++; continue; } // 원인이 사라졌다 (누가 손썼거나 저절로)
            if (w.Tick < s.Due) continue;
            s.Done = true;
            _focus = s.RoomId >= 0 ? w.Ship.Rooms.ElementAtOrDefault(s.RoomId) : null;
            var what = w.Hazards.FireStory(s.Kind.ToString(), _focus);
            _focus = null;
            if (what != null) Stats.Fired++;
        }
        Signs.RemoveAll(s => s.Done && w.Tick - s.Since > SimTime.Hours(24));
        // 새 전조: 원인이 무르익은 것 (사고가 꺼진 배 — 무작위 사고 · 이야기꾼 모두 끔 — 에서는 원인만 쌓이고 터지지 않는다)
        if (HazardSystem.RandomDays <= 0f && Storyteller.Persona == StoryPersona.Off) return;
        foreach (var k in HazardsV18.Specs.Select(x => x.Kind))
        {
            var (p, room, refId) = HazardsV18.Pressure(w, k);
            p *= w.Eras.RiskMul(k.ToString()); // 익힌 기술이 원인을 누그러뜨린다
            if (p < 0.35f || !R.Chance(0.12f * p)) continue;
            Raise(k, room, refId, R.Range(2f, 6f));
        }
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Traces.Count); foreach (var t in Traces) { I((int)t.Kind); I(t.RoomId); I(t.Until); I(t.Cleaned ? 1 : 0); }
        I(Signs.Count); foreach (var s in Signs) { I((int)s.Kind); I(s.Due); I(s.Claim); I(s.Done ? 1 : 0); }
        I(Seen.Count); I(Stats.Averted); I(Stats.Fired);
    }
}

/// <summary>전조를 본 사람이 손을 쓰러 간다 · 사고 흔적을 치운다.</summary>
public sealed class OmenCheckActivity : Activity
{
    public override string Id => "omencheck";
    public override string Label => "낌새 살피기";

    private static HazardSign? Mine(CrewMember c, World w)
    {
        foreach (var s in w.Signs.Signs) if (!s.Done && s.Claim == c.Id) return s;
        return null;
    }

    private static IncidentTrace? Mess(CrewMember c, World w)
    {
        foreach (var t in w.Signs.Traces)
            if (!t.Cleaned && t.ClaimedBy < 0 && HazardSignSystem.Messy(t.Kind) && w.Tick - t.Tick > SimTime.Minutes(30) && c.Room != null && c.Room.Id == t.RoomId) return t;
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.IsChild || c.Down || c.Outside || HazardSignSystem.Off) return (0f, "—");
        if (Mine(c, w) is HazardSign s)
        {
            var more = HazardsV18.Of(s.Kind)!;
            return (Crisis.Acting(w) ? 0.2f : Bedtime(c, w) ? 0.4f : 0.62f, $"{more.Omen} — {Ko.EulReul(more.Cause)} 손본다");
        }
        if (Mess(c, w) is IncidentTrace t && !OnShift(c, w) && !Bedtime(c, w)) return (0.24f, $"{HazardsV18.Of(t.Kind)!.Trace} — 치운다");
        return (0f, "—");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Mine(c, w) is HazardSign s)
        {
            var room = s.RoomId >= 0 ? w.Ship.Rooms.ElementAtOrDefault(s.RoomId) : c.Room;
            if (room == null) return null;
            var spot = room.Cells.Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
            if (spot is not Cell at) return null;
            var sign = s;
            var toils = new List<Toil>
            {
                new GotoToil(at),
                new WaitToil(SimTime.Minutes(20), Pose.Working, at.Center),
                new DoToil((cm, world) => { world.Signs.Resolve(sign, cm); return true; }),
            };
            var more = HazardsV18.Of(s.Kind)!;
            return new Job(this, "낌새 살피기", toils) { LogText = $"{more.Omen} — {room.Name}의 {Ko.EulReul(more.Cause)} 손보러 간다", LogKind = LogKind.Work };
        }
        if (Mess(c, w) is IncidentTrace t)
        {
            var room = w.Ship.Rooms.ElementAtOrDefault(t.RoomId);
            if (room == null) return null;
            var at = room.Cells.Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x)).OrderBy(x => Math.Abs(x.X - t.At.X) + Math.Abs(x.Y - t.At.Y)).ThenBy(x => x.Y).ThenBy(x => x.X).Cast<Cell?>().FirstOrDefault();
            if (at is not Cell a) return null;
            t.ClaimedBy = c.Id;
            var tr = t;
            var toils = new List<Toil>
            {
                new GotoToil(a),
                new WaitToil(SimTime.Minutes(15), Pose.Working, a.Center),
                new DoToil((cm, world) => { world.Signs.CleanTrace(tr, cm); return true; }),
            };
            return new Job(this, "흔적 치우기", toils) { LogText = $"{Ko.EulReul(HazardsV18.Of(t.Kind)!.Trace)} 치운다", LogKind = LogKind.Work };
        }
        return null;
    }
}
