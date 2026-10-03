using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// 의료 3차 — 주컴퓨터와 로봇도 의료를 한다 (수술 로봇 팔 · 동의와 신뢰 · 의료 로봇 · 원격 의료)
public static partial class Program
{
    private static int RunMedBotTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"의료 3차 점검 · 시드 {seed}\n");
        try
        {
            static List<CrewMember> Able(World w) => w.Crew.Where(c => !c.Dead && !c.Down && !c.IsChild && !c.Away && !c.Outside).OrderBy(c => c.Id).ToList();
            static CrewMember Patient(World w, int skip = 0) => Able(w).Where(c => c.Role != CrewRole.Medic).Skip(skip).First();
            static void Hurt(World w, CrewMember c, float amt, string cause)
            {
                NeedsSystem.AddInjury(c.Vitals, amt, cause);
                Run(w, SimTime.Minutes(1));
                c.Vitals.TreatedTick = w.Tick;
                Run(w, SimTime.Minutes(2));
            }
            static SurgeryCase? Done(World w, CrewMember c, SurgeryKind? k = null) => w.Surgery.Done.LastOrDefault(x => x.Patient == c.Id && (k == null || x.Kind == k) && x.Outcome != "칼을 대지 않아도 되게 됐다");
            static void Until(World w, Func<bool> ok, float hours) { long end = w.Tick + SimTime.Hours(hours); while (w.Tick < end && !ok()) Run(w, SimTime.Minutes(1)); }
            static Room Med(World w) => w.Ship.RoomsOf(RoomType.Medbay).OrderByDescending(r => r.Cells.Count).First();
            // 수술대 곁에 수술 팔을 세운다 (개조로 들인 셈)
            static Furniture FitArm(World w)
            {
                Run(w, World.SystemInterval * 2);
                var room = Med(w);
                var table = room.Furniture.First(f => f.Type == FurnitureType.OperatingTable);
                var tc = table.Cells[0];
                var at = Adaptation.CotCells(w, room).Where(c => c != tc && Adaptation.SafeToBlock(w, room, c))
                    .OrderBy(c => Math.Max(Math.Abs(c.X - tc.X), Math.Abs(c.Y - tc.Y))).ThenBy(c => c.Y).ThenBy(c => c.X).First();
                var f = w.Ship.AddFurniture(FurnitureType.SurgicalArm, at);
                if (f.Machine != null) { f.Machine.Wear = 0f; f.Machine.Condition = 1f; }
                w.Paths.Invalidate();
                return f;
            }
            // 의무관이 없다 (원정 · 다른 배) · 의료를 아는 사람도 없다
            static void NoMedic(World w)
            {
                foreach (var c in w.Crew)
                {
                    if (c.Role == CrewRole.Medic) c.Away = true;
                    c.SkillLevels[(int)Skill.Medicine] = MathF.Min(c.SkillLevels[(int)Skill.Medicine], 0.12f);
                }
            }
            static void Alone(World w, params CrewMember[] keep) { foreach (var c in w.Crew) if (!keep.Contains(c) && !c.Dead) c.Away = true; }
            static Robot Bot(World w, RobotKind k)
            {
                bool ok = RobotsV15.Install(w, RobotsV15.Code(k), null, out var text);
                var r = w.Robots.Robots.Last(x => x.Kind == k);
                return r;
            }

            // ── 1) 의무관이 없다 → 주컴퓨터가 수술 팔로 골절을 고정한다 (V 지휘 · 수술 로봇 기술) · 성공하면 믿음이 오른다 ──
            var w = DayOne(seed, "Hanbit");
            var arm = FitArm(w);
            NoMedic(w);
            float hand0 = w.SurgArm.Hand(arm);
            w.Eras.Known.Add("surgbot");
            float hand1 = w.SurgArm.Hand(arm);
            var a = Patient(w);
            float trust0 = w.Automation.Trusts.Of(a);
            Hurt(w, a, 0.34f, "작업 중 넘어짐");
            Until(w, () => Done(w, a, SurgeryKind.Fracture) != null, 14f);
            var k1 = Done(w, a, SurgeryKind.Fracture);
            Check("의무관 없음 → 컴퓨터가 수술 팔로 골절 수술 (동의 · 마취 · 멸균 · 집도)",
                k1 != null && k1.Surgeon < 0 && k1.Notes.Contains("수술 팔 집도") && w.SurgArm.LeadOps == 1 && w.SurgArm.Consents >= 1 && k1.Anesthesia && hand1 > hand0,
                k1 == null ? $"열린 수술 {string.Join(",", w.Surgery.Cases.Select(x => $"{x.Kind}:{x.State}:{x.Surgeon}:{w.SurgArm.Leads(x)}"))} · 동의 {w.SurgArm.Consents} 거절 {w.SurgArm.Refusals} 보조만 {w.SurgArm.AssistOnly} · 레벨 {w.Automation.Level}"
                    : $"{a.Name} {SurgerySystem.KindName(k1.Kind)} — {k1.Outcome} · 가망 {k1.Chance * 100:0}% · 팔 손 {hand0 * 100:0}% → 기술 {hand1 * 100:0}% · {string.Join(" · ", k1.Notes)}");
            float trust1 = w.Automation.Trusts.Of(a);
            Check("성공 → 환자의 컴퓨터 믿음이 오른다 · 기억 · 팔이 겪은 수술이 쌓인다",
                k1 != null && k1.Success && trust1 > trust0 && a.Memory.Marks.Any(m => m.Text.Contains("수술 팔")) && w.SurgArm.Ops >= 1 && w.SurgArm.Successes == 1,
                $"믿음 {trust0 * 100:0}% → {trust1 * 100:0}% · 성공 {k1?.Success} · 겪은 수술 {w.SurgArm.Ops} · 기억 \"{a.Memory.Marks.Select(m => m.Text).LastOrDefault(t => t.Contains("수술 팔"))}\"");

            // ── 2) 등급 III (조정) — 집도는 못 하고 곁에서 거들기만 (수술 · 이식 가망에 같은 함수로) ──
            {
                var w2 = DayOne(seed, "Hanbit");
                var arm2 = FitArm(w2);
                NoMedic(w2);
                w2.Automation.LevelCap = 3;
                var b = Patient(w2);
                Hurt(w2, b, 0.34f, "작업 중 넘어짐");
                Run(w2, SimTime.Hours(1));
                bool noLead = w2.SurgArm.LeadOps == 0 && !w2.Surgery.Cases.Any(k => w2.SurgArm.Leads(k)) && w2.SurgArm.AssistOnly > 0;
                var helper = Patient(w2, 1);
                float f3 = w2.SurgArm.Factor(arm2.Room, helper);
                var table = arm2.Room.Furniture.First(f => f.Type == FurnitureType.OperatingTable);
                float withArm = TransplantSystem.Chance(w2, helper, b, null, table);
                arm2.Stowed = true;
                float noArm = TransplantSystem.Chance(w2, helper, b, null, table);
                arm2.Stowed = false;
                Check("등급 III — 팔은 집도하지 않고 곁에서 거들기만 (수술 · 이식 가망이 오른다)", noLead && f3 > 0f && withArm > noArm,
                    $"집도 {w2.SurgArm.LeadOps} · 보조만 {w2.SurgArm.AssistOnly} · 거드는 몫 +{f3 * 100:0.#}% · 이식 가망 {noArm * 100:0}% → {withArm * 100:0}% · {w2.Automation.Book.Acts.LastOrDefault(x => x.Act.Contains("팔로 집도하지"))?.Judge}");
            }

            // ── 3) 수술 중 정전 → 팔이 멈추고 곁의 사람이 이어받아 마친다 ──
            {
                var w3 = DayOne(seed, "Hanbit");
                var arm3 = FitArm(w3);
                NoMedic(w3);
                w3.Eras.Known.Add("surgbot");
                var c3 = Patient(w3);
                Hurt(w3, c3, 0.34f, "작업 중 넘어짐");
                Until(w3, () => w3.Surgery.Cases.Any(k => k.Patient == c3.Id && k.State == CaseState.Operating && w3.SurgArm.Leads(k)), 12f);
                var k3 = w3.Surgery.Cases.FirstOrDefault(k => k.Patient == c3.Id && w3.SurgArm.Leads(k));
                arm3.Room.PowerCut = true;
                Run(w3, SimTime.Minutes(2));
                bool stalled = k3 != null && w3.SurgArm.Stalls > 0 && !w3.SurgArm.Leads(k3) && k3.Surgeon >= 0;
                string taker = k3 != null && k3.Surgeon >= 0 ? w3.Crew.First(x => x.Id == k3.Surgeon).Name : "-";
                Run(w3, SimTime.Minutes(4));
                arm3.Room.PowerCut = false;
                Until(w3, () => Done(w3, c3) != null, 8f);
                var kd = Done(w3, c3);
                Check("수술 중 정전 — 팔이 멈추고 곁의 사람이 이어받아 마친다", stalled && kd != null && kd.Surgeon >= 0 && w3.SurgArm.Takeovers > 0 && kd.Notes.Any(n => n.Contains("팔이 멈췄다")),
                    $"멈춤 {w3.SurgArm.Stalls} · 이어받은 사람 {taker} · 결과 {kd?.Outcome} ({string.Join(" · ", kd?.Notes ?? new())})");
            }

            // ── 4) 환자가 "기계에 몸을 맡길 순 없다" → 손이 덜 익은 사람이라도 사람이 한다 ──
            {
                var w4 = DayOne(seed, "Hanbit");
                FitArm(w4);
                NoMedic(w4);
                var d4 = Patient(w4);
                w4.Automation.Trusts.Change(d4, -0.6f, "시험: 컴퓨터를 믿지 않는다", quiet: true);
                Hurt(w4, d4, 0.34f, "작업 중 넘어짐");
                Until(w4, () => Done(w4, d4) != null, 14f);
                var k4 = Done(w4, d4);
                Check("환자가 수술 팔을 마다함 → 사람이 집도 (기억에 남는다)", w4.SurgArm.Refusals > 0 && k4 != null && k4.Surgeon >= 0 && !k4.Notes.Contains("수술 팔 집도") && d4.Memory.Marks.Any(m => m.Text.Contains("마다했다")),
                    $"거절 {w4.SurgArm.Refusals} · 집도 {(k4 != null && k4.Surgeon >= 0 ? w4.Crew.First(x => x.Id == k4.Surgeon).Name : "-")} · 결과 {k4?.Outcome} · \"{d4.Memory.Marks.Select(m => m.Text).LastOrDefault(t => t.Contains("마다"))}\"");
            }

            // ── 5) 팔이 집도하다 숨지면 — 믿음이 꺾이고 회의 안건(컴퓨터 제안 → 모두 묻는다)으로 ──
            {
                var w5 = DayOne(seed, "Hanbit");
                var arm5 = FitArm(w5);
                var e5 = Patient(w5);
                var near = Patient(w5, 1);
                near.Position = arm5.Center; near.Room = arm5.Room;
                float t0 = w5.Automation.Trusts.Of(near), ship0 = w5.Command.ComputerTrust;
                var fake = new SurgeryCase { Id = 999, Patient = e5.Id, Kind = SurgeryKind.Laparotomy, Part = BodyPart.Chest, Table = arm5.Room.Furniture.First(f => f.Type == FurnitureType.OperatingTable).Id };
                w5.SurgArm.Finished(fake, e5, null, false, true);
                Check("팔이 집도하다 숨짐 → 곁의 사람 · 배 전체 믿음이 꺾이고 회의 안건 · 연대기", w5.Meetings.Reviews.Any(r => r.id == "computerask" && r.to == 2) && w5.Automation.Trusts.Of(near) < t0 && w5.Command.ComputerTrust < ship0
                    && w5.History.Events.Any(h => h.Text.Contains("수술 팔")),
                    $"믿음 {t0 * 100:0}% → {w5.Automation.Trusts.Of(near) * 100:0}% · 배 {ship0 * 100:0}% → {w5.Command.ComputerTrust * 100:0}% · 안건 {w5.Meetings.Reviews.Count}");
            }

            // ── 6) 들것 로봇 — 쓰러진 사람을 치료 침대로 (사람이 없을 때) · 로봇이 멎으면 내려놓고 사람이 이어 옮긴다 ──
            {
                var w6 = DayOne(seed, "Hanbit");
                var st = Bot(w6, RobotKind.Stretcher);
                var v6 = Able(w6).Where(c => c.Room != null && c.Room.Type != RoomType.Medbay).First();
                Alone(w6, v6);
                v6.Vitals.Health = 0.1f;
                Run(w6, SimTime.Minutes(2));
                bool down = v6.Down;
                Until(w6, () => w6.MedBots.Carried > 0, 3f);
                Check("들것 로봇 — 쓰러진 사람을 들것에 실어 치료 침대로 옮긴다", down && w6.MedBots.Carried > 0 && (v6.CareBed != null || v6.LaidSafe) && v6.Memory.Marks.Any(m => m.Text.Contains(st.Name)),
                    $"{v6.Name} 쓰러짐 {down} · 옮김 {w6.MedBots.Carried} · 침대 {v6.CareBed?.Name ?? (v6.LaidSafe ? "바닥" : "-")} · {v6.Room?.Name} · 로봇 {st.State}/{st.Doing}");

                var w6b = DayOne(seed, "Hanbit");
                var st2 = Bot(w6b, RobotKind.Stretcher);
                var v6b = Able(w6b).Where(c => c.Room != null && c.Room.Type != RoomType.Medbay).First();
                var back = Able(w6b).First(c => c != v6b && c.Role != CrewRole.Medic);
                Alone(w6b, v6b);
                v6b.Vitals.Health = 0.1f;
                Until(w6b, () => w6b.MedBots.Carrying(st2) != null, 3f);
                bool carrying = w6b.MedBots.Carrying(st2) != null;
                Run(w6b, SimTime.Minutes(1));
                w6b.Robots.ForceFault(st2, RobotFault.Drive);
                Run(w6b, SimTime.Minutes(1));
                bool dropped = w6b.MedBots.Dropped > 0 && !w6b.MedBots.CarriedByBot(v6b) && v6b.Down;
                back.Away = false;
                Until(w6b, () => v6b.CareBed != null || v6b.LaidSafe, 3f);
                Check("들것 로봇이 고장 — 그 자리에 내려놓고 사람이 이어 옮긴다", carrying && dropped && (v6b.CareBed != null || v6b.LaidSafe),
                    $"싣고 감 {carrying} · 내려놓음 {w6b.MedBots.Dropped} · 사람 {back.Name} → {v6b.CareBed?.Name ?? (v6b.LaidSafe ? "바닥" : "-")}");
            }

            // ── 7) 간호 로봇 — 피 나는 사람 곁에서 눌러 멎게 한다 ──
            {
                var w7 = DayOne(seed, "Hanbit");
                var nb = Bot(w7, RobotKind.Nurse);
                var v7 = Able(w7).Where(c => c.Room != null && c.Room.Type != RoomType.Medbay).First();
                Alone(w7, v7);
                v7.Vitals.Health = 0.11f;
                Run(w7, SimTime.Minutes(1));
                w7.Casualty.Inflict(v7, TraumaKind.Bleed, 0.12f, "날카로운 모서리에 베임");
                var t7 = w7.Casualty.Of(v7);
                float r0 = t7?.Rate ?? 0f;
                Until(w7, () => w7.MedBots.Pressed > 0, 3f);
                Run(w7, SimTime.Minutes(1));
                Check("간호 로봇 — 피 나는 사람 곁에 와서 지혈 거즈로 눌러 멎게 한다", w7.MedBots.Pressed > 0 && t7 != null && (t7.Closed || t7.Rate < r0 * 0.6f),
                    $"누름 {w7.MedBots.Pressed} · 멎음 {w7.MedBots.Stopped} · {r0:0.###} → {(t7?.Closed == true ? "멎었다" : $"{t7?.Rate:0.###}")} ({t7?.Outcome})");
            }

            // ── 8) 간호 로봇 — 혈액 냉장고에서 피를 가져와 (주컴퓨터가 침대 투여 펌프로) 넣는다 · 원격 진통 ──
            {
                var w8 = DayOne(seed, "Hanbit");
                var nb = Bot(w8, RobotKind.Nurse);
                nb.Disabled = true;
                var v8 = Able(w8).Where(c => c.Room != null && c.Room.Type != RoomType.Medbay).First();
                Alone(w8, v8);
                v8.Vitals.Health = 0.5f;
                w8.Casualty.Inflict(v8, TraumaKind.Bleed, 1.2f, "파편에 찢김");
                Until(w8, () => w8.Blood.Lost(v8) >= 0.32f, 2f);
                if (w8.Casualty.Of(v8) is Trauma t8) w8.Casualty.StopBy(v8, t8, "시험: 누군가 묶었다");
                v8.Vitals.Health = MathF.Min(v8.Vitals.Health, 0.11f);
                v8.Vitals.Injury = MathF.Max(v8.Vitals.Injury, 0.4f);
                Run(w8, SimTime.Minutes(1));
                var bed = w8.Ship.FurnitureOf(FurnitureType.MedBed).OrderBy(f => f.Id).First();
                var spot = bed.UseSpots[0];
                v8.Position = spot.Center; v8.PreviousPosition = spot.Center; v8.Room = w8.Ship.RoomAt(spot);
                bed.ReservedBy = v8; v8.CareBed = bed;
                int tf0 = w8.Blood.Transfusions + w8.Blood.Substitutes;
                nb.Disabled = false;
                Until(w8, () => w8.MedBots.Transfused > 0, 3f);
                Check("간호 로봇 — 혈액 냉장고의 피를 침대까지 날라 (주컴퓨터 투여 펌프로) 넣는다", w8.MedBots.Delivered > 0 && w8.MedBots.Transfused > 0 && w8.Blood.Transfusions + w8.Blood.Substitutes > tf0,
                    $"{v8.Name} 흘린 피 {w8.Blood.Lost(v8) * 100:0}% · 날라 옴 {w8.MedBots.Delivered} · 넣음 {w8.MedBots.Transfused} · {w8.Log.Entries.Select(e => e.Text).LastOrDefault(t => t.Contains("가져온 피"))}");
                Run(w8, SimTime.Hours(1));
                Check("원격 의료 — 데이터선이 닿는 침대: 투여 펌프로 진통제를 조금씩", w8.Telemed.PumpDoses > 0,
                    $"펌프 {w8.Telemed.PumpDoses} · 줄임 {w8.Telemed.Tapered} · 의존 {w8.Pharmacy.Dependence.GetValueOrDefault(v8.Id) * 100:0}%");
            }

            // ── 9) 결정론 ──
            uint H()
            {
                var x = World.CreateDefault(seed, 0, "Hanbit");
                FitArm(x);
                Bot(x, RobotKind.Nurse);
                Bot(x, RobotKind.Stretcher);
                NeedsSystem.AddInjury(Patient(x).Vitals, 0.34f, "운석 파편");
                Run(x, SimTime.TicksPerDay + SimTime.Hours(6));
                return SaveGame.StateHash(x);
            }
            uint h1 = H(), h2 = H();
            Check("결정론 — 같은 시드면 같은 배 (팔 · 로봇 · 원격 의료)", h1 == h2, $"{h1:x8} / {h2:x8}");
        }
        catch (Exception ex)
        {
            Check("예외 없음", false, ex.ToString());
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 의료 3차 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
