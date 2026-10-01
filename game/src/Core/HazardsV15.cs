using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v15 사고 26 → 70: 전기 6 · 물 6 · 공기 6 · 기계 6 · 구조 5 · 바깥 5 · 불 4 · 생물 4 · 사람 2.
// 새 사고는 이미 있는 효과(고장 · 불 · 망 손상 · 공기 · 오염 · 수압 · 병 · 외벽)를 엮는다 — 승무원은 이미 아는 대응으로 움직인다.

public static class HazardsV15
{
    public static readonly HazardSpec[] Specs =
    {
        // 전기
        new(HazardKind.BreakerCascade, "차단기 연쇄", HazardTarget.Ship, 3f, "차단기 연쇄 — 한 회로가 떨어지며 옆 회로까지 줄줄이 떨어진다 · 배전반에서 하나씩 다시 올려야 한다"),
        new(HazardKind.ArcFault, "배전반 아크", HazardTarget.Machine, 2f, "배전반 아크 — 배전반을 클릭: 단자가 달아올라 아크 섬광 위험 · 먼저 내려 식히면 막는다"),
        new(HazardKind.GroundFault, "접지 불량", HazardTarget.Room, 3f, "접지 불량 — 방을 클릭: 설비 둘이 배선 고장 · 만지던 사람이 찌릿 감전된다"),
        new(HazardKind.CellAging, "배터리 셀 열화", HazardTarget.Machine, 3f, "배터리 셀 열화 — 배터리를 클릭: 셀이 부풀어 용량이 준다 · 셀을 갈아야 한다"),
        new(HazardKind.InsulationCrack, "피복 경화", HazardTarget.Ship, 3f, "피복 경화 — 오래된 전선 피복이 갈라져 간선 두 토막이 상한다 (전압이 떨어지거나 끊긴다)"),
        new(HazardKind.TrunkSag, "간선 전압 강하", HazardTarget.Ship, 2.5f, "간선 전압 강하 — 배전실 앞 간선 이음매가 헐거워져 여러 방의 전압이 함께 떨어진다"),
        // 물
        new(HazardKind.PipeFreeze, "배관 동결", HazardTarget.Room, 2.5f, "배관 동결 — 방을 클릭: 차가워진 방의 급수관이 얼어 터진다 · 단수"),
        new(HazardKind.ValveSeize, "밸브 고착", HazardTarget.Room, 3f, "밸브 고착 — 방을 클릭: 급수 밸브가 반쯤 걸려 수압이 바닥난다 (역류 위험)"),
        new(HazardKind.Backflow, "오수 역류", HazardTarget.Room, 2.5f, "오수 역류 — 방을 클릭: 더러운 물이 급수관으로 거꾸로 든다 · 마시면 탈이 난다 · 관을 씻어 낸다"),
        new(HazardKind.TankSludge, "탱크 침전물", HazardTarget.Ship, 3f, "탱크 침전물 — 물 일부를 버리고 정수기 막이 오염된다"),
        new(HazardKind.CondensateFlood, "결로 침수", HazardTarget.Room, 3f, "결로 침수 — 방을 클릭: 천장 결로가 모여 바닥에 물이 고인다 (전기 설비 위험)"),
        new(HazardKind.SewageBackup, "오수 넘침", HazardTarget.Room, 2.5f, "오수 넘침 — 방을 클릭: 바닥에 오수가 넘친다 · 균이 퍼지고 냄새가 난다"),
        // 공기
        new(HazardKind.Co2Spike, "이산화탄소 급증", HazardTarget.Ship, 3f, "이산화탄소 급증 — 세정 필터가 막히고 사람이 많은 방의 이산화탄소가 치솟는다"),
        new(HazardKind.ScrubberSaturation, "세정제 포화", HazardTarget.Machine, 3f, "세정제 포화 — 산소 발생기·세정기를 클릭: 막이 포화돼 공기를 못 거른다"),
        new(HazardKind.InsulationSmoke, "단열재 그을음", HazardTarget.Room, 3f, "단열재 그을음 — 방을 클릭: 불은 없는데 연기가 찬다 · 어디서 나는지 찾아야 한다"),
        new(HazardKind.SealLeak, "문 씰 손상", HazardTarget.Door, 3f, "문 씰 손상 — 문을 클릭: 한쪽 방 기압이 떨어져 문 양쪽에 압이 걸린다 (균압 밸브로 맞추고 연다)"),
        new(HazardKind.Ozone, "오존", HazardTarget.Room, 2f, "오존 — 방을 클릭: 고전압 방전으로 오존이 생겨 공기가 독해진다"),
        new(HazardKind.HumiditySpike, "습도 급등", HazardTarget.Room, 3f, "습도 급등 — 방을 클릭: 물기가 차서 감지기가 흐려지고 바닥이 젖는다"),
        // 기계
        new(HazardKind.BearingSeize, "베어링 고착", HazardTarget.Machine, 3f, "베어링 고착 — 펌프·설비를 클릭: 베어링이 들러붙어 멈추고 달아오른다"),
        new(HazardKind.FanImbalance, "팬 진동", HazardTarget.Machine, 3f, "팬 진동 — 설비를 클릭: 팬이 틀어져 진동과 소음 · 곁의 사람이 신경이 곤두선다"),
        new(HazardKind.ShaftMisalign, "축 정렬 불량", HazardTarget.Machine, 2f, "축 정렬 불량 — 엔진·펌프를 클릭: 축이 틀어져 걸린다"),
        new(HazardKind.LooseMount, "고정 볼트 풀림", HazardTarget.Machine, 3f, "고정 볼트 풀림 — 설비를 클릭: 진동으로 볼트가 풀려 빨리 닳는다 (아직 고장은 아니다)"),
        new(HazardKind.HoistDrop, "들던 부품 낙하", HazardTarget.Crew, 2f, "들던 부품 낙하 — 승무원을 클릭: 무거운 부품을 놓쳐 발을 찧고 부품이 망가진다"),
        new(HazardKind.BadBatch, "불량 묶음", HazardTarget.Ship, 2f, "불량 묶음 — 기항지에서 산 부품 묶음이 의심스럽다 · 시험해 보기 전엔 쓰기 꺼림칙하다"),
        // 구조
        new(HazardKind.WeldFatigue, "용접부 피로", HazardTarget.Hull, 3f, "용접부 피로 — 외벽을 클릭: 전에 용접한 자리가 다시 갈라진다"),
        new(HazardKind.WindowCrack, "창 균열", HazardTarget.Ship, 2f, "창 균열 — 휴게실·관측실 창에 금이 간다"),
        new(HazardKind.HatchSeal, "외부 해치 씰", HazardTarget.Ship, 2.5f, "외부 해치 씰 — 에어락 바깥 문 씰이 상해 공기 탱크가 샌다"),
        new(HazardKind.ThermalStress, "열 응력", HazardTarget.Ship, 2.5f, "열 응력 — 햇빛과 그늘을 오가며 외판 두 곳에 실금이 간다"),
        new(HazardKind.FrameCreak, "골조 삐걱임", HazardTarget.Ship, 3f, "골조 삐걱임 — 배 전체가 삐걱댄다 · 외벽이 조금씩 약해지고 사람들이 불안해한다"),
        // 바깥
        new(HazardKind.RadiationBurst, "방사선 돌발", HazardTarget.Ship, 2f, "방사선 돌발 — 바깥 쪽 방과 선체 밖의 사람이 방사선을 쬔다 · 전자 장비 하나가 튄다"),
        new(HazardKind.IonStorm, "이온 폭풍", HazardTarget.Ship, 2.5f, "이온 폭풍 — 배 전체 감지기 교정이 틀어진다 (헛경보)"),
        new(HazardKind.DebrisAlert, "잔해 한쪽 면", HazardTarget.Ship, 2.5f, "잔해 한쪽 면 — 한쪽 면으로 작은 잔해 셋이 날아든다"),
        new(HazardKind.CometTail, "혜성 꼬리", HazardTarget.Ship, 2f, "혜성 꼬리 — 바깥 감지기에 먼지가 덮이고 선체 밖 사람의 우주복에 분진이 앉는다"),
        new(HazardKind.StaticDischarge, "정전기 방전", HazardTarget.Room, 2.5f, "정전기 방전 — 방을 클릭: 그 방의 전자 장비가 튄다"),
        // 불
        new(HazardKind.GreaseFire, "기름 화재", HazardTarget.Ship, 3f, "기름 화재 — 주방 조리대의 기름에 불이 붙는다"),
        new(HazardKind.DryerFire, "건조기 화재", HazardTarget.Ship, 2.5f, "건조기 화재 — 세탁실(없으면 침실)에서 불이 난다"),
        new(HazardKind.CableTrayFire, "케이블 트레이 화재", HazardTarget.Room, 2.5f, "케이블 트레이 화재 — 방을 클릭: 천장 케이블이 타며 전력 간선이 상한다"),
        new(HazardKind.Smolder, "훈소", HazardTarget.Room, 2.5f, "훈소 — 방을 클릭: 짐 더미 속에서 불씨가 연기를 피운다 · 두면 불이 된다"),
        // 생물
        new(HazardKind.MoldOutbreak, "곰팡이", HazardTarget.Room, 3f, "곰팡이 — 방을 클릭: 습한 구석에 곰팡이가 번진다 · 숨 쉬면 기관지가 상하고 식사도 상한다"),
        new(HazardKind.SeedRot, "종자 부패", HazardTarget.Ship, 2f, "종자 부패 — 재배대마다 자라던 작물이 뒤로 물러난다"),
        new(HazardKind.NutrientCrash, "양액 오염", HazardTarget.Machine, 3f, "양액 오염 — 재배대를 클릭: 양액 관이 막혀 작물이 마른다"),
        new(HazardKind.SkinFungus, "피부 곰팡이 번짐", HazardTarget.Ship, 2f, "피부 곰팡이 — 재배실·세탁실을 드나든 사람 몇에게 옮는다"),
        // 사람
        new(HazardKind.PanicAttack, "공황 발작", HazardTarget.Crew, 2f, "공황 발작 — 승무원을 클릭: 숨이 막히고 손이 떨려 하던 일을 놓는다"),
        new(HazardKind.MedError, "투약 실수", HazardTarget.Crew, 1.5f, "투약 실수 — 아픈 승무원을 클릭: 약을 잘못 써서 더 나빠진다"),
    };

    /// <summary>설비를 고르는 사고: 그 설비에 걸 수 있나.</summary>
    public static bool Fits(HazardKind k, Furniture f) => k switch
    {
        HazardKind.ArcFault => f.Type is FurnitureType.PowerPanel,
        HazardKind.CellAging => f.Type is FurnitureType.Battery or FurnitureType.CapacitorBank,
        HazardKind.ScrubberSaturation => f.Type is FurnitureType.OxygenGenerator or FurnitureType.Scrubber,
        HazardKind.BearingSeize => f.Type is FurnitureType.CoolantPump or FurnitureType.WaterRecycler or FurnitureType.OxygenGenerator or FurnitureType.Scrubber or FurnitureType.HeatExchanger,
        HazardKind.FanImbalance => f.Type is FurnitureType.Scrubber or FurnitureType.OxygenGenerator or FurnitureType.HeatExchanger or FurnitureType.CoolantPump or FurnitureType.MainComputer,
        HazardKind.ShaftMisalign => f.Type is FurnitureType.EngineCore or FurnitureType.CoolantPump or FurnitureType.AuxGenerator,
        HazardKind.NutrientCrash => f.Machine?.Crop != null,
        _ => true,
    };
}

public sealed partial class HazardSystem
{
    /// <summary>v15 새 사고 44.</summary>
    internal string? V15(HazardKind k, Cell at, int id)
    {
        var w = _w;
        var ship = w.Ship;
        var rooms = ship.Rooms.Where(r => !r.Detached && !r.Abandoned).ToList();
        Room? Pick(params RoomType[] t) => rooms.Where(r => t.Contains(r.Type)).OrderBy(r => r.Id).FirstOrDefault();
        Room? RoomHere() => Hazards.RoomAt(w, at) is Room r && !r.Detached ? r : null;
        Furniture? Here() => Hazards.MachineAt(w, k, at);
        CrewMember? Who() => w.Crew.FirstOrDefault(c => c.Id == id && !c.Dead);
        string? Inc(string text, Room? room, string ret, AlertLevel? alert = null, bool shipWide = false, IEnumerable<CrewMember>? crew = null)
        {
            if (alert is AlertLevel a) w.RaiseAlert(text, room, a, shipWide);
            w.History.Add(w, HistoryKind.Incident, text, room, crew);
            return ret;
        }
        bool Break(Machine m, FaultKind f) => w.Machines.Break(m, f) != null;
        void FrightenRoom(Room r, float amt, string why) { foreach (var c in w.Crew.Where(c => !c.Dead && c.Room == r)) Memory.Frighten(w, c, r, amt, why); }

        switch (k)
        {
            // ─── 전기 ───
            case HazardKind.BreakerCascade:
            {
                var panel = ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault(f => !f.Room.Detached)?.Machine;
                if (panel == null) return null;
                int start = w.Rng.Range(0, PowerGrid.CircuitCount), n = 0;
                for (int i = 0; i < 3; i++)
                {
                    int c = (start + i) % PowerGrid.CircuitCount;
                    if (panel.Faults.Any(f => f.Circuit == c)) continue;
                    panel.Faults.Add(new Fault { Kind = FaultKind.BreakerTrip, Since = w.Tick, Circuit = c });
                    w.Causes.OnFault(panel, panel.Faults[^1]);
                    panel.FaultCount++; n++;
                }
                if (n == 0) return null;
                MarkLog.Add(panel.Marks, w.Tick, $"차단기 {n}개가 줄줄이 떨어졌다");
                return Inc($"차단기 연쇄 — 회로 {n}개가 줄줄이 떨어졌다", panel.Body.Room, "차단기 연쇄", AlertLevel.Critical, true);
            }
            case HazardKind.ArcFault:
            {
                if (Here() is not Furniture f) return null;
                f.Machine!.Heat = MathF.Max(f.Machine.Heat, 0.95f);
                MarkLog.Add(f.Machine.Marks, w.Tick, "단자가 달아오른다 (아크 위험)");
                return Inc($"{f.Label} 단자가 달아오른다 — 아크 섬광 위험", f.Room, $"배전반 아크({f.Room.Name})", AlertLevel.Warning);
            }
            case HazardKind.GroundFault:
            {
                if (RoomHere() is not Room r) return null;
                var ms = r.Furniture.Where(f => f.Machine is { } m && m.Spec.PowerDraw > 0f && m.Faults.Count == 0).Take(2).ToList();
                if (ms.Count == 0) return null;
                foreach (var f in ms) Break(f.Machine!, FaultKind.WiringFault);
                var hurt = w.Crew.Where(c => !c.Dead && c.Room == r && c.Pose == Pose.Working).Take(1).ToList();
                foreach (var c in hurt) { NeedsSystem.AddInjury(c.Vitals, 0.06f, "감전"); Memory.Shake(w, c, 0.08f, "설비를 만지다 감전됐다"); }
                return Inc($"{r.Name} 접지 불량 — 설비 {ms.Count}대 배선 고장" + (hurt.Count > 0 ? $" · {Ko.IGa(hurt[0].Name)} 감전됐다" : ""), r, $"접지 불량({r.Name})", AlertLevel.Warning, crew: hurt);
            }
            case HazardKind.CellAging:
            {
                if (Here() is not Furniture f || !Break(f.Machine!, FaultKind.CellDegradation)) return null;
                return Inc($"{f.Label} 셀이 부풀었다 — 용량이 준다", f.Room, $"배터리 셀 열화({f.Label})");
            }
            case HazardKind.InsulationCrack:
            {
                var links = w.Net.Links.Where(l => l.Kind == NetKind.Power && !l.Cut && !l.Room.Detached).ToList();
                if (links.Count < 2) return null;
                var hit = new List<string>();
                for (int i = 0; i < 2; i++) { var l = w.Rng.Pick(links); links.Remove(l); w.Net.Hurt(l, 0.45f, "피복 경화"); hit.Add(l.Room.Name); }
                return Inc($"전선 피복이 갈라졌다 — {string.Join("·", hit)}", null, "피복 경화", AlertLevel.Warning, true);
            }
            case HazardKind.TrunkSag:
            {
                var src = w.Net.SourceRoom(NetKind.Power);
                var l = w.Net.Links.Where(l => l.Kind == NetKind.Power && !l.Cut && l.Door != null && (l.Door.RoomA == src || l.Door.RoomB == src)).OrderBy(l => l.Id).FirstOrDefault();
                if (l == null) return null;
                l.Integrity = MathF.Min(l.Integrity, 0.42f);
                return Inc($"{l.Room.Name} 간선 이음매가 헐거워졌다 — 그 너머 방들의 전압이 떨어진다", l.Room, "간선 전압 강하", AlertLevel.Warning, true);
            }
            // ─── 물 ───
            case HazardKind.PipeFreeze:
            {
                if (RoomHere() is not Room r) return null;
                var l = w.Net.Links.FirstOrDefault(l => l.Kind == NetKind.Water && l.Room == r && !l.Cut);
                if (l == null) return null;
                r.Air.Temperature = MathF.Min(r.Air.Temperature, 4f);
                w.Net.Hurt(l, 1f, "동결");
                return Inc($"{r.Name} 급수관이 얼어 터졌다", r, $"배관 동결({r.Name})", AlertLevel.Warning);
            }
            case HazardKind.ValveSeize:
            {
                if (RoomHere() is not Room r) return null;
                var ls = w.Net.Links.Where(l => l.Kind == NetKind.Water && !l.Cut && (l.Room == r || l.Door != null && (l.Door.RoomA == r || l.Door.RoomB == r))).ToList();
                if (ls.Count == 0) return null;
                foreach (var l in ls) { l.Integrity = MathF.Min(l.Integrity, 0.32f); l.Cause = "밸브 고착"; }
                return Inc($"{r.Name} 급수 밸브가 반쯤 걸렸다 — 수압이 바닥난다", r, $"밸브 고착({r.Name})", AlertLevel.Warning);
            }
            case HazardKind.Backflow:
            {
                if (RoomHere() is not Room r) return null;
                w.Flow.WaterQuality = MathF.Max(0f, w.Flow.WaterQuality - 0.4f);
                w.Soil.RoomSoil(r)[(int)SoilKind.Bio] = MathF.Min(1f, w.Soil.RoomSoil(r)[(int)SoilKind.Bio] + 0.3f);
                w.Flow.Stats.Backflows++;
                return Inc($"{r.Name}의 더러운 물이 급수관으로 거꾸로 들었다", r, $"오수 역류({r.Name})", AlertLevel.Warning, true);
            }
            case HazardKind.TankSludge:
            {
                var rec = ship.FurnitureOf(FurnitureType.WaterRecycler).FirstOrDefault(f => !f.Room.Detached)?.Machine;
                if (rec == null || w.Water.Level < 20f) return null;
                float dump = w.Water.Level * 0.1f;
                w.Water.Level -= dump;
                if (!rec.Has(FaultKind.MembraneFouling)) Break(rec, FaultKind.MembraneFouling);
                return Inc($"물탱크 바닥 침전물이 올라왔다 — {dump:0}L를 버리고 정수기 막이 오염됐다", rec.Body.Room, "탱크 침전물", AlertLevel.Warning, true);
            }
            case HazardKind.CondensateFlood:
            {
                if (RoomHere() is not Room r) return null;
                w.Moisture.AddWater(r, 40f);
                MarkLog.Add(r.Marks, w.Tick, "결로가 모여 바닥에 물이 고였다");
                return Inc($"{r.Name} 천장 결로가 모여 바닥에 물이 고였다", r, $"결로 침수({r.Name})", AlertLevel.Warning);
            }
            case HazardKind.SewageBackup:
            {
                if (RoomHere() is not Room r) return null;
                w.Soil.RoomSoil(r)[(int)SoilKind.Bio] = MathF.Min(1f, w.Soil.RoomSoil(r)[(int)SoilKind.Bio] + 0.7f);
                w.Moisture.AddWater(r, 15f);
                foreach (var c in w.Crew.Where(c => !c.Dead && c.Room == r)) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.1f);
                return Inc($"{r.Name} 바닥에 오수가 넘쳤다", r, $"오수 넘침({r.Name})", AlertLevel.Warning);
            }
            // ─── 공기 ───
            case HazardKind.Co2Spike:
            {
                var scrub = ship.FurnitureOf(FurnitureType.Scrubber).Concat(ship.FurnitureOf(FurnitureType.OxygenGenerator)).FirstOrDefault(f => !f.Room.Detached && f.Machine!.Faults.Count == 0)?.Machine;
                var crowd = rooms.OrderByDescending(r => w.Crew.Count(c => c.Room == r && !c.Dead)).ThenBy(r => r.Id).FirstOrDefault();
                if (scrub == null || crowd == null) return null;
                Break(scrub, FaultKind.FilterClogged);
                crowd.Air.CO2 += 1.5f;
                return Inc($"세정 필터가 막혔다 — {crowd.Name} 이산화탄소가 치솟는다", crowd, "이산화탄소 급증", AlertLevel.Warning, true);
            }
            case HazardKind.ScrubberSaturation:
            {
                if (Here() is not Furniture f || !Break(f.Machine!, FaultKind.MembraneFouling)) return null;
                return Inc($"{f.Label} 막이 포화됐다 — 공기를 못 거른다", f.Room, $"세정제 포화({f.Label})", AlertLevel.Warning);
            }
            case HazardKind.InsulationSmoke:
            {
                if (RoomHere() is not Room r) return null;
                r.Air.Smoke = MathF.Min(1f, r.Air.Smoke + 0.55f);
                FrightenRoom(r, 0.1f, "불도 없는데 연기가 찼다");
                return Inc($"{r.Name}에 연기가 찬다 — 불은 보이지 않는다 (벽 속 단열재)", r, $"단열재 그을음({r.Name})", AlertLevel.Warning);
            }
            case HazardKind.SealLeak:
            {
                var d = Hazards.DoorAt(w, at);
                if (d?.RoomA is not Room a || d.RoomB is not Room b) return null;
                var low = a.Type == RoomType.Corridor ? b : a;
                float kk = 0.84f;
                low.Air.O2 *= kk; low.Air.N2 *= kk;
                MarkLog.Add(low.Marks, w.Tick, "문 씰이 상해 기압이 빠졌다");
                return Inc($"{a.Name}·{b.Name} 사이 문 씰이 상했다 — {low.Name} 기압이 떨어졌다 ({low.Air.Pressure:0}kPa)", low, $"문 씰 손상({low.Name})", AlertLevel.Warning);
            }
            case HazardKind.Ozone:
            {
                if (RoomHere() is not Room r) return null;
                r.Air.Toxin = MathF.Min(1.5f, r.Air.Toxin + 0.18f);
                return Inc($"{r.Name}에 오존이 찼다 — 고전압 방전", r, $"오존({r.Name})", AlertLevel.Warning);
            }
            case HazardKind.HumiditySpike:
            {
                if (RoomHere() is not Room r) return null;
                w.Moisture.AddWater(r, 10f);
                foreach (var f in r.Furniture.Where(f => f.Machine != null)) f.Machine!.SensorCal = MathF.Max(0.3f, f.Machine.SensorCal - 0.2f);
                return Inc($"{r.Name} 습도가 치솟았다 — 감지기가 흐려지고 바닥이 젖는다", r, $"습도 급등({r.Name})");
            }
            // ─── 기계 ───
            case HazardKind.BearingSeize:
            {
                if (Here() is not Furniture f || !Break(f.Machine!, FaultKind.PumpSeized)) return null;
                f.Machine!.Heat = MathF.Max(f.Machine.Heat, 0.6f);
                return Inc($"{f.Label} 베어링이 들러붙어 멈췄다 — 달아오른다", f.Room, $"베어링 고착({f.Label})", AlertLevel.Warning);
            }
            case HazardKind.FanImbalance:
            {
                if (Here() is not Furniture f || !Break(f.Machine!, FaultKind.BearingWear)) return null;
                foreach (var c in w.Crew.Where(c => !c.Dead && c.Room == f.Room)) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f);
                return Inc($"{f.Label} 팬이 틀어져 덜덜거린다", f.Room, $"팬 진동({f.Label})");
            }
            case HazardKind.ShaftMisalign:
            {
                if (Here() is not Furniture f || !Break(f.Machine!, FaultKind.Jam)) return null;
                return Inc($"{f.Label} 축이 틀어져 걸렸다", f.Room, $"축 정렬 불량({f.Label})", AlertLevel.Warning);
            }
            case HazardKind.LooseMount:
            {
                if (Here() is not Furniture f) return null;
                f.Machine!.Wear = MathF.Min(1f, f.Machine.Wear + 0.3f);
                MarkLog.Add(f.Machine.Marks, w.Tick, "고정 볼트가 풀렸다 — 진동");
                return Inc($"{f.Label} 고정 볼트가 풀렸다 — 아직 돌지만 빨리 닳는다", f.Room, $"고정 볼트 풀림({f.Label})");
            }
            case HazardKind.HoistDrop:
            {
                if (Who() is not CrewMember c) return null;
                NeedsSystem.AddInjury(c.Vitals, 0.15f, "부품을 놓쳐 발을 찧었다");
                c.Interrupt(w);
                Memory.Shake(w, c, 0.08f, "들던 부품을 놓쳤다");
                var lost = new[] { ItemKind.Bearing, ItemKind.Pump, ItemKind.Plate }.FirstOrDefault(i => ship.CountStored(i) > 0);
                var box = ship.Furniture.FirstOrDefault(f => f.Storage != null && f.Storage.Count(lost) > 0 && !f.Room.Detached);
                box?.Storage!.Take(lost, 1);
                return Inc($"{Ko.IGa(c.Name)} 들던 부품을 놓쳐 발을 찧었다" + (box != null ? $" — {ItemKinds.Name(lost)} 하나가 망가졌다" : ""), c.Room, $"부품 낙하({c.Name})", AlertLevel.Warning, crew: new[] { c });
            }
            case HazardKind.BadBatch:
            {
                var lots = PartsSystem.TestKinds.SelectMany(kd => w.Parts.StockOf(kd)).Where(l => !l.Tested && !l.Suspect).ToList();
                if (lots.Count == 0) return null;
                var lot = lots.OrderByDescending(l => l.Made).First();
                foreach (var l in lots.Where(l => l.Batch == lot.Batch && l.Kind == lot.Kind)) l.Suspect = true;
                return Inc($"{ItemKinds.Name(lot.Kind)} 묶음({lot.Batch})이 의심스럽다 — 같은 묶음이 다른 배에서 일찍 나갔다는 소식", null, "불량 묶음");
            }
            // ─── 구조 ───
            case HazardKind.WeldFatigue:
            {
                var c0 = Hazards.HullAt(w, at);
                var welded = ship.Walls.Where(kv => kv.Value.IsHull && kv.Value.Welds > 0 && Hull.InsideRoom(ship, kv.Key) is Room r && !r.Detached).Select(kv => (Cell?)kv.Key).FirstOrDefault();
                return Crack(welded ?? c0) is string s ? s.Replace("선체 균열", "용접부 피로") : null;
            }
            case HazardKind.WindowCrack:
            {
                var room = Pick(RoomType.Lounge, RoomType.Observatory, RoomType.Bridge, RoomType.Mess);
                if (room == null) return null;
                var wall = ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == room).Select(kv => (Cell?)kv.Key).FirstOrDefault();
                return Crack(wall) is string s ? s.Replace("선체 균열", "창 균열") : null;
            }
            case HazardKind.HatchSeal:
            {
                var lockRoom = Pick(RoomType.Airlock, RoomType.EvaPrep);
                if (lockRoom == null || w.Air.Reserve < 40f) return null;
                w.Air.Reserve -= 35f;
                MarkLog.Add(lockRoom.Marks, w.Tick, "바깥 해치 씰이 상했다");
                return Inc("에어락 바깥 해치 씰이 상했다 — 공기 탱크가 샌다", lockRoom, "외부 해치 씰", AlertLevel.Warning, true);
            }
            case HazardKind.ThermalStress:
            {
                var hull = ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) is Room r && !r.Detached && r.Type != RoomType.Corridor).Select(kv => kv.Key).ToList();
                if (hull.Count == 0) return null;
                int n = 0;
                for (int i = 0; i < 2; i++) { var c = w.Rng.Pick(hull); Hull.Damage(ship, c, 0.35f); MarkLog.Add(ship.WallAt(c)!.Marks, w.Tick, "열 응력 실금"); n++; }
                return Inc($"열 응력으로 외판 {n}곳에 실금이 갔다", null, "열 응력", AlertLevel.Warning, true);
            }
            case HazardKind.FrameCreak:
            {
                foreach (var (_, wall) in ship.Walls.Where(kv => kv.Value.IsHull)) wall.MaxIntegrity = MathF.Max(0.5f, wall.MaxIntegrity - 0.02f);
                foreach (var c in w.Crew.Where(c => !c.Dead && c.Fears.Count > 0)) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.06f);
                return Inc("배 전체가 길게 삐걱였다 — 외벽이 조금씩 약해진다", null, "골조 삐걱임", AlertLevel.Warning, true);
            }
            // ─── 바깥 ───
            case HazardKind.RadiationBurst:
            {
                var exposed = w.Crew.Where(c => !c.Dead && (c.Outside || c.Room != null && ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == c.Room))).ToList();
                int sick = 0;
                foreach (var c in exposed) if (w.Rng.Chance(c.Outside ? 0.8f : 0.2f) && w.Ailments.Catch(c, "radiation", null, "방사선 돌발") != null) sick++;
                Glitch("방사선 돌발");
                return Inc($"방사선 돌발 — 바깥 쪽에 있던 {exposed.Count}명이 쬐었다 (앓는 사람 {sick})", null, "방사선 돌발", AlertLevel.Critical, true, exposed);
            }
            case HazardKind.IonStorm:
            {
                foreach (var m in ship.Machines) m.SensorCal = MathF.Max(0.3f, m.SensorCal - 0.25f);
                return Inc("이온 폭풍 — 배 전체 감지기 교정이 틀어졌다", null, "이온 폭풍", AlertLevel.Warning, true);
            }
            case HazardKind.DebrisAlert:
            {
                var side = rooms.Where(r => r.Type != RoomType.Corridor && ship.Walls.Any(kv => kv.Value.IsHull && Hull.InsideRoom(ship, kv.Key) == r)).OrderBy(r => r.Center.X).ToList();
                if (side.Count == 0) return null;
                bool left = w.Rng.Chance(0.5f);
                var targets = (left ? side.Take(3) : side.AsEnumerable().Reverse().Take(3)).ToList();
                foreach (var r in targets) Shower.Add((w.Tick + SimTime.Minutes(w.Rng.Range(3f, 25f)), Scenarios.OuterTarget(w, r), w.Rng.Range(0.2f, 0.35f)));
                Shower.Sort((a, b) => a.tick.CompareTo(b.tick));
                return Inc($"잔해 경보 — {(left ? "왼" : "오른")}쪽 면으로 작은 잔해 {targets.Count}개", null, "잔해 한쪽 면", AlertLevel.Critical, true);
            }
            case HazardKind.CometTail:
            {
                var sensor = ship.FurnitureOf(FurnitureType.SensorArray).FirstOrDefault(f => !f.Room.Detached)?.Machine;
                if (sensor != null && !sensor.Has(FaultKind.SensorFouling)) Break(sensor, FaultKind.SensorFouling);
                foreach (var c in w.Crew.Where(c => c.Outside)) c.Soil.SuitDust = 1f;
                return Inc("혜성 꼬리를 지난다 — 바깥 감지기에 먼지가 덮였다", sensor?.Body.Room, "혜성 꼬리", AlertLevel.Warning, true);
            }
            case HazardKind.StaticDischarge:
            {
                if (RoomHere() is not Room r) return null;
                var m = r.Furniture.Select(f => f.Machine).FirstOrDefault(m => m != null && m.Spec.PowerDraw > 0f && m.Faults.Count == 0);
                if (m == null || !Break(m, FaultKind.ControlFault)) return null;
                return Inc($"{r.Name} 정전기 방전 — {m.Name} 제어부가 튀었다", r, $"정전기 방전({r.Name})");
            }
            // ─── 불 ───
            case HazardKind.GreaseFire:
            {
                var stove = ship.FurnitureOf(FurnitureType.Stove).FirstOrDefault(f => !f.Room.Detached);
                var spot = stove?.UseSpots.FirstOrDefault(ship.IsOpenFloor);
                if (stove == null || spot == null || spot == default(Cell) || !Incidents.Fire(w, spot.Value)) return null;
                return Inc($"{stove.Room.Name} 조리대 기름에 불이 붙었다", stove.Room, "기름 화재");
            }
            case HazardKind.DryerFire:
            {
                var room = Pick(RoomType.Laundry) ?? Pick(RoomType.Quarters);
                var floor = room?.Cells.Where(ship.IsOpenFloor).OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
                if (room == null || floor == null || floor.Count == 0 || !Incidents.Fire(w, floor[w.Rng.Range(0, floor.Count)])) return null;
                return Inc($"{room.Name} 건조기에서 불이 났다", room, "건조기 화재");
            }
            case HazardKind.CableTrayFire:
            {
                if (RoomHere() is not Room r) return null;
                var floor = r.Cells.Where(ship.IsOpenFloor).ToList();
                if (floor.Count == 0) return null;
                var c0 = floor[w.Rng.Range(0, floor.Count)];
                if (!Incidents.Fire(w, c0)) return null;
                w.Net.DamageNear(c0, 2.5f, 0.6f, "케이블 트레이 화재", fire: true);
                return Inc($"{r.Name} 천장 케이블 트레이가 탄다 — 간선이 상한다", r, $"케이블 트레이 화재({r.Name})");
            }
            case HazardKind.Smolder:
            {
                if (RoomHere() is not Room r) return null;
                var floor = r.Cells.Where(ship.IsOpenFloor).ToList();
                if (floor.Count == 0) return null;
                r.Air.Smoke = MathF.Min(1f, r.Air.Smoke + 0.35f);
                w.Fire.Ignite(floor[w.Rng.Range(0, floor.Count)], 0.15f);
                return Inc($"{r.Name} 짐 더미 속에서 불씨가 연기를 피운다", r, $"훈소({r.Name})", AlertLevel.Warning);
            }
            // ─── 생물 ───
            case HazardKind.MoldOutbreak:
            {
                if (RoomHere() is not Room r) return null;
                w.Soil.RoomSoil(r)[(int)SoilKind.Bio] = MathF.Min(1f, w.Soil.RoomSoil(r)[(int)SoilKind.Bio] + 0.5f);
                var sick = new List<CrewMember>();
                foreach (var c in w.Crew.Where(c => !c.Dead && c.Room == r)) if (w.Rng.Chance(0.3f) && w.Ailments.Catch(c, "moldlung", null, $"{r.Name} 곰팡이") != null) sick.Add(c);
                var box = r.Furniture.FirstOrDefault(f => f.Storage != null && f.Storage.Count(ItemKind.Meal) > f.Storage.Tainted);
                if (box != null) box.Storage!.Taint(Math.Min(2, box.Storage.Count(ItemKind.Meal) - box.Storage.Tainted));
                return Inc($"{r.Name} 구석에 곰팡이가 번졌다" + (sick.Count > 0 ? $" — {sick.Count}명 기침" : ""), r, $"곰팡이({r.Name})", AlertLevel.Warning, crew: sick);
            }
            case HazardKind.SeedRot:
            {
                var beds = ship.Machines.Where(m => m.Crop != null && !m.Body.Room.Detached).ToList();
                if (beds.Count == 0) return null;
                foreach (var m in beds) m.Crop!.Growth = MathF.Max(0f, m.Crop.Growth - 0.25f);
                return Inc($"종자가 상했다 — 재배대 {beds.Count}곳 작물이 뒤로 물러났다", beds[0].Body.Room, "종자 부패", AlertLevel.Warning, true);
            }
            case HazardKind.NutrientCrash:
            {
                if (Here() is not Furniture f || !Break(f.Machine!, FaultKind.NutrientClog)) return null;
                return Inc($"{f.Label} 양액 관이 막혔다 — 작물이 마른다", f.Room, $"양액 오염({f.Label})", AlertLevel.Warning);
            }
            case HazardKind.SkinFungus:
            {
                var who = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderByDescending(c => c.Soil.ClothesMax + c.Soil.HandsMax).ThenBy(c => c.Id).Take(2).ToList();
                int n = who.Count(c => w.Ailments.Catch(c, "fungus", null, "습한 재배실·세탁실") != null);
                if (n == 0) return null;
                return Inc($"피부 곰팡이가 번졌다 — {n}명", null, "피부 곰팡이 번짐", crew: who);
            }
            // ─── 사람 ───
            case HazardKind.PanicAttack:
            {
                if (Who() is not CrewMember c) return null;
                c.Needs.Stress = MathF.Max(c.Needs.Stress, 0.9f);
                c.Interrupt(w);
                Memory.Shake(w, c, 0.15f, "숨이 막히고 손이 떨렸다");
                c.Say(w, "숨이… 잠깐만");
                return Inc($"{Ko.IGa(c.Name)} 공황 발작을 일으켰다 — 하던 일을 놓았다", c.Room, $"공황 발작({c.Name})", AlertLevel.Warning, crew: new[] { c });
            }
            case HazardKind.MedError:
            {
                var c = Who() ?? w.Crew.FirstOrDefault(x => !x.Dead && x.Ailments.Count > 0);
                if (c == null || c.Ailments.Count == 0) return null;
                c.Vitals.Health = MathF.Max(0.1f, c.Vitals.Health - 0.12f);
                MarkLog.Add(c.Memory.Marks, w.Tick, "약을 잘못 썼다 — 더 나빠졌다");
                return Inc($"투약 실수 — {c.Name}에게 맞지 않는 약을 썼다 (체력 {c.Vitals.Health * 100:0}%)", c.Room, $"투약 실수({c.Name})", AlertLevel.Warning, crew: new[] { c });
            }
        }
        return null;
    }
}
