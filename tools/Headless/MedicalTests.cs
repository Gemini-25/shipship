using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// 의료 1차 — 수술 · 수혈 · 약 · 회복 (검증 장면이 헤드리스에서 실제로 일어나는지)
public static partial class Program
{
    private static int RunMedicalTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"의료 1차 점검 · 시드 {seed}\n");
        try
        {
            static System.Collections.Generic.List<CrewMember> Able(World w) => w.Crew.Where(c => !c.Dead && !c.Down && !c.IsChild && !c.Away && !c.Outside).OrderBy(c => c.Id).ToList();
            static void Strip(World w, ItemKind k) { while (ItemsV15.Use(w, k)) { } }
            static Furniture? Cab(World w) => w.Ship.FurnitureOf(FurnitureType.MedCabinet).FirstOrDefault() ?? w.Ship.Furniture.FirstOrDefault(f => f.Type == FurnitureType.Shelf && f.Storage != null);
            static CrewMember Patient(World w, int skip = 0) => Able(w).Where(c => c.Role != CrewRole.Medic && c.SkillLevel(Skill.Medicine) < 0.3f).Skip(skip).First();
            // 크게 다쳤다 → 곁의 사람이 바로 눌러 피를 멈췄다 (중상으로 남는다)
            static void Hurt(World w, CrewMember c, float amt, string cause)
            {
                NeedsSystem.AddInjury(c.Vitals, amt, cause);
                Run(w, SimTime.Minutes(1));
                c.Vitals.TreatedTick = w.Tick;
                Run(w, SimTime.Minutes(2));
            }
            static SurgeryCase? Done(World w, CrewMember c, SurgeryKind? k = null) => w.Surgery.Done.LastOrDefault(x => x.Patient == c.Id && (k == null || x.Kind == k) && x.Outcome != "칼을 대지 않아도 되게 됐다");
            static void Until(World w, Func<bool> ok, float hours) { long end = w.Tick + SimTime.Hours(hours); while (w.Tick < end && !ok()) Run(w, SimTime.Minutes(1)); }

            // ── 0) 수술실: 배가 처음부터 싣고 나온다 ──
            var w = DayOne(seed, "Hanbit");
            var med = w.Ship.RoomsOf(RoomType.Medbay).OrderByDescending(r => r.Cells.Count).First();
            var kinds = new[] { FurnitureType.OperatingTable, FurnitureType.SurgicalLamp, FurnitureType.AnesthesiaMachine, FurnitureType.Autoclave, FurnitureType.BloodFridge, FurnitureType.MedCabinet };
            Check("수술실 — 의무실에 수술대 · 무영등 · 마취기 · 멸균기 · 혈액 냉장고 · 약장", kinds.All(t => med.Furniture.Any(f => f.Type == t)),
                string.Join(" · ", kinds.Select(t => $"{FurnitureTypes.Name(t)} {med.Furniture.Count(f => f.Type == t)}")) + $" · 피 {w.Blood.Count()}팩 · 마취제 {w.Pharmacy.Stock(ItemKind.Anesthetic)}");
            var types = Able(w).Select(c => w.Blood.TypeOf(c).Name).ToList();
            var w0 = DayOne(seed, "Hanbit");
            Check("혈액형 — 사람마다 정해져 있다 (같은 시드면 같다 · 여러 형)", types.SequenceEqual(Able(w0).Select(c => w0.Blood.TypeOf(c).Name)) && types.Distinct().Count() >= 3,
                string.Join(" ", Able(w).Take(8).Select(c => $"{c.Name}:{w.Blood.TypeOf(c).Name}")));

            // ── 1) 골절 중상 → 수술 성공 (집도 · 보조 · 멸균 · 예측) ──
            var a = Patient(w);
            Hurt(w, a, 0.34f, "작업 중 넘어짐");
            var g1 = w.Grades.Now(a);
            Until(w, () => Done(w, a, SurgeryKind.Fracture) != null, 14f);
            var k1 = Done(w, a, SurgeryKind.Fracture);
            var s1 = k1 != null ? w.Crew.First(c => c.Id == k1.Surgeon) : null;
            Check("골절 중상 → 수술 — 집도의가 수술대에서 고정한다 (손 씻기 · 마취 · 멸균)", g1 == InjuryGrade.Serious && k1 != null && k1.Success && k1.Anesthesia && k1.Sterile >= 0.75f,
                k1 == null ? $"{a.Name} {InjuryGradeSystem.Name(g1)} · 열린 수술 {string.Join(",", w.Surgery.Cases.Select(x => $"{x.Kind}:{x.State}:{x.Surgeon}"))} · 끝난 {string.Join(",", w.Surgery.Done.Select(x => $"{x.Kind}:{x.Outcome}"))}"
                    : $"{a.Name} {SurgerySystem.KindName(k1.Kind)} — {k1.Outcome} · 집도 {s1?.Name} · 보조 {k1.Assistant} · 가망 {k1.Chance * 100:0}% · {string.Join(" · ", k1.Notes)}");
            Check("수술 뒤 — 고정한 뼈는 빨리 붙는다 · 집도의는 '살렸다'를 기억한다 · 주컴퓨터가 피를 예측했다",
                k1 != null && w.Recovery.HealMul(a) > 1.2f && s1 != null && s1.Memory.Marks.Any(m => m.Text.Contains("살렸다")) && w.Surgery.BleedForecasts > 0,
                $"낫는 빠르기 ×{w.Recovery.HealMul(a):0.0} · 예측 {w.Surgery.BleedForecasts} · 기억 \"{(s1 != null && s1.Memory.Marks.Count > 0 ? s1.Memory.Marks[^1].Text : "")}\"");

            // ── 2) 수술 중 정전 → 무영등 비상 배터리 · 컴퓨터가 수술실 회로를 몰아준다 ──
            {
                var wp = DayOne(seed, "Hanbit");
                var b = Patient(wp);
                Hurt(wp, b, 0.34f, "작업 중 넘어짐");
                Until(wp, () => wp.Surgery.Cases.Any(x => x.Patient == b.Id && x.State == CaseState.Operating), 12f);
                var kb = wp.Surgery.Cases.FirstOrDefault(x => x.Patient == b.Id && x.State == CaseState.Operating);
                var room = kb != null ? wp.Surgery.TableOf(kb)!.Room : med;
                var lamp = room.Furniture.First(f => f.Type == FurnitureType.SurgicalLamp);
                bool held = lamp.Machine != null && wp.Surgery.Holds(lamp.Machine) && PowerTriage.Rank(wp, lamp.Machine) >= 11;
                room.PowerCut = true;
                Run(wp, SimTime.Minutes(3));
                float light = kb != null ? wp.Surgery.Light(kb) : -1f;
                bool told = wp.Log.Entries.Any(e => e.Text.Contains("비상 배터리")) && wp.Automation.Book.Acts.Any(x => x.Act.Contains("수술실 회로"));
                room.PowerCut = false;
                Run(wp, SimTime.Minutes(3));
                Until(wp, () => Done(wp, b) != null, 6f);
                var kd = Done(wp, b);
                Check("수술 중 정전 — 무영등은 비상 배터리로 · 주컴퓨터가 수술실 회로를 맨 앞에 · 전기가 돌아와 마친다",
                    kb != null && held && light == 1f && told && wp.Surgery.PowerRouted > 0 && kd != null,
                    $"몰아주기 {held} · 빛 {light} (2 무영등 · 1 배터리) · 알림 {told} · 되살림 {wp.Surgery.PowerRouted} · 결과 {kd?.Outcome} ({string.Join(" · ", kd?.Notes ?? new())})");
            }

            // ── 3) 마취제 없음 → 미룬다 → 컴퓨터가 만들게 한다 → 생기면 다시 ──
            {
                var wa = DayOne(seed, "Hanbit");
                Strip(wa, ItemKind.Anesthetic);
                var c = Patient(wa);
                Hurt(wa, c, 0.34f, "작업 중 넘어짐");
                Until(wa, () => wa.Surgery.Cases.Any(x => x.Patient == c.Id && x.State == CaseState.Deferred), 12f);
                var kc = wa.Surgery.Cases.FirstOrDefault(x => x.Patient == c.Id);
                Run(wa, SimTime.Hours(2));
                int want = wa.Pharmacy.Want(ItemKind.Anesthetic);
                Check("마취제 없음 → 미룬다 (위중하지 않다) · 주컴퓨터가 마취제를 더 만들라고 한다",
                    kc != null && kc.DeferWhy.StartsWith("마취제") && wa.Surgery.Deferrals > 0 && want > 3 && wa.Automation.Book.Acts.Any(x => x.Key == "rx:Anesthetic" || x.Key.StartsWith("defer:")),
                    $"{c.Name}: {kc?.State} \"{kc?.DeferWhy}\" · 미룸 {wa.Surgery.Deferrals} · 마취제 목표 {want}");
                Cab(wa)!.Storage!.Add(ItemKind.Anesthetic, 2);
                Until(wa, () => Done(wa, c) != null, 12f);
                var kc2 = Done(wa, c);
                Check("마취제가 생기면 — 미룬 수술을 다시 잡는다", kc2 != null && kc2.Anesthesia, $"{kc2?.Kind} {kc2?.Outcome}");
            }

            // ── 4) 출혈 위중 → 맞는 피 수혈 · 냉장고가 비면 맞는 사람이 나선다 (헌혈) ──
            {
                var wb = DayOne(seed, "Hanbit");
                var d = Patient(wb);
                wb.Casualty.Inflict(d, TraumaKind.Bleed, 0.3f, "시험 파편");
                d.Vitals.Health = 0.32f;
                int t0 = wb.Blood.Transfusions;
                Until(wb, () => wb.Blood.Transfusions > t0, 3f);
                Check("출혈 위중 → 수혈 — 냉장고의 맞는 피 (O형 Rh-는 누구에게나)", wb.Blood.Transfusions > t0 && wb.Log.Entries.Any(e => e.Text.Contains($"{d.Name}에게") && e.Text.Contains("피를 넣었다")),
                    $"{d.Name}({wb.Blood.TypeOf(d).Name}) 수혈 {wb.Blood.Transfusions - t0} · 체력 {d.Vitals.Health * 100:0}% · 남은 팩 {wb.Blood.Count()}");
                wb.Blood.Packs.Clear();
                Strip(wb, ItemKind.BloodSubstitute);
                var e = Patient(wb, 1);
                wb.Casualty.Inflict(e, TraumaKind.Bleed, 0.3f, "시험 파편");
                e.Vitals.Health = 0.3f;
                Until(wb, () => wb.Blood.Fresh > 0, 4f);
                var giver = wb.Crew.FirstOrDefault(x => x.Memory.Marks.Any(m => m.Text.Contains($"{e.Name}에게 내 피")));
                Check("맞는 피가 없으면 — 피가 맞는 사람이 나서 바로 준다 (관계 · 기억)",
                    wb.Blood.Fresh > 0 && giver != null && wb.Blood.Compatible(giver, e) && e.Memory.Marks.Any(m => m.Text.Contains("피로 살았다")) && e.AffinityTo(giver) > 0.05f,
                    $"{e.Name}({wb.Blood.TypeOf(e).Name}) ← {giver?.Name}({(giver != null ? wb.Blood.TypeOf(giver).Name : "-")}) · 바로 {wb.Blood.Fresh} · 부름 {wb.Blood.Calls}");
                // 맞지 않는 피를 무릅쓰면 위험하다
                var odd = Able(wb).FirstOrDefault(x => wb.Blood.TypeOf(x) is { Group: not BloodGroup.AB } || wb.Blood.TypeOf(x).Neg);
                wb.Blood.Packs.Clear();
                wb.Blood.Store(new BloodType(BloodGroup.AB, false), -1, "시험");
                string r = odd != null ? wb.Blood.Transfuse(odd, null, desperate: true) : "";
                Check("맞는 피가 없으면 위험 — 맞지 않는 피는 거부 반응이 날 수 있다", odd != null && wb.Blood.Mismatched == 1 && r.StartsWith("맞지 않는"), $"{odd?.Name}({(odd != null ? wb.Blood.TypeOf(odd).Name : "-")}) ← AB형: \"{r}\" · 반응 {wb.Blood.Reactions}");
            }

            // ── 5) 항생제 남용 → 내성 → 주컴퓨터가 아끼자고 → 의무관이 바꾼다 ──
            {
                var wr = DayOne(seed, "Hanbit");
                Cab(wr)!.Storage!.Add(ItemKind.Antibiotic, 30);
                var doc = Able(wr).OrderBy(x => x.SkillLevel(Skill.Medicine)).First();
                var pt = Able(wr).First(x => x != doc);
                int tries = 0;
                while (wr.Pharmacy.Misuse < 3 && tries++ < 60) wr.Pharmacy.Treat(pt, doc, "cold");
                float res = wr.Pharmacy.Resistance;
                int failed0 = wr.Pharmacy.Failed;
                for (int i = 0; i < 12; i++) wr.Pharmacy.Treat(pt, doc, "pneumonia");
                Run(wr, SimTime.Hours(2) + 30);
                int mis = wr.Pharmacy.Misuse;
                for (int i = 0; i < 20; i++) wr.Pharmacy.Treat(pt, doc, "cold");
                Check("항생제 남용 → 내성 — 감기에 쓴 항생제가 쌓여 폐렴에 안 듣는다", res > 0.15f && wr.Pharmacy.Failed > failed0,
                    $"남용 {wr.Pharmacy.Misuse}번 · 내성 {res * 100:0}% → {wr.Pharmacy.Resistance * 100:0}% · 안 들음 {wr.Pharmacy.Failed}");
                Check("주컴퓨터 — 남용을 알아채 아끼자고 한다 → 의무관이 더는 감기에 쓰지 않는다", wr.Pharmacy.Careful && wr.Automation.Book.Acts.Any(x => x.Key == "rxmisuse") && wr.Pharmacy.Misuse == mis,
                    $"아낌 {wr.Pharmacy.Careful} · 그 뒤 남용 {wr.Pharmacy.Misuse - mis}");
                // 진통제 의존: 자주 먹으면 → 몰래 꺼내 먹는다 → 컴퓨터가 장부와 다른 재고를 알아챈다
                Cab(wr)!.Storage!.Add(ItemKind.Painkiller, 20);
                var pk = Able(wr).Where(x => x.Role != CrewRole.Medic).OrderBy(x => x.Traits.Calm).First();
                for (int i = 0; i < 9; i++) { wr.Pharmacy.LastDose.Remove(pk.Id); wr.Pharmacy.Painkill(pk, null, "시험"); }
                Until(wr, () => wr.Pharmacy.SneakSeen > 0, 72f);
                Check("진통제 의존 — 끊기 어렵다 · 몰래 꺼내 먹는다 · 컴퓨터가 장부보다 빨리 주는 걸 알아챈다",
                    wr.Ailments.Has(pk, "pkdep") && wr.Pharmacy.Sneaked > 0 && wr.Pharmacy.SneakSeen > 0,
                    $"{pk.Name} 의존 {wr.Pharmacy.Dependence.GetValueOrDefault(pk.Id) * 100:0}% · 몰래 {wr.Pharmacy.Sneaked} · 알아챔 {wr.Pharmacy.SneakSeen}");
            }

            // ── 6) 병상 부족 → 휴게실 · 복도 간이침대 · 간병 순번 ──
            {
                var wd = DayOne(seed, "Hanbit");
                int beds = wd.Ship.FurnitureOf(FurnitureType.MedBed).Count();
                var hurt = Able(wd).Where(x => x.Role != CrewRole.Medic).Take(beds + 2).ToList();
                foreach (var x in hurt) { NeedsSystem.AddInjury(x.Vitals, 0.75f, "식중독"); x.Vitals.Health = 0.4f; }
                Until(wd, () => wd.Recovery.CotsSpread > 0 && wd.Crew.Any(x => x.Job?.Activity is WardRestActivity), 6f);
                var cot = wd.Ship.Furniture.FirstOrDefault(f => wd.Recovery.IsWardCot(f));
                Check("병상 부족 — 치료 침대가 모자라 휴게실 · 복도에 간이침대를 펴고 눕는다 · 컴퓨터가 알린다",
                    cot != null && cot.Room.Type is RoomType.Lounge or RoomType.Corridor && wd.Crew.Any(x => x.Job?.Activity is WardRestActivity) && wd.Automation.Book.Acts.Any(x => x.Key == "wardbeds"),
                    $"치료 침대 {beds} · 누운 사람 {hurt.Count} · 간이침대 {wd.Recovery.CotsSpread} ({cot?.Room.Name} {cot?.Room.Type}) · 간이침대에 누움 {wd.Crew.Count(x => x.Job?.Activity is WardRestActivity)} · 알림 {wd.Automation.Book.Acts.Any(x => x.Key == "wardbeds")} · "
                    + string.Join(",", hurt.Select(x => $"{x.Name}:{x.Job?.Activity?.Id}:{InjuryGradeSystem.Name(wd.Grades.Now(x))}")));
                Until(wd, () => wd.Recovery.Rounds > 0, 10f);
                Check("간병 순번 — 돌아가며 누운 사람을 들여다본다", wd.Recovery.Rounds > 0 && wd.Recovery.Carer >= 0, $"들여다봄 {wd.Recovery.Rounds} · 순번 {wd.Crew.FirstOrDefault(x => x.Id == wd.Recovery.Carer)?.Name}");
            }

            // ── 7) 후유증 → 재활 → 풀린다 · 자존감 ──
            {
                var wh = DayOne(seed, "Hanbit");
                var f = Patient(wh);
                wh.Recovery.Sequela(f, BodyPart.LeftLeg, "시험 골절 뒤");
                Run(wh, SimTime.Hours(6));
                float walk = f.Fx.WalkMul, est0 = wh.Recovery.EsteemOf(f);
                var limp = f.Ailments.FirstOrDefault(x => x.Id == "limp");
                Until(wh, () => wh.Recovery.RehabGains > 0, 40f);
                Check("후유증 — 다리를 전다 (걸음이 느리다) · 자존감이 꺾인다 · 재활 운동이 푼다",
                    limp != null && walk < 0.95f && est0 < 0.7f && wh.Recovery.RehabGains > 0 && limp.Healed > 0f,
                    $"{f.Name} 걸음 ×{walk:0.00} · 자존감 {est0:0.00} → {wh.Recovery.EsteemOf(f):0.00} · 재활 {f.Stats.RehabSessions}번 · 나음 {limp?.Healed:0.0}일");
            }

            // ── 8) 배 속 출혈 → 침대로는 못 멎는다 → 개복 · 주컴퓨터 생체 신호 · 점화를 미루자고 ──
            {
                var wi = DayOne(seed, "Hanbit");
                var g = Patient(wi);
                NeedsSystem.AddInjury(g.Vitals, 0.3f, "구조물 충돌");
                wi.Casualty.Inflict(g, TraumaKind.Bleed, 0.12f, "구조물 충돌 · 배 속");
                Run(wi, SimTime.Minutes(1));
                var tr = wi.Casualty.Of(g);
                if (tr != null && !wi.Surgery.Internal(tr)) wi.Surgery.MarkInternalFor(tr);
                g.Vitals.TreatedTick = wi.Tick;
                g.Vitals.Health = 0.5f;
                Run(wi, SimTime.Minutes(3));
                bool still = wi.Casualty.Of(g) != null;
                // 항로를 바꿀 때가 되었다 (공기 탱크가 줄었다 · 잔해 지대)
                wi.Propulsion.Drift(ZoneKind.Debris);
                wi.Air.Reserve = wi.Air.ReserveCapacity * 0.5f;
                Until(wi, () => wi.Surgery.BurnProposals > 0 || Done(wi, g) != null, 12f);
                var prop = wi.Automation.Asks.Latest("surgburn");
                if (prop != null && prop.State == ProposalState.Pending) wi.Automation.Asks.Decide(prop, true, "함장", "수술이 먼저");
                int tr0 = wi.Propulsion.Transfers;
                bool heldNow = wi.Surgery.BurnHeld;
                Until(wi, () => Done(wi, g) != null, 8f);
                var kg = Done(wi, g);
                Check("배 속 출혈 — 누르고 치료해도 멎지 않는다 → 개복", still && kg != null && kg.Kind == SurgeryKind.Laparotomy,
                    $"{g.Name}: 멎지 않음 {still} · {kg?.Kind} {kg?.Outcome} · 가망 {kg?.Chance * 100:0}%");
                Check("주컴퓨터 — 수술 중 항로 점화를 미루자고 함장에게 제안 → 받으면 점화가 미뤄진다",
                    prop != null && heldNow && wi.Surgery.BurnHolds > 0 && wi.Log.Entries.Any(x => x.Text.Contains("항로 점화를 미뤘다")),
                    $"제안 {wi.Surgery.BurnProposals} \"{prop?.Title}\" · 미룸 {heldNow} · 그사이 항로 변경 {wi.Propulsion.Transfers - tr0}");
                Check("주컴퓨터 — 생체 신호를 본다 (\"혈압이 떨어집니다\") · 출혈량을 예측한다", wi.Surgery.BleedForecasts > 0 && wi.Surgery.VitalWarnings > 0 && wi.Log.Entries.Any(x => x.Text.Contains("혈압이 떨어집니다")),
                    $"예측 {wi.Surgery.BleedForecasts} · 혈압 경고 {wi.Surgery.VitalWarnings} · 수혈 {kg?.Bloods}");
            }

            // ── 9) 집도의의 기억: 잃은 사람이 있으면 수술대 앞에 서지 못한다 (다른 사람이 있으면 물러선다) ──
            {
                var wg = DayOne(seed, "Hanbit");
                var docs = Able(wg).Where(x => x.Role == CrewRole.Medic || x.SkillLevel(Skill.Medicine) >= 0.3f).OrderByDescending(x => x.SkillLevel(Skill.Medicine) + (x.Role == CrewRole.Medic ? 0.25f : 0f)).ToList();
                if (docs.Count >= 2)
                {
                    wg.Surgery.Guilt[docs[0].Id] = 0.9f;
                    var p = Patient(wg);
                    Hurt(wg, p, 0.34f, "작업 중 넘어짐");
                    Until(wg, () => wg.Surgery.Cases.Any(x => x.Patient == p.Id && x.Surgeon >= 0), 3f);
                    var kp = wg.Surgery.Cases.FirstOrDefault(x => x.Patient == p.Id);
                    Check("집도의의 기억 — 죄책감이 큰 사람은 물러서고 다른 사람이 집도한다", kp != null && kp.Surgeon >= 0 && kp.Surgeon != docs[0].Id && wg.Surgery.StepBacks > 0,
                        $"{docs[0].Name} 죄책감 0.9 → 집도 {wg.Crew.FirstOrDefault(x => x.Id == kp?.Surgeon)?.Name} · 물러섬 {wg.Surgery.StepBacks}");
                }
                else Check("집도의의 기억 — (의료를 아는 사람이 하나뿐인 배: 건너뜀)", true);
            }

            // ── 10) 수술 순서 · 냉장고 · 약 기한 · 약 만들기 ──
            {
                var wo = DayOne(seed, "Hanbit");
                var p1 = Patient(wo); var p2 = Patient(wo, 1);
                NeedsSystem.AddInjury(p1.Vitals, 0.34f, "작업 중 넘어짐");
                NeedsSystem.AddInjury(p2.Vitals, 0.42f, "작업 중 넘어짐");
                Run(wo, SimTime.Minutes(1));
                p1.Vitals.TreatedTick = wo.Tick; p2.Vitals.TreatedTick = wo.Tick;
                Until(wo, () => wo.Surgery.LastOrder != "", 1f);
                Check("주컴퓨터 — 수술을 기다리는 사람이 둘이면 순서를 권한다", wo.Surgery.LastOrder.Contains(p1.Name) && wo.Surgery.LastOrder.Contains(p2.Name), $"\"{wo.Surgery.LastOrder}\"");
                // 냉장고 전기가 끊기면 피가 상한다
                var fr = wo.Ship.FurnitureOf(FurnitureType.BloodFridge).First();
                int sp0 = wo.Blood.Spoiled, packs0 = wo.Blood.Count();
                fr.Room.PowerCut = true;
                Run(wo, SimTime.Hours(6));
                fr.Room.PowerCut = false;
                Check("혈액 냉장고 — 전기가 끊겨 미지근하면 피가 상한다 · 컴퓨터가 상하기 전에 알린다", packs0 > 0 && wo.Blood.Spoiled > sp0 && wo.Automation.Book.Acts.Any(x => x.Key == "bloodcold"),
                    $"팩 {packs0} → 상함 {wo.Blood.Spoiled - sp0}");
                // 약 기한이 지나면 버리고 → 컴퓨터가 예측해 → 재배실 채소로 약초 · 약초로 진통제를 만든다
                Run(wo, SimTime.Hours(2));
                wo.Pharmacy.Age(ItemKind.Painkiller, SimTime.TicksPerDay * 61);
                Until(wo, () => wo.Pharmacy.ExpiredCount > 0, 3f);
                int left = wo.Pharmacy.Stock(ItemKind.Painkiller);
                for (int i = 0; i < 3; i++) wo.Pharmacy.Used(ItemKind.Painkiller);
                Until(wo, () => wo.Pharmacy.Stock(ItemKind.Painkiller) > 0, 36f);
                Check("약 — 기한이 지나면 버린다 · 컴퓨터가 남은 날을 예측해 더 만들게 한다 · 재배실 채소 → 약초 → 진통제",
                    wo.Pharmacy.ExpiredCount > 0 && left == 0 && wo.Pharmacy.Forecasts > 0 && wo.Pharmacy.Stock(ItemKind.Painkiller) > 0,
                    $"버림 {wo.Pharmacy.ExpiredCount} · 남음 {left} → {wo.Pharmacy.Stock(ItemKind.Painkiller)} · 예측 {wo.Pharmacy.Forecasts} · 목표 {wo.Pharmacy.Want(ItemKind.Painkiller)} · 약초 {wo.Pharmacy.Stock(ItemKind.MedHerb)}");
            }

            // ── 11) 결정론 ──
            uint H() { var x = World.CreateDefault(seed, 0, "Hanbit"); Run(x, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(x); }
            uint h1 = H(), h2 = H();
            Check("결정론 — 같은 시드면 같은 배 (수술 · 피 · 약 · 회복 포함)", h1 == h2, $"{h1:x8} / {h2:x8}");
        }
        catch (Exception ex)
        {
            Check("예외 없음", false, ex.ToString());
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 의료 1차 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
