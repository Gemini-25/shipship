using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// 의료 3차 — 수술 로봇 팔: 사람이 없거나 못 할 때 주컴퓨터가 칼을 잡는다.
//   팔: 수술대 곁에 선 기둥 · 세 마디 팔 · 기구 머리. 주컴퓨터가 데이터선으로 움직인다 (선이 끊긴 방 · 정전 · 컴퓨터가 멎으면 팔도 멎는다).
//   집도: IV 추론 · V 지휘부터 (그 밑은 곁에서 거들기만). 의무관이 없거나 · 다쳤거나 · 지쳤거나 · 죄책감으로 물러설 때 컴퓨터가 맡는다.
//   손: 컴퓨터 등급 · 수술 로봇 기술(surgbot) · 협동 로봇 팔(cobotarm) · 생체 감시 모듈 등급 · 겪은 수술 수 × 교정 × 팔 상태.
//     — 수술(Surgery.Odds)과 이식(Transplant.Chance)에 같은 함수(Factor)로 들어간다. 사람 곁에서 거들면 가망이 오른다.
//   동의: 깨어 있으면 환자가, 쓰러졌으면 함장(지휘하는 사람)이 정한다 — 컴퓨터를 믿는 정도 · 가치관 · 팔이 겪은 결과 · 방침(컴퓨터 제안).
//     마다하면 손이 덜 익은 사람이라도 칼을 잡는다.
//   결과: 살리면 믿음이 오르고(환자 · 동의한 사람 · 배 전체) · 잘못되면 내리고 · 숨지면 회의 안건(컴퓨터 제안 → 모두 묻는다)까지.
//   멈춤: 수술 중 정전 · 데이터선 끊김 · 컴퓨터가 멎거나 내려앉으면 팔이 선다 — 곁의 사람(보조 → 아무나)이 이어받는다 · 아무도 없으면 열린 채로 피가 빠진다.
//   교정: 수술마다 · 흔들림마다 조금씩 어긋난다 — 기관사 · 의무관이 다시 맞춘다 (CalibrateArmActivity).
public sealed class ArmRun
{
    public int Case { get; init; }
    public int Arm { get; init; }
    public long Since { get; init; }
    public string Why { get; init; } = "";
    public int ConsentBy { get; init; } = -1;
    public string Consent { get; init; } = "";
}

public sealed class SurgicalArmSystem
{
    private readonly World _w;
    private readonly SortedDictionary<int, ArmRun> _lead = new();      // 수술 번호 → 팔이 집도
    private readonly SortedDictionary<int, float> _calib = new();      // 팔(가구 번호) → 교정 0~1
    private readonly SortedDictionary<int, long> _refused = new();     // 수술 번호 → 마다한 때
    private readonly SortedDictionary<int, long> _orphan = new();      // 팔이 멈춰 열린 채 남은 수술 → 멈춘 때
    private readonly SortedSet<int> _assist = new();                   // 팔이 곁에서 거든 수술
    private readonly SortedDictionary<int, long> _told = new();
    private Furniture? _cur;
    public int Ops, LeadOps, AssistOps, Successes, Failures, Deaths, Stalls, Takeovers, Consents, Refusals, Calibrations, AssistOnly, Waited, Reviews;
    public List<string> Notes { get; } = new();

    public SurgicalArmSystem(World w) => _w = w;

    public IReadOnlyDictionary<int, ArmRun> Runs => _lead;
    public bool Leads(SurgeryCase k) => _lead.ContainsKey(k.Id);
    public bool Orphaned(SurgeryCase k) => _orphan.ContainsKey(k.Id);
    public bool Refused(SurgeryCase k) => _refused.ContainsKey(k.Id);
    public float Calib(Furniture f) => _calib.TryGetValue(f.Id, out var v) ? v : 1f;

    private CrewMember? P(int id) { foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }

    /// <summary>그 방의 수술 팔 (없으면 null).</summary>
    public Furniture? ArmIn(Room? r)
    {
        if (r == null) return null;
        foreach (var f in r.Furniture) if (f.Type == FurnitureType.SurgicalArm && !f.Stowed && !f.Room.Detached) return f;
        return null;
    }

    /// <summary>팔이 움직일 수 있나 (전기 · 고장 · 데이터선 · 주컴퓨터).</summary>
    public bool Works(Furniture arm, out string why)
    {
        var a = _w.Automation;
        why = "";
        if (arm.Machine is not Machine m || arm.Stowed || arm.Room.Detached) { why = "팔이 없다"; return false; }
        if (m.Faults.Count > 0) { why = $"팔 고장 — {m.Faults[0].Spec.Name}"; return false; }
        if (!m.Powered || m.Stopped) { why = "팔에 전기가 끊겼다"; return false; }
        if (!arm.Room.DataLinked) { why = "데이터선이 끊겼다"; return false; }
        if (!a.Present || !a.CoreOnline) { why = "주컴퓨터가 멎었다"; return false; }
        return true;
    }

    /// <summary>집도할 수 있나 (IV 추론 · V 지휘부터 · 본체가 돌고 · 교정이 맞아야).</summary>
    public bool CanLead(Furniture arm, out string why)
    {
        var a = _w.Automation;
        if (!Works(arm, out why)) return false;
        if (!a.MainOnline || a.Level < 4) { why = $"컴퓨터가 {AutomationSystem.LevelName(a.Level)} — 곁에서 거들기만 한다"; return false; }
        if (Calib(arm) < 0.35f) { why = "팔이 어긋났다 — 다시 맞춰야 한다"; return false; }
        return true;
    }

    /// <summary>팔의 손 (0~1 — 사람의 의료 솜씨와 같은 눈금).</summary>
    public float Hand(Furniture arm)
    {
        var w = _w;
        var a = w.Automation;
        int lvl = a.Present ? a.Level : 1;
        float h = lvl >= 5 ? 0.5f : lvl == 4 ? 0.38f : 0.25f;
        if (w.Eras.Has("surgbot")) h += 0.15f;                         // 수술 로봇 기술
        if (w.Eras.Has("cobotarm")) h += 0.04f;                        // 협동 로봇 팔
        if (a.Present) h += MathF.Min(0.08f, 0.02f * a.Core.Grade(ComputerModule.BioMonitor)); // 생체 감시를 다시 단 만큼
        h += MathF.Min(0.2f, 0.015f * Ops);                            // 겪은 수술 수만큼 다듬어진다
        float eff = arm.Machine is Machine m ? Math.Clamp(m.Efficiency, 0.3f, 1f) : 0.5f;
        return Math.Clamp(h * (0.6f + 0.4f * Calib(arm)) * (0.75f + 0.25f * eff), 0.05f, 0.95f);
    }

    /// <summary>지금 집도하는 팔의 손 (Surgery가 집도의 솜씨 자리에 쓴다).</summary>
    public float ArmSkill() => _cur != null ? Hand(_cur) : 0.3f;

    /// <summary>팔의 빠르기 (떨지 않고 꾸준하다 · 어긋나면 느리다).</summary>
    public float Pace() => _cur != null ? 0.8f + 0.25f * Calib(_cur) : 0.9f;

    /// <summary>
    /// 수술 · 이식 가망에 더하는 몫 (같은 함수): s == null이면 팔이 집도 (손은 Skill로 이미 셌다 — 어긋남만), 사람이면 곁에서 거든다.
    /// </summary>
    public float Factor(Room? r, CrewMember? s)
    {
        var arm = ArmIn(r);
        if (arm == null || !Works(arm, out _)) return s == null ? -0.2f : 0f;
        if (s == null) return -0.12f * (1f - Calib(arm));
        return 0.03f + 0.08f * Hand(arm);
    }

    /// <summary>개조 후보 (배에 팔이 없을 때): 의무관이 없어 기다린 수술 · 물러선 사람 · 수술 로봇 기술.</summary>
    public (float, string) Need()
    {
        var w = _w;
        if (w.Ship.FurnitureOf(FurnitureType.SurgicalArm).Any(f => !f.Room.Detached)) return (0f, "");
        if (Waited > 0) return (0.6f, $"집도할 사람이 없어 수술을 기다렸다 ({Waited}번)");
        if (w.Surgery.StepBacks > 0) return (0.4f, "수술대 앞에 서지 못한 사람이 있었다");
        if (w.Eras.Has("surgbot")) return (0.45f, "수술 로봇을 배웠다");
        if (w.Surgery.Deaths > 0) return (0.3f, "수술대 위에서 사람을 잃었다");
        return (0f, "");
    }

    // ───────────── 누가 칼을 잡나 (Surgery.Assign이 부른다) ─────────────

    /// <summary>
    /// 사람이 없거나 · 지쳤거나 · 다쳤거나 · 물러서면 팔이 맡는다 (동의를 받고). true면 팔이 집도한다.
    /// 마다했는데 집도할 사람이 없으면 stand에 손이 덜 익은 사람을 돌려준다.
    /// </summary>
    public bool Claim(SurgeryCase k, CrewMember pt, CrewMember? best, bool cont, out CrewMember? stand)
    {
        var w = _w;
        stand = null;
        if (_lead.ContainsKey(k.Id)) return true;
        if (_orphan.ContainsKey(k.Id)) return false; // 멈춘 팔 — 사람이 마친다
        var guilty = w.Crew.Where(c => c != pt && c.CanAct && !c.Outside && !c.IsChild && !w.Surgery.Busy(c) && (c.Role == CrewRole.Medic || c.SkillLevel(Skill.Medicine) >= 0.5f)
                                       && w.Surgery.Guilt.GetValueOrDefault(c.Id) > 0.5f).OrderBy(c => c.Id).FirstOrDefault();
        string? why = best == null ? "집도할 사람이 없다"
            : best.Needs.Rest < 0.15f ? $"{Ko.IGa(best.Name)} 지쳐 손이 무디다"
            : best.Vitals.Injury > 0.25f ? $"{best.Name}도 다쳤다"
            : guilty != null && best.Role != CrewRole.Medic ? $"{Ko.IGa(guilty.Name)} 수술대 앞에 서지 못한다 — 손이 떨린다"
            : null;
        if (why == null) return false; // 사람이 한다 (팔은 곁에서 거든다)
        if (best == null) Waited++;
        var table = TableFor(k, pt);
        var arm = table != null ? ArmIn(table.Room) : null;
        if (arm == null) { if (best == null) stand = null; return false; }
        if (!CanLead(arm, out string no))
        {
            AssistOnly++;
            Tell($"nolead:{k.Id}", SimTime.Hours(3), () => w.Automation.Book.Add(ActKind.Advice, arm.Room, $"{pt.Name} {SurgerySystem.KindName(k.Kind)} — {why}", no,
                "팔로 집도하지 못한다 — 사람을 기다린다", "", $"armno:{k.Id}", SimTime.Hours(3)));
            return false;
        }
        if (pt.Down && !w.MedBots.CanCarry() && !table!.Cells.Contains(pt.Cell)) return false; // 수술대까지 옮길 들것이 없다
        if (_refused.TryGetValue(k.Id, out var rt) && w.Tick - rt < SimTime.Hours(6) && w.Grades.Now(pt) != InjuryGrade.Critical)
        {
            if (best == null) stand = StandIn(pt, k);
            return false;
        }
        if (!Consent(k, pt, why, best == null, out var by, out var said))
        {
            _refused[k.Id] = w.Tick;
            if (best == null) stand = StandIn(pt, k);
            return false;
        }
        _refused.Remove(k.Id);
        Lead(k, pt, table!, arm, why, by, said, best);
        return true;
    }

    private Furniture? TableFor(SurgeryCase k, CrewMember pt)
    {
        var w = _w;
        if (w.Surgery.TableOf(k) is Furniture t0 && t0.Type == FurnitureType.OperatingTable && ArmIn(t0.Room) != null) return t0;
        foreach (var t in w.Ship.FurnitureOf(FurnitureType.OperatingTable).OrderBy(f => f.Id))
        {
            if (t.Room.Detached || ArmIn(t.Room) == null) continue;
            if (t.ReservedBy != null && t.ReservedBy != pt) continue;
            if (w.Surgery.Cases.Any(x => x != k && x.Table == t.Id && x.State != CaseState.Done)) continue;
            return t;
        }
        return null;
    }

    /// <summary>컴퓨터를 마다했을 때 칼을 잡을 사람: 의료를 가장 아는 어른 (손이 덜 익어도).</summary>
    private CrewMember? StandIn(CrewMember pt, SurgeryCase k)
    {
        var w = _w;
        return w.Crew.Where(c => c != pt && c.CanAct && !c.Outside && !c.IsChild && !c.Away && !w.Surgery.Busy(c) && w.Grades.Now(c) < InjuryGrade.Serious
                                 && !w.Surgery.Cases.Any(x => x != k && (x.Surgeon == c.Id || x.Assistant == c.Id)))
            .OrderByDescending(c => c.SkillLevel(Skill.Medicine)).ThenBy(c => c.Id).FirstOrDefault();
    }

    /// <summary>"컴퓨터에 칼을 맡긴다" — 깨어 있으면 환자가, 아니면 함장(지휘하는 사람)이.</summary>
    private bool Consent(SurgeryCase k, CrewMember pt, string why, bool noHuman, out int byId, out string said)
    {
        var w = _w;
        var a = w.Automation;
        var cmd = w.Command;
        bool crit = w.Grades.Now(pt) == InjuryGrade.Critical || w.Surgery.Internal(pt);
        var boss = cmd.Active && cmd.Commander is CrewMember c0 && c0.CanAct && c0 != pt ? c0 : cmd.Captain is CrewMember cap && cap.CanAct && cap != pt ? cap : null;
        CrewMember? d = !pt.Down && !pt.Dead ? pt : boss;
        int ask = w.Policies["computerask"];
        if (d == null)
        {
            byId = -1;
            said = "물을 사람이 없다 — 기다리면 더 위험하다";
            bool go = crit || noHuman;
            w.Log.Add(w.Tick, go ? LogKind.Warning : LogKind.Life, go ? $"{pt.Name}: 물을 사람이 없어 주컴퓨터가 팔로 연다" : $"{pt.Name}: 물을 사람이 없다 — 사람을 기다린다", pt.Id);
            return go;
        }
        bool yes = Willing(d, noHuman, crit, ask, out float trust, out string reason);
        // 방침 "모두 묻는다": 환자가 받아도 지휘하는 사람이 한 번 더 본다
        if (yes && ask >= 2 && d == pt && boss != null && !Willing(boss, noHuman, crit, ask, out float bt, out _))
        {
            yes = false;
            d = boss;
            reason = $"{Ko.IGa(boss.Name)} 사람 손을 원했다 (컴퓨터 믿음 {bt * 100:0}%)";
        }
        byId = d.Id;
        said = reason;
        string kind = SurgerySystem.KindName(k.Kind);
        if (yes)
        {
            Consents++;
            d.Say(w, Persona.Say(d, d == pt ? "맡길게. 손은 떨리지 않겠지" : $"{pt.Name}을 살려 줘. 팔로 해"));
            w.Log.Add(w.Tick, LogKind.Life, d == pt ? $"{pt.Name}: 컴퓨터에 칼을 맡기겠다 — {reason}" : $"{d.Name}: {pt.Name} {kind}을 컴퓨터에 맡긴다 — {reason}", d.Id);
            MarkLog.Add(d.Memory.Marks, w.Tick, d == pt ? $"{kind}을 수술 팔에 맡겼다" : $"{pt.Name} {kind}을 수술 팔에 맡겼다");
        }
        else
        {
            Refusals++;
            d.Say(w, Persona.Say(d, d == pt ? "기계한테 몸을 열게 할 순 없어. 사람이 해 줘" : "사람이 해. 기계 손에 맡길 순 없어"));
            w.Log.Add(w.Tick, LogKind.Life, d == pt ? $"{pt.Name}: 수술 팔은 싫다 — 사람이 해 달라 ({reason})" : $"{d.Name}: {pt.Name} {kind}은 사람이 한다 ({reason})", d.Id);
            MarkLog.Add(d.Memory.Marks, w.Tick, $"수술 팔을 마다했다 — {reason}");
            if (a.Present && a.MainOnline)
                a.Book.Add(ActKind.Advice, pt.Room, $"{pt.Name} {kind} — {why}", $"{Ko.IGa(d.Name)} 팔을 마다했다 ({reason})", "사람에게 맡긴다 — 곁에서 거들기만 한다", "", $"armrefuse:{k.Id}", SimTime.Hours(6));
        }
        return yes;
    }

    /// <summary>이 사람이 컴퓨터에 칼을 맡길까 (믿음 · 가치관 · 팔이 겪은 결과 · 방침 · 급함).</summary>
    private bool Willing(CrewMember d, bool noHuman, bool crit, int ask, out float trust, out string reason)
    {
        var w = _w;
        trust = w.Automation.Trusts.Of(d);
        float s = trust + d.Value switch { CrewValue.Efficiency => 0.1f, CrewValue.Rules => 0.04f, CrewValue.Freedom => -0.1f, CrewValue.People => -0.08f, _ => 0f }
                  + (noHuman ? 0.15f : 0f) + (crit ? 0.1f : 0f)
                  + MathF.Min(0.15f, 0.03f * Successes) - 0.12f * Deaths - 0.04f * Failures - (ask >= 2 ? 0.08f : 0f);
        bool yes = s >= 0.5f;
        reason = yes ? (trust >= 0.6f ? $"컴퓨터를 믿는다 ({trust * 100:0}%)" : noHuman ? "칼을 잡을 사람이 없다" : "기다릴 수 없다")
            : trust < 0.4f ? $"컴퓨터를 믿지 못한다 ({trust * 100:0}%)"
            : Deaths > 0 ? "팔이 사람을 잃은 적이 있다"
            : d.Value == CrewValue.People ? "사람 손이 낫다" : "아직은 기계 손이 미덥지 않다";
        return yes;
    }

    private void Lead(SurgeryCase k, CrewMember pt, Furniture table, Furniture arm, string why, int by, string said, CrewMember? best)
    {
        var w = _w;
        var a = w.Automation;
        _lead[k.Id] = new ArmRun { Case = k.Id, Arm = arm.Id, Since = w.Tick, Why = why, ConsentBy = by, Consent = said };
        k.Surgeon = -1;
        k.Table = table.Id;
        k.Notes.Add("수술 팔 집도");
        // 곁에 설 사람: 물러선 사람이라도 거들 수는 있다 · 없으면 손이 빈 사람
        var helper = best != null && best.CanAct && best.Vitals.Injury <= 0.25f ? best : StandIn(pt, k);
        if (!(k.Assistant >= 0 && P(k.Assistant) is CrewMember a0 && a0.CanAct)) k.Assistant = helper?.Id ?? -1;
        var asx = P(k.Assistant);
        string kind = SurgerySystem.KindName(k.Kind);
        w.Log.Add(w.Tick, LogKind.Work, $"수술 팔이 {pt.Name} {kind}을 맡는다 — {why}" + (asx != null ? $" (곁에 {asx.Name})" : ""), pt.Id);
        if (a.Present)
        {
            a.Speak.Announce(a.Voice.Style($"{table.Room.Name} — {pt.Name} {kind}, 제가 팔로 집도합니다" + (asx != null ? $". {asx.Name}, 수술대 건너편에 서 주십시오" : "")), table.Room, 2);
            a.Book.Add(ActKind.Plan, table.Room, $"{pt.Name} {kind} — {why}", $"팔 손 {Hand(arm) * 100:0}% · 교정 {Calib(arm) * 100:0}% · {said}",
                "수술 팔로 집도한다", asx != null ? $"{asx.Name}: 곁에서 거들기" : "", $"armlead:{k.Id}", SimTime.Hours(2));
        }
        pt.NextThinkTick = Math.Min(pt.NextThinkTick, w.Tick + 1);
        if (asx != null) asx.NextThinkTick = Math.Min(asx.NextThinkTick, w.Tick + 1);
    }

    // ───────────── 시스템 틱 ─────────────

    public void Update(float dt)
    {
        var w = _w;
        if (_lead.Count > 0) StepLeads();
        if (_orphan.Count > 0) Orphans(dt);
        if (w.Tick % SimTime.Minutes(10) >= World.SystemInterval) return;
        Arms();
    }

    /// <summary>팔이 집도하는 수술: 한 틱씩 (손 씻기 대신 기구 멸균 → 마취 → 수술 → 닫기).</summary>
    private void StepLeads()
    {
        var w = _w;
        foreach (var id in _lead.Keys.ToList())
        {
            var run = _lead[id];
            var k = w.Surgery.Cases.FirstOrDefault(x => x.Id == id);
            if (k == null || k.State is CaseState.Done or CaseState.Deferred) { _lead.Remove(id); continue; }
            var arm = run.Arm >= 0 && run.Arm < w.Ship.Furniture.Count ? w.Ship.Furniture[run.Arm] : null;
            string why = "팔이 없다";
            if (arm == null || !CanLead(arm, out why)) { Stall(k, run, arm, why); continue; }
            if (w.Propulsion.Burning && k.State == CaseState.Operating && !k.Warned) _calib[arm.Id] = MathF.Max(0f, Calib(arm) - 0.002f); // 흔들림에 어긋난다
            _cur = arm;
            var st = ToilStatus.Running;
            for (int i = 0; i < World.SystemInterval && st == ToilStatus.Running; i++) st = w.Surgery.Step(k, null);
            _cur = null;
            if (st == ToilStatus.Failed || st == ToilStatus.Succeeded && k.State is CaseState.Deferred or CaseState.Waiting) _lead.Remove(id);
        }
    }

    /// <summary>팔이 멈췄다: 곁의 사람이 이어받는다 (없으면 열린 채로 피가 빠진다).</summary>
    private void Stall(SurgeryCase k, ArmRun run, Furniture? arm, string why)
    {
        var w = _w;
        var a = w.Automation;
        _lead.Remove(k.Id);
        var pt = P(k.Patient);
        var room = w.Surgery.TableOf(k)?.Room ?? arm?.Room;
        string kind = SurgerySystem.KindName(k.Kind);
        k.Notes.Add($"팔이 멈췄다 ({why})");
        if (k.State != CaseState.Operating)
        {
            // 아직 열지 않았다 — 사람에게 돌려준다
            k.State = CaseState.Waiting; k.Phase = 0; k.PhaseTicks = 0;
            w.Log.Add(w.Tick, LogKind.Warning, $"수술 팔이 섰다 — {why} · {pt?.Name} {kind}은 사람이 맡는다", pt?.Id ?? -1);
            return;
        }
        Stalls++;
        _orphan[k.Id] = w.Tick;
        if (arm?.Machine != null) MarkLog.Add(arm.Machine.Marks, w.Tick, $"{pt?.Name} {kind} 중에 멈췄다 ({why})");
        w.Log.Add(w.Tick, LogKind.Warning, $"수술 중에 팔이 멈췄다 — {why} · {pt?.Name}의 {kind}이 열린 채다", pt?.Id ?? -1);
        if (a.Present && a.CoreOnline)
            a.Speak.Announce(a.Voice.Style($"{room?.Name} — 팔이 멈췄습니다 ({why}). 곁에 있는 분이 이어받으십시오"), room, 3);
        if (pt != null) MarkLog.Add(pt.Memory.Marks, w.Tick, $"수술 중에 기계 팔이 멈췄다 ({why})");
        TakeOver(k);
    }

    /// <summary>열린 채 멈춘 수술을 사람이 이어받는다: 보조 → 의료를 아는 사람 → 아무 어른.</summary>
    private bool TakeOver(SurgeryCase k)
    {
        var w = _w;
        var pt = P(k.Patient);
        if (pt == null) return false;
        var by = P(k.Assistant) is CrewMember ax && ax.CanAct && !ax.Outside ? ax : StandIn(pt, k);
        if (by == null) return false;
        k.Surgeon = by.Id;
        if (k.Assistant == by.Id) k.Assistant = -1;
        Takeovers++;
        by.NextThinkTick = Math.Min(by.NextThinkTick, w.Tick + 1);
        by.Needs.Stress = MathF.Min(1f, by.Needs.Stress + 0.1f);
        by.Say(w, Persona.Say(by, "내가 잡을게 — 집게 줘"));
        w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(by.Name)} 멈춘 팔 대신 {pt.Name} 수술을 이어받는다", by.Id);
        MarkLog.Add(by.Memory.Marks, w.Tick, $"멈춘 수술 팔 대신 {pt.Name}의 칼을 잡았다");
        return true;
    }

    private void Orphans(float dt)
    {
        var w = _w;
        foreach (var id in _orphan.Keys.ToList())
        {
            var k = w.Surgery.Cases.FirstOrDefault(x => x.Id == id);
            if (k == null || k.State is CaseState.Done or CaseState.Deferred) { _orphan.Remove(id); continue; }
            var pt = P(k.Patient);
            bool held = k.Surgeon >= 0 && P(k.Surgeon) is CrewMember s && s.CanAct && w.Surgery.TableOf(k) is Furniture t && (s.Position - t.Center).Length() < 2.8f;
            if (held || pt == null) continue;
            // 아무도 붙잡지 않은 열린 상처 — 피가 빠진다
            pt.Vitals.Health = MathF.Max(w.CrewCanDie ? 0f : 0.02f, pt.Vitals.Health - 0.12f * dt);
            if (k.Surgeon < 0 && w.Tick % SimTime.Minutes(5) < World.SystemInterval) TakeOver(k);
        }
    }

    /// <summary>10분마다: 팔 전원 · 곁에서 거들기 · 교정 알림.</summary>
    private void Arms()
    {
        var w = _w;
        var a = w.Automation;
        foreach (var arm in w.Ship.FurnitureOf(FurnitureType.SurgicalArm))
        {
            if (arm.Room.Detached || arm.Machine is not Machine m) continue;
            bool busy = false;
            foreach (var k in w.Surgery.Cases)
            {
                if (k.State is not (CaseState.Prep or CaseState.Operating) || w.Surgery.TableOf(k)?.Room != arm.Room) continue;
                busy = true;
                if (k.Surgeon >= 0 && !_lead.ContainsKey(k.Id) && Works(arm, out _) && _assist.Add(k.Id))
                    w.Log.Add(w.Tick, LogKind.Work, $"수술 팔이 곁에서 거든다 — 시야를 비추고 · 벌려 잡고 · 흔들리지 않게 ({P(k.Surgeon)?.Name})", k.Surgeon);
            }
            m.Active = busy;
            if (Calib(arm) < 0.6f && a.Present && a.MainOnline)
                Tell($"calib:{arm.Id}", SimTime.Hours(8), () => a.Book.Add(ActKind.Advice, arm.Room, $"수술 팔 교정 {Calib(arm) * 100:0}%", "수술마다 · 흔들릴 때마다 조금씩 어긋난다",
                    "다시 맞춰 달라고 했다", "기관사 · 의무관: 팔 교정", $"armcal:{arm.Id}", SimTime.Hours(8)));
        }
        if (_assist.Count > 40) _assist.Remove(_assist.Min);
    }

    private void Tell(string key, long gap, Action act)
    {
        var w = _w;
        int h = 0; foreach (char ch in key) h = unchecked(h * 31 + ch);
        if (_told.TryGetValue(h, out var t) && w.Tick - t < gap) return;
        _told[h] = w.Tick;
        act();
    }

    // ───────────── 끝난 뒤 (Surgery.Finish가 부른다) ─────────────

    public void Finished(SurgeryCase k, CrewMember pt, CrewMember? s, bool ok, bool fatal)
    {
        var w = _w;
        var a = w.Automation;
        var room = w.Surgery.TableOf(k)?.Room;
        var arm = ArmIn(room);
        _lead.TryGetValue(k.Id, out var run);
        bool lead = s == null;
        bool assisted = s != null && _assist.Contains(k.Id);
        bool orphan = _orphan.Remove(k.Id);
        _lead.Remove(k.Id);
        _assist.Remove(k.Id);
        if (!lead && !assisted && !orphan) return;
        string kind = SurgerySystem.KindName(k.Kind);
        if (arm != null && (lead || assisted))
        {
            Ops++;
            _calib[arm.Id] = MathF.Max(0f, Calib(arm) - (lead ? 0.06f : 0.03f) - 0.02f * MathF.Min(5f, k.ShakeTicks / (float)SimTime.Minutes(1)));
            if (arm.Machine != null) MarkLog.Add(arm.Machine.Marks, w.Tick, $"{pt.Name} {kind} — {(lead ? "집도" : "보조")} · {(ok ? "살렸다" : fatal ? "잃었다" : "잘 안 됐다")}");
        }
        if (assisted) AssistOps++;
        if (orphan && s != null)
        {
            // 멈춘 팔 대신 마쳤다 — 사람 손을 믿게 되고, 컴퓨터는 조금 덜 믿는다
            a.Trusts.Change(pt, -0.06f, "수술 중에 기계 팔이 멈췄다", quiet: true);
            MarkLog.Add(s.Memory.Marks, w.Tick, ok ? $"멈춘 팔 대신 {pt.Name}의 {kind}을 마쳤다" : $"멈춘 팔 대신 잡은 {pt.Name}의 {kind} — 잘 안 됐다");
            if (ok) { pt.ChangeAffinity(s, 0.1f); w.Relations.Remember(pt, s, RelationReason.SavedMe, "멈춘 수술 팔 대신 칼을 잡아 줬다"); }
            w.History.Add(w, HistoryKind.Casualty, $"수술 팔이 멈춘 {pt.Name}의 {kind}을 {Ko.IGa(s.Name)} 이어받아 마쳤다 — {k.Outcome}", room, new[] { s, pt });
        }
        if (!lead) return;
        LeadOps++;
        var by = run != null && run.ConsentBy >= 0 ? P(run.ConsentBy) : null;
        var cmd = w.Command;
        if (ok)
        {
            Successes++;
            a.Trusts.Change(pt, 0.1f, $"컴퓨터가 집도한 {kind}으로 나았다");
            if (by != null && by != pt) a.Trusts.Change(by, 0.04f, $"{pt.Name}을 컴퓨터에 맡겼고 나았다", quiet: true);
            cmd.ComputerTrust = MathF.Min(0.98f, cmd.ComputerTrust + 0.02f);
            MarkLog.Add(pt.Memory.Marks, w.Tick, $"수술 팔이 {kind}을 해 줬다 — 떨림 하나 없었다");
            Life.Diary(w, pt, Persona.Say(pt, "기계 손이 나를 고쳤다. 떨림 하나 없었다"));
            if (a.Present) a.Book.Add(ActKind.Check, room, $"{pt.Name} {kind} — {k.Outcome}", $"가망 {k.Chance * 100:0}% · 팔 손 {(arm != null ? Hand(arm) * 100 : 0):0}%",
                "수술 팔 집도를 마쳤다", "", $"armdone:{k.Id}", SimTime.Hours(1));
            return;
        }
        Failures++;
        if (!fatal)
        {
            a.Trusts.Change(pt, -0.12f, $"컴퓨터에 맡긴 {kind}이 잘 안 됐다");
            if (by != null && by != pt) a.Trusts.Change(by, -0.06f, $"{pt.Name}을 컴퓨터에 맡겼는데 잘 안 됐다", quiet: true);
            cmd.ComputerTrust = MathF.Max(0.05f, cmd.ComputerTrust - 0.03f);
            MarkLog.Add(pt.Memory.Marks, w.Tick, $"수술 팔에 맡긴 {kind}이 잘 안 됐다");
            return;
        }
        Deaths++;
        cmd.ComputerTrust = MathF.Max(0.05f, cmd.ComputerTrust - 0.08f);
        foreach (var c in w.Crew)
        {
            if (c.Dead || c == pt || c.Away) continue;
            bool near = room != null && c.Room == room;
            if (near || c == by || c.AffinityTo(pt) > 0.3f)
                a.Trusts.Change(c, near || c == by ? -0.15f : -0.06f, $"컴퓨터가 집도하다 {Ko.EulReul(pt.Name)} 잃었다", quiet: !near && c != by);
            if (near) Memory.Frighten(w, c, room, 0.15f, $"수술 팔 아래서 {Ko.IGa(pt.Name)} 숨졌다");
        }
        if (by != null) MarkLog.Add(by.Memory.Marks, w.Tick, $"{pt.Name}을 수술 팔에 맡겼다 — 잃었다");
        Reviews++;
        w.Meetings.Reviews.Add(("computerask", 2, $"수술 팔이 집도한 {pt.Name}의 {kind} 중 숨졌다 — 칼은 사람이 잡자"));
        w.History.Add(w, HistoryKind.Casualty, $"주컴퓨터가 수술 팔로 집도하던 {pt.Name}의 {kind} — 수술대 위에서 숨졌다", room, by != null ? new[] { pt, by } : new[] { pt });
    }

    // ───────────── 교정 ─────────────

    public bool NeedsCalib(Furniture arm) => Calib(arm) < 0.6f && !w0Busy(arm);
    private bool w0Busy(Furniture arm) => _w.Surgery.Cases.Any(k => k.State is CaseState.Prep or CaseState.Operating && _w.Surgery.TableOf(k)?.Room == arm.Room);

    public void Calibrated(Furniture arm, CrewMember by)
    {
        var w = _w;
        _calib[arm.Id] = 1f;
        Calibrations++;
        by.Practice(Skill.Electrical, 0.03f);
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(by.Name)} 수술 팔을 다시 맞췄다 — 기준점 다섯 곳", by.Id);
        if (arm.Machine != null) MarkLog.Add(arm.Machine.Marks, w.Tick, $"{by.Name}: 교정");
    }

    /// <summary>화면: 팔이 지금 무엇을 하나 (0 쉼 · 1 거듦 · 2 집도 · 3 멈춤) · 뻗는 곳(수술대). 읽기만 한다.</summary>
    public (int mode, Vector2? at) PoseOf(Furniture arm)
    {
        var w = _w;
        bool works = Works(arm, out _);
        foreach (var k in w.Surgery.Cases)
        {
            if (k.State is not (CaseState.Prep or CaseState.Operating) || w.Surgery.TableOf(k) is not Furniture t || t.Room != arm.Room) continue;
            if (!works) return (3, t.Center);
            return (_lead.ContainsKey(k.Id) ? 2 : 1, t.Center);
        }
        foreach (var kv in _lead) if (kv.Value.Arm == arm.Id && works) return (1, null); // 환자를 기다린다
        return (works ? 0 : 3, null);
    }

    /// <summary>시험 · 장면: 팔을 어긋나게 한다.</summary>
    public void Jolt(Furniture arm, float amt) => _calib[arm.Id] = MathF.Max(0f, Calib(arm) - amt);

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var kv in _lead) { I(kv.Key); I(kv.Value.Arm); I(kv.Value.ConsentBy); }
        foreach (var kv in _calib) { I(kv.Key); F(kv.Value); }
        foreach (var kv in _orphan) { I(kv.Key); I(kv.Value); }
        foreach (var kv in _refused) { I(kv.Key); I(kv.Value); }
        I(Ops); I(LeadOps); I(AssistOps); I(Successes); I(Failures); I(Deaths); I(Stalls); I(Takeovers); I(Consents); I(Refusals); I(Calibrations);
    }
}

/// <summary>수술 팔 교정: 수술마다 어긋난 팔을 기관사 · 의무관이 기준점에 맞춰 다시 세운다.</summary>
public sealed class CalibrateArmActivity : Activity
{
    public override string Id => "armcalib";
    public override string Label => "수술 팔 교정";

    private static Furniture? Arm(World w)
    {
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.SurgicalArm))
            if (!f.Room.Detached && !f.Stowed && f.Machine is Machine m && m.Faults.Count == 0 && w.SurgArm.NeedsCalib(f) && f.UseSpots.Count > 0) return f;
        return null;
    }

    public override (float score, string reason) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct || c.Outside || c.IsChild || c.Pose == Pose.Sleeping) return (0f, "—");
        if (c.SkillLevel(Skill.Electrical) < 0.3f && c.Role != CrewRole.Medic) return (0f, "—");
        if (Arm(w) is not Furniture f || !dist.Reachable(f.UseSpots[0])) return (0f, "—");
        return (0.5f + (OnShift(c, w) ? 0.08f : 0f) + 0.1f * (0.6f - w.SurgArm.Calib(f)), $"수술 팔이 어긋났다 ({w.SurgArm.Calib(f) * 100:0}%)");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Arm(w) is not Furniture f) return null;
        var spot = f.UseSpots.Where(s => dist.Reachable(s)).OrderBy(s => dist.Get(s)).Cast<Cell?>().FirstOrDefault();
        if (spot is not Cell at) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.6f, Skill.Electrical, f.Center) { CanContinue = (cm, world) => world.SurgArm.NeedsCalib(f) });
        toils.Add(new DoToil((cm, world) => { world.SurgArm.Calibrated(f, cm); return true; }));
        return new Job(this, "수술 팔 교정", toils) { LogText = "수술 팔 기준점을 맞추러 간다", TargetRoom = f.Room, Target = f };
    }
}
