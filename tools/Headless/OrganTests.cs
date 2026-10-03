using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// 의료 2차 — 장기 손상 · 인공 장기 · 이식 · 거부반응 · 감염 · 격리
public static partial class Program
{
    /// <summary>치료 침대 곁(두 칸 안)에 설비를 놓는다.</summary>
    private static Furniture? OgPut(World w, FurnitureType t, Furniture bed)
    {
        var room = bed.Room;
        var at = bed.UseSpots.Count > 0 ? bed.UseSpots[0] : bed.Cells[0];
        foreach (var c in room.Cells.OrderBy(c => (c.Center - at.Center).LengthSquared()).ThenBy(c => c.X).ThenBy(c => c.Y))
        {
            if (!w.Ship.IsOpenFloor(c) || (c.Center - at.Center).Length() > 2.2f || bed.UseSpots.Contains(c) || !Adaptation.SafeToBlock(w, room, c)) continue;
            var f = w.Ship.AddFurniture(t, c);
            w.Paths.Invalidate();
            w.Structure.Touch();
            return f;
        }
        return null;
    }

    private static Furniture? OgPutRoom(World w, FurnitureType t, Room room)
    {
        if (Modules.Spot(w, room) is not Cell c) return null;
        var f = w.Ship.AddFurniture(t, c);
        w.Paths.Invalidate();
        w.Structure.Touch();
        return f;
    }

    private static bool OgRunUntil(World w, long max, Func<bool> done, long step = 150)
    {
        for (long t = 0; t < max; t += step) { Run(w, step); if (done()) return true; }
        return done();
    }

    private static List<CrewMember> OgCrew(World w) => w.Crew.Where(c => !c.Dead && !c.Down && !c.IsChild && !c.Away && !c.Outside).OrderBy(c => c.Id).ToList();

    private static int RunOrganTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"장기 · 이식 · 감염 점검 · 시드 {seed}\n");
        try
        {
            OgLungs(seed);
            OgKidney(seed);
            OgDonor(seed, true);
            OgDonor(seed, false);
            OgReject(seed);
            OgSepsis(seed);
            OgIsolate(seed);
            OgLiving(seed);
            OgPumpPrint(seed);
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint h1 = H(), h2 = H();
            Check("결정론 — 같은 시드 하루 반 지문이 같다", h1 == h2, $"{h1:x8} / {h2:x8}");
        }
        catch (Exception e) { Check("예외 없음", false, e.ToString()); }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }

    // 1) 연기 흡입 → 폐 손상 → 숨이 차다(행동) → 생체 신호 진단 → 인공 폐
    private static void OgLungs(int seed)
    {
        var w = DayOne(seed, "Hanbit");
        var bed = w.Ship.FurnitureOf(FurnitureType.MedBed).OrderBy(f => f.Id).FirstOrDefault();
        if (bed == null) { Check("인공 폐 — 치료 침대가 있다", false); return; }
        var ecmo = OgPut(w, FurnitureType.Ecmo, bed);
        var a = OgCrew(w).First(c => !OrganCareActivity.Medic(c));
        NeedsSystem.AddInjury(a.Vitals, 0.85f, "연기 흡입");
        OgRunUntil(w, SimTime.Hours(5), () => w.Organs.Dmg(a, Organ.Lungs) >= 0.7f && a.Fx.WalkMul < 0.95f, SimTime.Minutes(20));
        var b = w.Organs.Of(a);
        float lung = b.Dmg[(int)Organ.Lungs];
        var ail = a.Ailments.FirstOrDefault(x => x.Id == "lungfail");
        Check("폐 손상 — 연기 흡입이 폐를 상하게 하고 · 숨이 찬다 (걸음 · 일 손이 느려진다)", lung >= 0.7f && ail != null && a.Fx.Walk > 0f && a.Fx.WalkMul < 0.95f,
            $"{a.Name} 폐 {lung * 100:0}% 상함 ({b.Cause[0]}) · 앓는 것 {ail?.Id ?? "-"} {(ail != null ? w.Ailments.Severity(ail) : 0f):0.00} · 걸음 {a.Fx.WalkMul:0.00} · 일 {a.Fx.WorkMul:0.00}");
        bool hooked = OgRunUntil(w, SimTime.Hours(14), () => w.Organs.Peek(a)?.Hooked == true);
        var diag = w.Log.Entries.LastOrDefault(e => e.Text.Contains("생체 신호") && e.Text.Contains(a.Name));
        Check("진단 — 주컴퓨터가 생체 신호로 폐를 읽거나 의무관이 진단한다", w.Organs.Known(a, Organ.Lungs), diag.Text ?? $"방송 없음 · {w.Ailments.Line(a)}");
        Check("인공 폐 — 진단받은 사람이 기계 곁에 눕고 의무관이 연결한다", hooked && ecmo != null && w.Organs.PatientOn(ecmo) == a,
            $"{(ecmo != null ? ecmo.Name : "설치 실패")} · 연결 {hooked} · {a.Name} {a.Job?.Label} · {w.Organs.Stats.Line()}");
        float l0 = w.Organs.Dmg(a, Organ.Lungs);
        Run(w, SimTime.Hours(6));
        float l1 = w.Organs.Dmg(a, Organ.Lungs);
        Check("인공 폐 — 기계가 숨을 대신하는 동안 피에 산소가 차고 폐가 쉬며 낫는다", a.Vitals.Oxygen > 0.85f && l1 < l0,
            $"산소 {a.Vitals.Oxygen:0.00} · 폐 {l0 * 100:0}% → {l1 * 100:0}% · 연결 {w.Organs.Peek(a)?.Hooked}");
    }

    // 2) 신장 부전 → 투석 / 3) 정전 중 투석기 → 컴퓨터가 회로를 지킨다
    private static void OgKidney(int seed)
    {
        Furniture? dia = null;
        CrewMember? k = null;
        World Scene(bool guard)
        {
            OrganSystem.NoGuard = !guard;
            var w = DayOne(seed, "Hanbit");
            var bed = w.Ship.FurnitureOf(FurnitureType.MedBed).OrderBy(f => f.Id).First();
            dia = OgPut(w, FurnitureType.Dialyzer, bed);
            k = OgCrew(w).Last(c => !OrganCareActivity.Medic(c));
            w.Organs.Hurt(k, Organ.Kidney, 0.9f, "탈수");
            w.Organs.Of(k).Uremia = 0.7f;
            return w;
        }
        var w = Scene(true);
        Run(w, SimTime.Hours(1));
        var ka = k!.Ailments.FirstOrDefault(x => x.Id == "kidneyfail");
        Check("신장 부전 — 붓고 메스껍다 (앓는 것으로 몸에 나온다)", ka != null, $"{k.Name} 신장 {w.Organs.Dmg(k, Organ.Kidney) * 100:0}% · 노폐물 {w.Organs.Of(k).Uremia:0.00} · {w.Ailments.Line(k)}");
        bool hooked = OgRunUntil(w, SimTime.Hours(16), () => w.Organs.Peek(k)?.Hooked == true);
        Check("투석 — 진단받은 사람이 투석기 곁에 눕고 연결된다", hooked, $"연결 {hooked} · {k.Name} {k.Job?.Label} · 진단 {w.Organs.Known(k, Organ.Kidney)} · {w.Organs.Stats.Line()}");
        // 정전: 냉각 펌프가 서서 원자로가 멎고 배터리만 남았다
        foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
        w.Power.BatteryCharge = w.Power.BatteryCapacity * 0.3f;
        int shed = 0, poweredTicks = 0, n = 0;
        for (int i = 0; i < 40; i++) { Run(w, SimTime.Minutes(1)); n++; if (dia!.Machine!.Powered) poweredTicks++; shed = Math.Max(shed, w.Power.ShedCount + w.Power.ParkedCount); }
        bool guarded = w.Organs.Guarded(dia!);
        var reason = w.Log.Entries.LastOrDefault(e => e.Text.Contains("주 컴퓨터") && e.Text.Contains(dia.Name));
        Check("정전 — 투석 중인 투석기 회로를 주컴퓨터가 지킨다 (전기를 몰아주고 다른 것을 먼저 내린다)", guarded && poweredTicks >= n * 3 / 4 && shed > 0,
            $"지킴 {guarded} · 켜져 있던 분 {poweredTicks}/{n} · 내린 설비 {shed} · 공급 {w.Power.Delivered:0.#}/{w.Power.Demand:0.#}kW · \"{reason.Text}\"");
        Run(w, SimTime.Hours(5));
        var kb = w.Organs.Of(k);
        Check("투석 — 노폐물을 걸러 낸다 (세션이 끝나면 기계에서 뗀다)", kb.Uremia < 0.3f && w.Organs.Stats.Sessions + (kb.Hooked ? 1 : 0) >= 1,
            $"노폐물 {kb.Uremia:0.00} · 투석 {w.Organs.Stats.Sessions} · 멈춤 {w.Organs.Stats.Aborted}");
        // 견줌: 컴퓨터가 지키지 않으면 (같은 정전) — 투석기가 먼저 꺼진다
        var w2 = Scene(false);
        OgRunUntil(w2, SimTime.Hours(16), () => w2.Organs.Peek(k!)?.Hooked == true);
        foreach (var p in w2.Ship.FurnitureOf(FurnitureType.CoolantPump)) w2.Machines.Break(p.Machine!, FaultKind.PumpSeized);
        w2.Power.BatteryCharge = w2.Power.BatteryCapacity * 0.3f;
        int off2 = 0;
        for (int i = 0; i < 40; i++) { Run(w2, SimTime.Minutes(1)); if (!dia!.Machine!.Powered) off2++; }
        OrganSystem.NoGuard = false;
        Check("견줌 — 지키지 않으면 같은 정전에 투석기가 꺼져 내장 전지로 버틴다 (정보)", true, $"꺼져 있던 분 {off2}/40 · 끊김 {w2.Organs.Stats.PowerLost} · 멈춤 {w2.Organs.Stats.Aborted}");
    }

    // 4) 숨진 동료의 장기 — 생전 뜻 · 회의 → 떼어 보관 → 이식 · "그 사람 신장으로 산다"
    private static void OgDonor(int seed, bool consent)
    {
        var w = DayOne(seed, "Hanbit");
        var bed = w.Ship.FurnitureOf(FurnitureType.MedBed).OrderBy(f => f.Id).First();
        var cooler = OgPut(w, FurnitureType.OrganCooler, bed) ?? OgPutRoom(w, FurnitureType.OrganCooler, bed.Room);
        var crew = OgCrew(w);
        var medic = crew.FirstOrDefault(OrganCareActivity.Medic) ?? crew[0];
        medic.SkillLevels[(int)Skill.Medicine] = 1f;
        var others = crew.Where(c => c != medic).ToList();
        var donor = others[0];
        var rc = others[^1];
        donor.Habits.Remove(Habit.Superstitious);
        donor.Hobbies.Remove(Hobby.Meditation);
        var ov = w.Values.Of(donor);
        if (consent) { ov.V[(int)Axis.Mercy] = 0.9f; ov.V[(int)Axis.Commune] = 0.6f; }
        else donor.Habits.Add(Habit.Superstitious);
        w.Organs.Hurt(rc, Organ.Kidney, 0.9f, "결석");
        w.Organs.Of(rc).Uremia = 0.5f;
        Run(w, SimTime.Minutes(20));
        foreach (var a in rc.Ailments) a.Diagnosed = true;
        int react0 = w.Values.Stats.Reactions;
        w.CrewCanDie = true;
        donor.Vitals.Health = 0f;
        Run(w, SimTime.Minutes(30));
        w.CrewCanDie = false;
        var d = w.Transplant.Donors.GetValueOrDefault(donor.Id);
        var dec = w.History.Events.LastOrDefault(h => h.Text.StartsWith("회의") && h.Text.Contains(donor.Name));
        if (!consent)
        {
            Check("기증 반대 — 생전 뜻(몸을 온전히)을 따라 장기를 떼지 않는다 · 회의 기록 · 사람마다 마음이 남는다",
                donor.Dead && d != null && d.Verdict < 0 && dec != null && w.Values.Stats.Reactions > react0,
                $"{donor.Name} 숨짐 {donor.Dead} · 결정 {d?.Verdict} (찬성 {d?.Yes} · 반대 {d?.No}) · \"{dec?.Text}\" · 반응 {w.Values.Stats.Reactions - react0}");
            Run(w, SimTime.Hours(3));
            Check("기증 반대 — 떼어 낸 장기가 없다", w.Transplant.Grafts.Count == 0, $"장기 {w.Transplant.Grafts.Count}");
            return;
        }
        Check("기증 동의 — 생전 뜻(남을 살리는 데)과 회의로 몇십 분 안에 정한다 · 주컴퓨터가 남은 시간과 맞는 사람을 알린다",
            donor.Dead && d != null && d.Verdict > 0 && dec != null && w.Log.Entries.Any(e => e.Text.Contains("[방송]") && e.Text.Contains(donor.Name) && e.Text.Contains("시간")),
            $"결정 {d?.Verdict} (찬성 {d?.Yes} · 반대 {d?.No}) · \"{dec?.Text}\" · 반응 {w.Values.Stats.Reactions - react0}");
        bool harvested = OgRunUntil(w, SimTime.Hours(4), () => d!.Harvested);
        var kid = w.Transplant.Grafts.Where(g => g.Organ == Organ.Kidney).ToList();
        Check("떼어 보관 — 의무관이 시간 안에 떼어 장기 보관함에 넣는다 (차게 두면 오래 간다)", harvested && kid.Count == 2 && kid.All(g => g.Cooler == cooler?.Id),
            $"뗌 {harvested} ({w.Crew.FirstOrDefault(c => c.Id == d!.Harvester)?.Name}) · 신장 {kid.Count} · 보관함 {kid.Count(g => g.Cooler >= 0)} · {w.Transplant.Stats.Line()}");
        bool op = OgRunUntil(w, SimTime.Hours(30), () =>
        {
            var kg = w.Transplant.Grafts.FirstOrDefault(g => g.For == rc.Id && !g.Gone);
            return w.Transplant.Stats.Success > 0 || w.Transplant.Stats.Ops >= 2;
        }); // 첫 수술이 잘못되면 두 번째 신장으로
        var tb = w.Organs.Of(rc);
        bool ok = w.Transplant.Stats.Success > 0;
        Check("이식 — 받을 사람이 치료 침대에 눕고 의무관이 수술한다 (성공률은 한 곳에서)", op,
            $"수술 {w.Transplant.Stats.Ops} · 성공 {w.Transplant.Stats.Success} · 실패 {w.Transplant.Stats.Failed} · {rc.Name} 신장 {tb.Dmg[3] * 100:0}% · 이식 {tb.Graft[3]} · 마지막 확률 {w.Transplant.LastChance:0.00}");
        if (ok)
            Check("그 사람 신장으로 산다 — 기억 · 연대기", rc.Memory.Marks.Any(m => m.Text.Contains(donor.Name)) && w.History.Events.Any(h => h.Text.Contains($"{donor.Name}의 신장")),
                string.Join(" / ", rc.Memory.Marks.TakeLast(2).Select(m => m.Text)));
    }

    // 5) 거부반응 · 면역억제제
    private static void OgReject(int seed)
    {
        var w = DayOne(seed, "Hanbit");
        var c = OgCrew(w).First(x => !OrganCareActivity.Medic(x));
        var b = w.Organs.Of(c);
        b.Graft[(int)Organ.Kidney] = 999; b.Match[(int)Organ.Kidney] = 0.33f; b.Dmg[(int)Organ.Kidney] = 0.15f;
        // 약이 다 떨어졌다
        var stash = new List<(Furniture f, int n)>();
        foreach (var f in w.Ship.Furniture) if (f.Storage != null && f.Storage.Count(ItemKind.Immunosuppressant) > 0) { int n = f.Storage.Count(ItemKind.Immunosuppressant); f.Storage.Take(ItemKind.Immunosuppressant, n); stash.Add((f, n)); }
        bool rej = OgRunUntil(w, SimTime.Hours(40), () => b.Reject >= 0.35f, SimTime.Hours(1));
        Run(w, SimTime.Hours(1));
        Check("거부반응 — 면역억제제가 떨어지면 몸이 이식받은 장기를 밀어낸다 · 주컴퓨터가 징후와 남은 약을 알린다",
            rej && c.Ailments.Any(x => x.Id == "rejection") && w.Log.Entries.Any(e => e.Text.Contains("거부반응") && e.Text.Contains("떨어졌다")),
            $"거부반응 {b.Reject:0.00} · 신장 {b.Dmg[3] * 100:0}% · {w.Ailments.Line(c)}");
        foreach (var (f, n) in stash) f.Storage!.Add(ItemKind.Immunosuppressant, n);
        float r0 = b.Reject;
        bool dosed = OgRunUntil(w, SimTime.Hours(10), () => b.DoseAt >= 0);
        Run(w, SimTime.Hours(20));
        Check("면역억제제 — 약이 오면 스스로 챙겨 먹고 거부반응이 가라앉는다 · 먹는 동안은 몸이 약하다", dosed && b.Reject < r0 && w.Organs.Suppressed(c),
            $"먹음 {b.Doses}번 · 거부반응 {r0:0.00} → {b.Reject:0.00} · 약한 몸 {w.Organs.Suppressed(c)}");
    }

    // 6) 상처 감염 → 패혈증 → 장기 · 멸균 실패
    private static void OgSepsis(int seed)
    {
        var w = DayOne(seed, "Hanbit");
        var c = OgCrew(w).First(x => !OrganCareActivity.Medic(x));
        foreach (var f in w.Ship.Furniture) if (f.Storage != null && f.Storage.Count(ItemKind.MedKit) > 0) f.Storage.Take(ItemKind.MedKit, f.Storage.Count(ItemKind.MedKit));
        w.Growth.NoFirstAid = true; // 치료하지 못한다
        NeedsSystem.AddInjury(c.Vitals, 0.4f, "작업 중 베임");
        c.Vitals.Frailty = 0.3f;
        w.Ailments.Catch(c, "woundinf", null, "시험");
        bool sep = OgRunUntil(w, SimTime.Hours(60), () => c.Ailments.Any(x => x.Id == "sepsis"), SimTime.Hours(1));
        Run(w, SimTime.Hours(12));
        var ob = w.Organs.Peek(c);
        Check("패혈증 — 곪은 상처를 오래 두면 패혈증이 되고 신장 · 폐 · 간이 함께 상한다", sep && ob != null && ob.Dmg[(int)Organ.Kidney] > 0.05f && ob.Dmg[(int)Organ.Lungs] > 0.02f,
            $"패혈증 {sep} · 신장 {(ob?.Dmg[3] ?? 0f) * 100:0.0}% · 폐 {(ob?.Dmg[0] ?? 0f) * 100:0.0}% · {w.Ailments.Line(c)} · {w.Infection.Stats.Line()}");
        w.Growth.NoFirstAid = false;
        // 멸균 실패: 멎은 멸균기 · 더러운 손 · 소독약 없음
        var medic = OgCrew(w).FirstOrDefault(OrganCareActivity.Medic) ?? OgCrew(w)[0];
        var room = w.Ship.FurnitureOf(FurnitureType.MedBed).First().Room;
        var clave = room.Furniture.FirstOrDefault(f => f.Type == FurnitureType.Autoclave) ?? OgPutRoom(w, FurnitureType.Autoclave, room);
        if (clave?.Machine != null) w.Machines.Break(clave.Machine, FaultKind.ThermostatFault);
        foreach (var f in w.Ship.Furniture) if (f.Storage != null && f.Storage.Count(ItemKind.Disinfectant) > 0) f.Storage.Take(ItemKind.Disinfectant, f.Storage.Count(ItemKind.Disinfectant));
        medic.Soil.Hands[(int)SoilKind.Bio] = 1f;
        var pts = OgCrew(w).Where(x => x != medic && x != c).ToList();
        int fails = 0;
        foreach (var p in pts) if (!w.Infection.Sterile(medic, p, room, "관 꽂은 자리")) fails++;
        Check("멸균 실패 — 멎은 멸균기 · 덜 씻은 손 · 소독약이 없으면 꽂은 자리가 곪는다 (무엇이 모자랐는지 남는다)", fails > 0 && room.Marks.Any(m => m.Text.Contains("소독이 덜 됐다")),
            $"{fails}/{pts.Count} 곪음 · \"{room.Marks.LastOrDefault().Text}\"");
    }

    // 7) 열병 → 격리 권고 · 댐퍼 · 음압 격리실
    private static void OgIsolate(int seed)
    {
        var w = DayOne(seed, "Hanbit");
        var crew = OgCrew(w);
        var medbay = w.Ship.FurnitureOf(FurnitureType.MedBed).First().Room;
        var ward = w.Ship.Rooms.FirstOrDefault(r => r.Kind == RoomType.Quarantine && !r.Detached) ?? medbay;
        var neg = OgPutRoom(w, FurnitureType.NegPressure, ward);
        var s = crew.First(c => c.Room != null && c.Room != ward && c.Room.Type != RoomType.Medbay && !OrganCareActivity.Medic(c));
        w.Disease.Infect(s, null);
        s.InfectedAt = w.Tick - SimTime.TicksPerDay * 4 / 5;
        var home = s.Room!;
        Run(w, SimTime.Minutes(25));
        bool advised = w.Infection.Advised(s);
        bool shut = w.Log.Entries.Any(e => e.Text.Contains("댐퍼를 닫았다"));
        Check("격리 권고 — 주컴퓨터가 열병을 읽고 격리를 권하고 · 앓는 사람의 방 댐퍼를 닫는다 (공기가 다른 방으로 가지 않게)",
            advised && w.Log.Entries.Any(e => e.Text.Contains("격리 권고") && e.Text.Contains(s.Name)) && w.Infection.Stats.Dampers > 0,
            $"권고 {advised} · 댐퍼 {w.Infection.Stats.Dampers} ({(shut ? "닫음" : "-")}) · {home.Name} 환기 {home.VentOpen} · 음압기 {(neg != null ? ward.Name : "설치 실패")}");
        bool went = OgRunUntil(w, SimTime.Hours(6), () => s.Room == ward);
        Check("격리 — 앓는 사람이 음압 격리실로 간다 (따르는 사람)", went || w.Infection.Refuses(s), $"{s.Name} → {s.Room?.Name} · {s.Job?.Label} · 버팀 {w.Infection.Refuses(s)}");
        Run(w, SimTime.Hours(1));
        Check("댐퍼 — 앓는 사람이 떠나면 (또는 숨이 차면) 컴퓨터가 다시 연다 · 음압 격리실은 닫지 않는다",
            !went || w.Infection.Stats.Reopened > 0 && ward.VentOpen && Infection_Negative(ward),
            $"다시 엶 {w.Infection.Stats.Reopened} · {ward.Name} 환기 {ward.VentOpen} · 음압 {Infection_Negative(ward)} · {w.Infection.Stats.Line()}");
    }

    // 8) 산 사람의 기증 — 가까운 사람이 신장 하나를 내놓는다 (희생 · 관계 · 기억)
    private static void OgLiving(int seed)
    {
        var w = DayOne(seed, "Hanbit");
        var crew = OgCrew(w);
        var medic = crew.FirstOrDefault(OrganCareActivity.Medic) ?? crew[0];
        medic.SkillLevels[(int)Skill.Medicine] = 1f;
        var rc = crew.Last(c => c != medic);
        var t = w.Transplant;
        var giver = crew.Where(c => c != medic && c != rc).OrderByDescending(c => TransplantSystem.MatchOf(t.Tissue(c), t.Tissue(rc))).ThenBy(c => c.Id).First();
        giver.ChangeAffinity(rc, 0.9f);
        w.Values.Of(giver).V[(int)Axis.Mercy] = 0.8f;
        w.Organs.Hurt(rc, Organ.Kidney, 0.9f, "결석");
        Run(w, SimTime.Minutes(20));
        foreach (var a in rc.Ailments) a.Diagnosed = true;
        bool offered = OgRunUntil(w, SimTime.Hours(2), () => t.Stats.Offers > 0);
        Check("산 사람의 기증 — 조직이 맞고 가까운 사람이 스스로 나선다 (기록 · 연대기)", offered && w.History.Events.Any(h => h.Text.Contains(giver.Name) && h.Text.Contains("주겠다고")),
            $"{giver.Name} → {rc.Name} · 조직 {TransplantSystem.MatchOf(t.Tissue(giver), t.Tissue(rc)) * 6:0}/6 · 나섬 {t.Stats.Offers} · {t.Stats.Line()}");
        bool done = OgRunUntil(w, SimTime.Hours(48), () => t.Stats.Living > 0 && (t.Stats.Success > 0 || t.Stats.Failed > 0));
        var gb = w.Organs.Peek(giver);
        Check("산 사람의 기증 — 떼어 준 사람은 신장이 하나 · 받은 사람과 깊이 이어진다 · 기억에 남는다",
            done && gb is { OneKidney: true } && giver.Memory.Marks.Any(m => m.Text.Contains("신장 하나")) && (t.Stats.Success == 0 || rc.AffinityTo(giver) > 0.2f),
            $"떼기 {t.Stats.Living} · 이식 성공 {t.Stats.Success} 실패 {t.Stats.Failed} · {giver.Name} 신장 하나 {gb?.OneKidney} · {rc.Name}→{giver.Name} {rc.AffinityTo(giver):0.00}");
    }

    // 9) 인공 심장 (충전) · 손 펌프 (인공 폐 전원이 끊겼다) · 배양 장기 (바이오 프린터)
    private static void OgPumpPrint(int seed)
    {
        var w = DayOne(seed, "Hanbit");
        var crew = OgCrew(w);
        var bed = w.Ship.FurnitureOf(FurnitureType.MedBed).OrderBy(f => f.Id).First();
        var dock = OgPut(w, FurnitureType.HeartPump, bed) ?? OgPutRoom(w, FurnitureType.HeartPump, bed.Room);
        var medic = crew.FirstOrDefault(OrganCareActivity.Medic) ?? crew[0];
        medic.SkillLevels[(int)Skill.Medicine] = 1f;
        var h = crew.First(c => c != medic);
        w.Organs.Hurt(h, Organ.Heart, 0.88f, "멎었던 심장");
        Run(w, SimTime.Minutes(20));
        foreach (var a in h.Ailments) a.Diagnosed = true;
        long t0 = w.Tick;
        bool pump = OgRunUntil(w, SimTime.Hours(30), () =>
        {
            return w.Organs.Peek(h)?.Pump == true || w.Transplant.Stats.Failed > 0;
        });
        var hb = w.Organs.Of(h);
        Check("인공 심장 — 심장이 버티지 못하면 의무관이 가슴에 펌프를 단다", pump, $"펌프 {hb.Pump} · 수술 {w.Transplant.Stats.Ops} 실패 {w.Transplant.Stats.Failed} · 심장 {hb.Dmg[1] * 100:0}%");
        if (hb.Pump)
        {
            hb.PumpCharge = 0.3f;
            bool charged = OgRunUntil(w, SimTime.Hours(4), () => hb.PumpCharge >= 0.95f);
            Check("인공 심장 — 전지가 줄면 스스로 충전대에 앉아 채운다", charged, $"전지 {hb.PumpCharge * 100:0}% · {h.Name} {h.Job?.Label} · 충전대 {dock?.Name}");
        }
        // 손 펌프: 인공 폐에 매달린 사람 · 기계 전선이 끊겼다
        var w2 = DayOne(seed, "Hanbit");
        var bed2 = w2.Ship.FurnitureOf(FurnitureType.MedBed).OrderBy(f => f.Id).First();
        var ecmo = OgPut(w2, FurnitureType.Ecmo, bed2)!;
        var p = OgCrew(w2).First(c => !OrganCareActivity.Medic(c));
        NeedsSystem.AddInjury(p.Vitals, 0.9f, "유독 가스");
        OgRunUntil(w2, SimTime.Hours(14), () => w2.Organs.Peek(p)?.Hooked == true);
        ecmo.Machine!.Feed = 0f;
        bool crank = OgRunUntil(w2, SimTime.Hours(3), () => w2.Organs.Stats.Cranked > 0);
        Check("손 펌프 — 인공 폐 전선이 끊기면 내장 전지로 돌고 · 바닥나면 곁의 사람이 손으로 돌린다 · 주컴퓨터가 알린다",
            w2.Organs.Peek(p)?.Hooked == true && w2.Organs.Stats.PowerLost > 0 && crank && w2.Log.Entries.Any(e => e.Text.Contains("인공 폐") && e.Text.Contains("전원이 끊겼다")),
            $"연결 {w2.Organs.Peek(p)?.Hooked} · 끊김 {w2.Organs.Stats.PowerLost} · 손 펌프 {w2.Organs.Stats.Cranked} · 산소 {p.Vitals.Oxygen:0.00}");
        // 배양 장기: 기술 · 세포 잉크 · 양액 → 받을 사람의 세포로
        var w3 = DayOne(seed, "Hanbit");
        w3.Eras.Known.Add("regenmed");
        var c3 = OgCrew(w3);
        var lab = w3.Ship.Rooms.FirstOrDefault(r => r.Type == RoomType.Lab && !r.Detached) ?? w3.Ship.FurnitureOf(FurnitureType.MedBed).First().Room;
        var printer = OgPutRoom(w3, FurnitureType.BioPrinter, lab);
        OrganSystem.Stock(w3, ItemKind.BioInk, 2); OrganSystem.Stock(w3, ItemKind.Nutrient, 2);
        c3[0].SkillLevels[(int)Skill.Botany] = 0.8f;
        var lv = c3[^1];
        w3.Organs.Hurt(lv, Organ.Liver, 0.85f, "약을 너무 많이 썼다");
        bool started = OgRunUntil(w3, SimTime.Hours(6), () => w3.Transplant.Prints.Count > 0);
        bool printed = OgRunUntil(w3, SimTime.Hours(48), () => w3.Transplant.Stats.Printed > 0, SimTime.Hours(1));
        var pg = w3.Transplant.Grafts.FirstOrDefault(g => g.Printed);
        Check("배양 장기 — 재생 의학을 알고 잉크가 있으면 받을 사람의 세포로 장기를 찍고 · 몸이 밀어내지 않는다 (조직 6/6)",
            printer != null && started && printed && pg != null && pg.For == lv.Id && w3.Transplant.Match(pg, lv) >= 1f,
            $"프린터 {printer?.Room.Name} · 시작 {started} · 다 찍음 {printed} · 받을 사람 {pg?.For} ({lv.Id}) · {w3.Transplant.Stats.Line()}");
    }

    private static bool Infection_Negative(Room r) => InfectionSystem.Negative(r);
}
