using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v17.1 승무원 인형 · 몸의 변화: 머리카락 · 수염이 자라고 서로 잘라 준다 · 체중이 변해 우주복이 안 맞는다 · 알아채고 말한다 · 컴퓨터가 경고한다
public static partial class Program
{
    private static List<CrewMember> Adults(World w) => w.Crew.Where(c => c.CanAct && !c.IsChild && !c.Outside).OrderBy(c => c.Id).ToList();

    /// <summary>다른 사람이 끼어들지 않게: 모두 막 자른 머리 · 이 사람과는 그저 그런 사이.</summary>
    private static void LookCalm(World w, params CrewMember[] keep)
    {
        foreach (var o in w.Crew)
        {
            var l = w.Body2.Of(o);
            if (!keep.Contains(o)) { l.HairCm = l.StyleCm; l.ShaggySince = -1; }
            foreach (var k in keep) if (o != k && !keep.Contains(o)) { o.Affinity[k.Id] = Math.Min(o.AffinityTo(k), 0.1f); }
        }
    }

    private static void Excuse(World w, params CrewMember[] cs)
    {
        foreach (var c in cs) { c.ExcusedUntil = w.Tick + SimTime.Hours(10); c.Needs.Rest = 0.9f; c.Needs.Food = 0.9f; c.Needs.Stress = 0.1f; }
    }

    private static int RunLookTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"승무원 인형 · 몸의 변화 (v17.1) · 시드 {seed}\n");
        bool debug = Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1";

        // ── 1) 그냥 흘러가는 사흘: 머리 · 수염이 자라고, 먹은 것 · 쓴 것대로 체중이 변하고, 사람마다 생김새가 다르다 ──
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            w.Step();
            var start = w.Crew.ToDictionary(c => c.Id, c => (hair: w.Body2.Of(c).HairCm, kg: w.Body2.Of(c).Kg, beard: w.Body2.Of(c).BeardMm));
            Run(w, SimTime.TicksPerDay * 3);
            var alive = w.Crew.Where(c => !c.Dead).ToList();
            var looks = alive.Select(c => w.Body2.Of(c)).ToList();
            int grew = alive.Count(c => w.Body2.Of(c).Style == HairStyle.Bald || w.Body2.Of(c).HairCm > start[c.Id].hair + 0.5f || w.Body2.Of(c).CutAt > 0);
            float meanIntake = looks.Average(l => l.LastIntake), meanBurn = looks.Average(l => l.LastBurn);
            float drift = alive.Average(c => w.Body2.Of(c).Kg - start[c.Id].kg);
            float spread = alive.Max(c => w.Body2.Of(c).Kg - start[c.Id].kg) - alive.Min(c => w.Body2.Of(c).Kg - start[c.Id].kg);
            if (debug)
                foreach (var c in alive)
                {
                    var l = w.Body2.Of(c);
                    Console.WriteLine($"    {c.Name}: {Body2System.StyleName(l.Style)} {l.HairCm:0.0}/{l.StyleCm:0.0}cm 수염 {l.BeardMm:0.0}mm · {l.Kg:0.0}kg(시작 {start[c.Id].kg:0.0}) 먹음 {l.LastIntake:0.00} 씀 {l.LastBurn:0.00} · 식성 {c.Traits.Appetite:0.00} · 이발 솜씨 {l.Barber:0.00}");
                }
            Check("머리카락이 자란다 (사흘)", grew == alive.Count, $"{grew}/{alive.Count}명");
            Check("수염: 기르는 사람은 길고 · 깎는 사람은 아침마다 깎는다", w.Body2.Stats.Shaves > 0 && looks.Any(l => l.KeepsBeard && l.BeardMm > 4f) || looks.All(l => !l.KeepsBeard),
                $"면도 {w.Body2.Stats.Shaves} · 다듬기 {w.Body2.Stats.Trims} · 기르는 사람 {looks.Count(l => l.KeepsBeard)}");
            Check("먹은 것과 쓴 것이 대체로 맞는다 (보통 하루에 크게 찌거나 빠지지 않는다)", MathF.Abs(drift) < 1.2f && MathF.Abs(meanIntake - meanBurn) < 0.5f,
                $"하루 먹음 {meanIntake:0.00} · 씀 {meanBurn:0.00} (허기 단위) · 사흘 평균 {drift:+0.00;-0.00}kg · 사람 사이 폭 {spread:0.00}kg");
            Check("사람마다 체중이 다르게 움직인다 (식성 · 운동 · 일)", spread > 0.3f, $"폭 {spread:0.00}kg");
            int distinct = looks.Select(l => (l.Style, l.HairColor, l.Skin, l.Pattern)).Distinct().Count();
            Check("생김새가 사람마다 다르다 (머리 모양 · 색 · 피부 · 옷 무늬)", distinct >= looks.Count - 1, $"{distinct}/{looks.Count}가지");
            if (debug) Console.WriteLine("    " + w.Body2.Stats.Summary());
        }

        // ── 2) 덥수룩해진 사람의 머리를 친한 사람이 잘라 준다 (솜씨 좋음 → 말끔 · 관계 · 기쁨 · 기억 · 본 사람이 알아채고 말한다) ──
        CrewMember? A = null, B = null;
        {
            var w = BrainDay(seed, "Hanbit");
            var adults = Adults(w);
            A = adults[0]; B = adults[1];
            var C = adults[2];
            var lounge = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Lounge or RoomType.Mess && r.Furniture.Any(f => f.Type == FurnitureType.Seat)).OrderBy(r => r.Id).First();
            LookCalm(w, A, B);
            Excuse(w, A, B, C);
            var la = w.Body2.Of(A); var lb = w.Body2.Of(B);
            if (la.Style == HairStyle.Bald) la.Style = HairStyle.Crop;
            if (la.StyleCm < 2f) la.StyleCm = 3f;
            la.HairCm = BodyLook.ShagAt(la.StyleCm) * 1.3f; la.ShaggySince = w.Tick;
            lb.Barber = 0.88f; B.Habits.Remove(Habit.Hasty); B.Habits.Remove(Habit.Fidgety); A.Habits.Remove(Habit.Fidgety);
            A.Affinity[B.Id] = 0.55f; B.Affinity[A.Id] = 0.6f;
            if (!C.Habits.Contains(Habit.Talker)) C.Habits.Add(Habit.Talker);
            BrainPut(w, A, lounge, 0); BrainPut(w, B, lounge, 1); BrainPut(w, C, lounge, 2);
            w.Body2.Notice(B, A); w.Body2.Notice(C, A); // 덥수룩한 모습을 본다
            float hairBefore = la.HairCm, affBefore = A.AffinityTo(B), skillBefore = lb.Barber, dustBefore = A.Soil.Clothes[(int)SoilKind.Dust];
            var dist = w.Paths.Flood(B.Cell, B.PathProfile);
            var act = Brain.Activities.OfType<HaircutActivity>().First();
            var (score, why) = act.Score(B, w, dist);
            Check("친한 사람이 덥수룩한 머리를 알아본다 (본 모습으로 — 이발 하고 싶어진다)", score > 0.4f && why.Contains(A.Name), $"점수 {score:0.00} · {why}");
            var job = act.Plan(B, w, dist);
            Check("이발 계획 (가서 묻고 · 의자에 앉히고 · 자른다)", job != null, job?.LogText ?? "없음");
            if (job != null) Force(w, B, job);
            bool sawScissors = false, sawSit = false;
            for (int k = 0; k < SimTime.Hours(6) && la.Cuts == 0; k++)
            {
                w.Step();
                if (debug && k % 300 == 0)
                {
                    var ss = w.Body2.SessionOf(B) ?? w.Body2.Sessions.LastOrDefault();
                    Console.WriteLine($"    [{SimTime.Clock(w.Tick)}] {A.Name}: {A.ActivityLabel}/{A.Job?.Current?.GetType().Name} {A.Pose} · {B.Name}: {B.ActivityLabel}/{B.Job?.Current?.GetType().Name} {B.Pose} · 세션 {(ss == null ? "-" : $"{ss.Progress:0.00} 앉음 {ss.Seated} 끝 {ss.Done} 취소 {ss.Canceled}")} · 거리 {(A.Position - B.Position).Length():0.0}");
                }
                if (k % 60 == 0)
                {
                    sawScissors |= Puppet.Of(w, B).Held == HeldThing.Scissors;
                    sawSit |= Puppet.Of(w, A).Pose == PuppetPose.Sit && A.Job?.Activity is HaircutActivity;
                }
            }
            if (debug) Console.WriteLine($"    {A.Name} 머리 {la.HairCm:0.0}cm · 결과 {Body2System.Name(la.LastCut)} · {A.ActivityLabel} / {B.ActivityLabel} · {w.Body2.Stats.Summary()}");
            Check("친한 사람이 잘라 줬다 (손님은 의자에 앉고 · 자르는 사람 손에 가위)", la.Cuts == 1 && la.CutBy == B.Id && sawScissors && sawSit,
                $"{A.Name} ← {B.Name} · {hairBefore:0.0} → {la.HairCm:0.0}cm · 가위 {sawScissors} · 앉음 {sawSit}");
            Check("솜씨 좋은 사람: 말끔 → 관계 · 기쁨 · 고마운 기억 · 솜씨가 는다", la.LastCut == CutResult.Neat && A.AffinityTo(B) > affBefore
                && w.Brain2.Emotions.Get(A, Feeling.Joy) > 0.05f && w.Relations.Of(A, B).Any(m => m.Reason == RelationReason.CutMyHair) && lb.Barber > skillBefore,
                $"{Body2System.Name(la.LastCut)} · 호감 {affBefore:0.00}→{A.AffinityTo(B):0.00} · 기쁨 {w.Brain2.Emotions.Get(A, Feeling.Joy):0.00} · 솜씨 {skillBefore:0.00}→{lb.Barber:0.00}");
            Check("자른 머리카락: 바닥에 남거나 치웠고 · 옷에 붙었다 (빨래)", (w.Body2.Stats.Clips > 0) && A.Soil.Clothes[(int)SoilKind.Dust] > dustBefore,
                $"떨어진 머리 {w.Body2.Stats.Clips} · 치움 {w.Body2.Stats.Sweeps} · 옷 먼지 {dustBefore:0.00}→{A.Soil.Clothes[(int)SoilKind.Dust]:0.00}");
            // 곁의 사람이 바뀐 머리를 알아채고 말한다 → 누가 잘랐는지 듣고 솜씨를 믿게 된다
            int before = w.Body2.Stats.HairComments;
            for (int k = 0; k < 6 && w.Body2.Stats.HairComments == before; k++) w.Body2.Notice(C, A);
            Check("곁의 사람이 바뀐 머리를 알아채고 말한다 (본 모습과 견주어)", w.Body2.Stats.HairComments > before && C.Said != null,
                $"{C.Name}: \"{C.Said}\" · 솜씨 믿음 {w.Body2.Rep(C, B):0.00}");
            Check("말해 준 사람은 그 솜씨를 믿게 된다 (다음에 부탁할 사람)", w.Body2.Rep(C, B) >= 0.5f, $"{C.Name}→{B.Name} {w.Body2.Rep(C, B):0.00}");
        }

        // ── 3) 솜씨 없고 성급한 사람이 다친 손으로: 삐뚤빼뚤 → 수치 · 서운한 기억 → 다음엔 거절 · 다친 팔은 늘어뜨린다 ──
        {
            var w = BrainDay(seed, "Hanbit");
            var adults = Adults(w);
            var up = adults.Where(c => !SimTime.InWindow(SimTime.HourOfDay(w.Tick), c.Schedule.SleepStart - 5f, c.Schedule.SleepLength + 5f)).ToList();
            if (up.Count < 3) up = adults;
            var D = up[0]; var E = up[1];
            var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Lounge or RoomType.Mess && r.Furniture.Any(f => f.Type == FurnitureType.Seat)).OrderBy(r => r.Id).First();
            LookCalm(w, D, E);
            Excuse(w, D, E);
            var ld = w.Body2.Of(D); var le = w.Body2.Of(E);
            if (ld.Style == HairStyle.Bald) ld.Style = HairStyle.Side;
            if (ld.StyleCm < 2f) ld.StyleCm = 5f;
            ld.HairCm = BodyLook.ShagAt(ld.StyleCm) * 1.2f; ld.ShaggySince = w.Tick;
            le.Barber = 0.05f; if (!E.Habits.Contains(Habit.Hasty)) E.Habits.Add(Habit.Hasty);
            E.Vitals.Wounds.Add(new Wound { Part = BodyPart.RightArm, Kind = WoundKind.Fracture, Weight = 1f, Cause = "시험" });
            E.Vitals.Injury = 0.4f;
            D.Affinity[E.Id] = 0.5f; E.Affinity[D.Id] = 0.6f;
            BrainPut(w, D, room, 0); BrainPut(w, E, room, 1);
            w.Body2.Notice(E, D);
            var spec = Puppet.Of(w, E);
            Check("다친 팔은 늘어뜨린다 (인형 사양)", spec.Right == ArmState.Hurt && spec.Left == ArmState.Ok, $"왼팔 {spec.Left} · 오른팔 {spec.Right}");
            float affBefore = D.AffinityTo(E);
            var dist = w.Paths.Flood(E.Cell, E.PathProfile);
            var job = Brain.Activities.OfType<HaircutActivity>().First().Plan(E, w, dist);
            if (job != null) Force(w, E, job);
            for (int k = 0; k < SimTime.Hours(6) && ld.Cuts == 0; k++)
            {
                w.Step();
                if (debug && k % 600 == 0)
                {
                    var ss = w.Body2.SessionOf(E) ?? w.Body2.Sessions.LastOrDefault();
                    var de = w.Paths.Flood(E.Cell, E.PathProfile);
                    var (hs, hw) = Brain.Activities.OfType<HaircutActivity>().First().Score(E, w, de);
                    Console.WriteLine($"    [{SimTime.Clock(w.Tick)}] {D.Name}: {D.ActivityLabel} · {E.Name}: {E.ActivityLabel} (이발 점수 {hs:0.00} {hw}) · 세션 {(ss == null ? "-" : $"{ss.Progress:0.00} 앉음 {ss.Seated}")}");
                }
            }
            var emo = w.Brain2.Emotions;
            Check("솜씨 없고 성급한 사람이 다친 손으로 자르면 삐뚤빼뚤 → 수치(또는 화) · 서운한 기억 · 관계가 식는다", ld.LastCut == CutResult.Botched && ld.Uneven > 0.5f
                && emo.Get(D, Feeling.Shame) + emo.Get(D, Feeling.Anger) > 0.1f && w.Relations.Of(D, E).Any(m => m.Reason == RelationReason.BotchedMyHair) && D.AffinityTo(E) < affBefore,
                $"{Body2System.Name(ld.LastCut)} · 삐뚤 {ld.Uneven:0.00} · 수치 {emo.Get(D, Feeling.Shame):0.00} 화 {emo.Get(D, Feeling.Anger):0.00} · 호감 {affBefore:0.00}→{D.AffinityTo(E):0.00} · 자른 사람 수치 {emo.Get(E, Feeling.Shame):0.00}");
            // 장난꾸러기가 놀린다
            var F = up[2];
            if (!F.Habits.Contains(Habit.Joker)) F.Habits.Add(Habit.Joker);
            F.Traits.GetType().GetProperty("Sociability")!.SetValue(F.Traits, 0.95f);
            var fi = w.Body2.Seen(F, D);
            if (fi == null) { w.Body2.Notice(F, D); fi = w.Body2.Seen(F, D)!; }
            fi.CutAt = -5; fi.Said = -1_000_000;
            int teases = w.Body2.Stats.Teases;
            for (int k = 0; k < 8 && w.Body2.Stats.Teases == teases; k++) { fi.CutAt = -5; fi.Said = -1_000_000; w.Body2.Notice(F, D); }
            Check("장난꾸러기가 삐뚤빼뚤한 머리를 놀린다 → 수치", w.Body2.Stats.Teases > teases, $"{F.Name}: \"{F.Said}\"");
            // 다시 덥수룩해지면: 같은 사람이 자르겠다 해도 거절한다 (기억)
            ld.HairCm = BodyLook.ShagAt(ld.StyleCm) * 1.3f;
            bool refused = w.Body2.Refuses(E, D, out var rwhy);
            Check("망친 기억이 있으면 다음엔 맡기지 않는다", refused && rwhy.Contains("지난번"), rwhy);
        }

        // ── 4) 체중이 늘어 우주복이 안 맞는다: 컴퓨터가 재고 알린다 → 치수 조정 · 선외에서는 긁히고 산소를 더 쓴다 ──
        {
            var w = BrainDay(seed, "Hanbit");
            var adults = Adults(w);
            var G = adults.FirstOrDefault(c => Life.HasQual(c, Qual.Eva)) ?? adults[0];
            G.Quals.Add(Qual.Eva);
            Excuse(w, G);
            var lg = w.Body2.Of(G);
            lg.Kg = lg.SuitKg + 7.5f;
            var med = w.Ship.LiveRooms.First(r => r.Type == RoomType.Medbay);
            BrainPut(w, G, med, 0);
            Force(w, G, new Job(null, "시험 검진", new Toil[] { new WaitToil(SimTime.Minutes(30), Pose.Standing) }), SimTime.Minutes(30));
            int warns = w.Body2.Stats.FitWarnings;
            Run(w, SimTime.Minutes(25));
            var fitAct = w.Automation.Book.Acts.LastOrDefault(a => a.Observe.Contains("우주복 치수 불일치") && a.Observe.Contains(G.Name));
            Check("컴퓨터가 잰 체중으로 우주복 치수 불일치를 알린다 (의무실 체중계 → 다섯 칸)", w.Body2.Stats.FitWarnings > warns && fitAct != null && lg.FitOrder,
                fitAct == null ? "없음" : $"{fitAct.Observe} / {fitAct.Judge} / {fitAct.Act} / {fitAct.Request}");
            for (int k = 0; k < SimTime.Hours(8) && lg.FitOrder; k++) w.Step();
            Check("조정 작업: 보관함에서 치수를 지금 몸에 맞춘다", !lg.FitOrder && MathF.Abs(lg.Misfit) < 1f && w.Body2.Stats.FitAdjusts > 0,
                $"맞춘 치수 {lg.SuitKg:0.0}kg · 체중 {lg.Kg:0.0}kg · 조정 {w.Body2.Stats.FitAdjusts}");

            // 선외: 안 맞는 우주복(H) ↔ 맞는 우주복(I) — 같은 시간 밖에 있으면
            var w2 = BrainDay(seed, "Hanbit");
            var ad2 = Adults(w2);
            var H = ad2[0]; var I = ad2[1];
            var lh = w2.Body2.Of(H); var li = w2.Body2.Of(I);
            lh.Kg = lh.SuitKg + 9f; li.Kg = li.SuitKg;
            var spots = HullSpots(w2, 4);
            PutOutside(w2, H, spots[0]); PutOutside(w2, I, spots[Math.Min(3, spots.Count - 1)]);
            Force(w2, H, new Job(null, "시험 선외", new Toil[] { new WaitToil(SimTime.Hours(1), Pose.Working) }) { InterruptMargin = 5f }, SimTime.Hours(1));
            Force(w2, I, new Job(null, "시험 선외", new Toil[] { new WaitToil(SimTime.Hours(1), Pose.Working) }) { InterruptMargin = 5f }, SimTime.Hours(1));
            float oh0 = H.Suit!.Oxygen, oi0 = I.Suit!.Oxygen;
            Run(w2, SimTime.Minutes(50));
            float oh = oh0 - (H.Suit?.Oxygen ?? oh0), oi = oi0 - (I.Suit?.Oxygen ?? oi0);
            float sh = H.Suit?.Wear.Scuff ?? 0f, si = I.Suit?.Wear.Scuff ?? 0f;
            Check("안 맞는 우주복으로 선외: 관절이 긁히고 · 숨이 가빠 산소를 더 쓰고 · 무전으로 투덜댄다", w2.Body2.Stats.MisfitEva > 0 && sh > si && oh > oi && w2.EvaRisk.Radio.Any(r => r.From == H.Id && r.Text.Contains("꽉")),
                $"긁힘 {sh:0.000} ↔ {si:0.000} · 산소 {oh:0.00} ↔ {oi:0.00}시간 · 무전 \"{w2.EvaRisk.Radio.LastOrDefault(r => r.From == H.Id)?.Text}\"");
        }

        // ── 5) 먹는 양 · 운동: 군것질하는 사람은 붙고, 운동하는 사람은 빠진다 · 남이 알아채고 말한다 · 컴퓨터가 추세를 알린다 ──
        {
            var w = BrainDay(seed, "Hanbit");
            var adults = Adults(w);
            var P = adults[6 % adults.Count]; var Q = adults[7 % adults.Count];
            typeof(Personality).GetProperty("Appetite")!.SetValue(P.Traits, 1.3f);
            if (!P.Habits.Contains(Habit.Snacker)) P.Habits.Add(Habit.Snacker);
            typeof(Personality).GetProperty("Appetite")!.SetValue(Q.Traits, 0.85f);
            Q.Habits.Remove(Habit.Snacker);
            if (!Q.Habits.Contains(Habit.GymRat)) Q.Habits.Add(Habit.GymRat);
            w.Brain2.Goals.Push(Q, "body:trim", "몸을 좀 움직이자", "시험", ActCat.Hobby, 96f, 1.5f);
            var lp = w.Body2.Of(P); var lq = w.Body2.Of(Q);
            float p0 = lp.Kg, q0 = lq.Kg;
            int jogs0 = w.Body2.Stats.Jogs;
            for (int k = 0; k < SimTime.TicksPerDay * 2; k++)
            {
                w.Step();
                if (debug && k % SimTime.Hours(2) == 0)
                {
                    var dq = w.Paths.Flood(Q.Cell, Q.PathProfile);
                    var (js, jw) = Brain.Activities.OfType<JogActivity>().First().Score(Q, w, dq);
                    Console.WriteLine($"    [{SimTime.Clock(w.Tick)}] {Q.Name}: {Q.ActivityLabel} · 달리기 점수 {js:0.00} ({jw}) · 쉼 {Q.Needs.Rest:0.00} 배 {Q.Needs.Food:0.00} · {lq.Kg:0.0}kg");
                }
            }
            Check("많이 먹는 사람은 체중이 붙고 · 몸을 움직이는 사람은 빠진다 (실제로 먹은 것 · 쓴 것)", lp.Kg - p0 > lq.Kg - q0 + 0.3f && w.Body2.Stats.Jogs > jogs0,
                $"{P.Name} {p0:0.0}→{lp.Kg:0.0}kg · {Q.Name} {q0:0.0}→{lq.Kg:0.0}kg (달리기 {w.Body2.Stats.Jogs - jogs0}번)");
            // 남이 알아챈다: 지난번 본 체중과 견준다
            var R2 = adults.First(c => c != P && c != Q);
            if (!R2.Habits.Contains(Habit.Joker)) R2.Habits.Add(Habit.Joker);
            R2.Habits.Remove(Habit.Cheerful); R2.Habits.Remove(Habit.Optimist); R2.Value = CrewValue.Efficiency; R2.Affinity[P.Id] = 0f;
            P.Traits.GetType().GetProperty("Diligence")!.SetValue(P.Traits, 0.7f);
            w.Body2.Notice(R2, P);
            var imp = w.Body2.Seen(R2, P)!;
            int wc = w.Body2.Stats.WeightComments;
            for (int k = 0; k < 10 && w.Body2.Stats.WeightComments == wc; k++) { imp.Kg = lp.Kg - 4.5f; imp.Tick = w.Tick - SimTime.Hours(30); imp.Said = -1_000_000; w.Body2.Notice(R2, P); }
            Check("남의 체중 변화를 알아채고 말한다 (놀림 → 수치 → 몸을 움직이기로)", w.Body2.Stats.WeightComments > wc && w.Brain2.Emotions.Get(P, Feeling.Shame) > 0.05f && w.Brain2.Goals.Has(P, "body:trim"),
                $"{R2.Name}: \"{R2.Said}\" · {P.Name} 수치 {w.Brain2.Emotions.Get(P, Feeling.Shame):0.00} · 목표 {w.Brain2.Goals.Has(P, "body:trim")}");
            // 컴퓨터의 추세: 잰 체중이 일주일 사이 3kg 넘게 늘었다
            lp.Measured.Clear();
            lp.Measured.Add((w.Day - 6, lp.Kg - 4f));
            lp.Measured.Add((w.Day, lp.Kg));
            lp.KnownKg = lp.Kg; lp.KnownBy = "침대 압력 감지기"; lp.LastTrend = -1_000_000;
            int tw = w.Body2.Stats.TrendWarnings;
            RunUntilHour(w, 8.05f);
            Run(w, SimTime.Minutes(5));
            var tact = w.Automation.Book.Acts.LastOrDefault(a => a.Observe.Contains(P.Name) && a.Observe.Contains("체중"));
            Check("컴퓨터가 건강 추세를 알린다 (잰 것만으로) → 믿으면 따르고 · 못 믿으면 투덜댄다", w.Body2.Stats.TrendWarnings > tw && tact != null && w.Body2.Stats.Heeded + w.Body2.Stats.Shrugged > 0,
                tact == null ? "없음" : $"{tact.Observe} / {tact.Judge} / {tact.Request} · 따름 {w.Body2.Stats.Heeded} · 흘려들음 {w.Body2.Stats.Shrugged}");
        }

        // ── 6) 인형 사양: 양손 짐 · 무릎 꿇기 · 머리 길이 · 뛰기 ──
        {
            var w = BrainDay(seed, "Hanbit");
            var adults = Adults(w);
            var X = adults[0]; var Y = adults[1]; var Z = adults[2];
            Force(w, X, new Job(null, "시험 나르기", new Toil[] { new WaitToil(SimTime.Hours(1), Pose.Standing) }));
            X.Carrying = new ItemStack(ItemKind.Structure, 2);
            var sx = Puppet.Of(w, X);
            Check("큰 짐은 양손으로 든다", sx.Held == HeldThing.Crate && sx.TwoHands, $"{sx.Held} · 양손 {sx.TwoHands}");
            X.Carrying = new ItemStack(ItemKind.Meal, 1);
            Check("식사는 접시 · 소화기 · 구급 키트는 각자 다른 물건", Puppet.Of(w, X).Held == HeldThing.Plate
                && (X.Carrying = new ItemStack(ItemKind.Extinguisher, 1)) != null && Puppet.Of(w, X).Held == HeldThing.Extinguisher
                && (X.Carrying = new ItemStack(ItemKind.MedKit, 1)) != null && Puppet.Of(w, X).Held == HeldThing.MedKit, "접시 · 소화기 · 구급 키트");
            X.Carrying = null;
            Force(w, Y, new Job(null, "시험 쓰러짐", new Toil[] { new WaitToil(SimTime.Hours(1), Pose.Down) }));
            Y.Down = true; Y.Vitals.Health = 0.05f; Y.Position = Z.Position + new System.Numerics.Vector2(0.9f, 0f); Y.Path = null;
            Force(w, Z, new Job(null, "시험 돌봄", new Toil[] { new WaitToil(SimTime.Hours(1), Pose.Working) }));
            w.Step();
            Check("쓰러진 사람 곁에서 일하면 무릎을 꿇는다", Puppet.PoseOf(w, Z) == PuppetPose.Kneel && Puppet.PoseOf(w, Y) == PuppetPose.Lie, $"{Puppet.PoseOf(w, Z)} · 쓰러진 사람 {Puppet.PoseOf(w, Y)}");
            Y.Down = false;
            var lz = w.Body2.Of(Z);
            float s0 = lz.Shag; lz.HairCm = BodyLook.ShagAt(lz.StyleCm) + 2f;
            Check("머리 길이가 생김새에 남는다 (덥수룩함 — 화면이 이 값으로 머리 윤곽을 키운다)", lz.Style == HairStyle.Bald || lz.Shag >= 1f && lz.Shag > s0, $"{s0:0.00} → {lz.Shag:0.00}");
            Force(w, X, new Job(null, "시험 급히", new Toil[] { new GotoToil(w.Ship.LiveRooms.First(r => r.Type == RoomType.Bridge).Cells.First(w.Ship.IsOpenFloor)) }) { Urgent = true });
            w.Step();
            Check("급한 일은 뛰고 · 보통은 걷는다", Puppet.PoseOf(w, X) == PuppetPose.Run, $"{Puppet.PoseOf(w, X)}");
        }

        // ── 7) 저장 · 불러오기: 머리카락 · 체중 · 우주복 치수가 그대로 ──
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.TicksPerDay + SimTime.Hours(7));
            Player.Policy(w, "rations", 1);
            Run(w, SimTime.Hours(9));
            string text = SaveGame.Write(w);
            var runner = new ReplayRunner(text);
            while (!runner.Advance(50000)) { }
            var w2 = runner.World;
            bool same = w.Crew.All(c => w2.Crew.FirstOrDefault(x => x.Id == c.Id) is CrewMember c2 && w.Body2.Of(c).HairCm == w2.Body2.Of(c2).HairCm
                && w.Body2.Of(c).Kg == w2.Body2.Of(c2).Kg && w.Body2.Of(c).SuitKg == w2.Body2.Of(c2).SuitKg && w.Body2.Of(c).BeardMm == w2.Body2.Of(c2).BeardMm);
            var c0 = w.Crew.First();
            Check("저장 · 불러오기: 머리카락 · 수염 · 체중 · 우주복 치수가 남는다 (같은 역사 · 지문)", runner.Verified && same,
                $"{c0.Name} 머리 {w.Body2.Of(c0).HairCm:0.00}cm · {w.Body2.Of(c0).Kg:0.00}kg · 지문 {SaveGame.StateHash(w):x8}/{SaveGame.StateHash(w2):x8}");
        }

        // ── 8) 결정론 ──
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("결정론 — 같은 시드 같은 지문", a == b, $"{a:x8} / {b:x8}");
        }

        // ── 9) 성능: 30명 하루 (끔 ↔ 켬) · 인형 사양 60명 한 장면 ──
        {
            double Time(bool on, out Body2Stats st)
            {
                Body2System.Enabled = on;
                var w = World.CreateDefault(seed, 0, "Cheonma");
                var sw = Stopwatch.StartNew();
                Run(w, SimTime.TicksPerDay);
                sw.Stop();
                st = w.Body2.Stats;
                Body2System.Enabled = true;
                return sw.Elapsed.TotalSeconds;
            }
            double off = Math.Min(Time(false, out _), Time(false, out _));
            double on = Math.Min(Time(true, out var st), Time(true, out _));
            double ratio = on / Math.Max(0.001, off);
            Check("성능 — 30명 하루가 크게 늘지 않는다", ratio < 1.12, $"천마 30명 하루: 끔 {off:0.00}초 · 켬 {on:0.00}초 (×{ratio:0.00}) · {st.Summary()}");
            var pw = World.CreateDefault(seed, 0, "Cheonma");
            Run(pw, SimTime.Hours(12));
            var crew = pw.Crew.Concat(pw.Crew).ToList(); // 60명 몫
            var sw2 = Stopwatch.StartNew();
            int frames = 300; long sum = 0;
            for (int f = 0; f < frames; f++) foreach (var c in crew) sum += (int)Puppet.Of(pw, c).Held;
            sw2.Stop();
            double perFrame = sw2.Elapsed.TotalMilliseconds / frames;
            Check("인형 사양 60명 한 장면이 가볍다 (그림 판정)", perFrame < 1.5, $"{perFrame:0.000}ms/장면 ({sum % 7})");
        }

        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
