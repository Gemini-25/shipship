using System.Collections.Generic;

namespace ShipSim.Core;

/// <summary>
/// 승무원의 판단. 모든 행동 후보에 점수를 매기고 가장 급한 것을 고른다.
/// 평소에는 10분마다 다시 생각하고, 경보나 위험을 알아채면 그 즉시 다시 생각한다(긴급 인터럽트).
/// </summary>
public static class Brain
{
    public static readonly Activity[] Activities =
    {
        new PanicActivity(), // v13.3 공황 (얼어붙거나 달아난다)
        new EvacuateActivity(),
        new EvaSurviveActivity(), new EvaRescueActivity(), new SuitMendActivity(), // v16.11 선외 생존(표류 · 패치 · 그늘) · 구조 EVA · 에어락 마중 · 우주복 수리
        new TakeCoverActivity(), new BlastResponseActivity(), new BlastingActivity(), // v16.13 쉭 소리에 몸을 피함 · 구조 · 조사 · 옮기기 · 항아리 · 추모 · 폭파
        new WayActivity(), // v16.25 여러 갈래 해법 (고른 갈래 · 나중에 제대로)
        new CheckRoomActivity(), // v16.6 컴퓨터 확인 요청 (직접 가서 보고 쓰러진 사람을 데려 나온다)
        new ShelterActivity(), // v12.6 태양 폭풍
        new HeedBroadcastActivity(), // v16.6 대피 방송을 들은 사람만 미리 대피소로 (ComputerLinks.cs)
        new MusterActivity(), // v16.18 배 전체 사고 — 전원 소집 · 점호 (ScalePlan.cs)
        new CosmicEvacuateActivity(), new CosmicShelterActivity(), new CosmicWarnActivity(), new CosmicBraceActivity(), new CosmicVigilActivity(), new CosmicLookActivity(), // v18.13 우주 대재난: 비우기 · 대피 · 알리기 · 대비 · 그날의 밤 · 창밖 보기
        new QuarantineActivity(), // v12.6 격리실
        new RecoverActivity(),
        new RadCareActivity(), new GiveBloodActivity(), // 통합5 방사선 병 간호 · 피 나눠 주기
        new StowSuitActivity(),
        new RefillSuitActivity(),
        new EatActivity(),
        new SleepActivity(),
        new ChoresActivity(),
        new StationActivity(), new HelpActivity(), // v16.21 비상 배치 자리로 · 큰 일 거들기
        new PatrolActivity(), // v13.4 야간 당직
        new DutyActivity(),
        new MeetingActivity(), // v13.2 정기 회의
        new CoverActivity(), // v18.7 흔적 치우기 · 기록 지우기 · 털어놓으러 가기
        new SchemeActivity(), // v18.14 꾸미는 일 (몰래 준비 · 귓속말 · 확인 · 모임 · 관행 · 일손 놓기)
        new SittingActivity(), new PetitionActivity(), new PenaltyDutyActivity(), new SneakFoodActivity(), new FeastActivity(), // v18.18 따로 연 회의 · 서명 받기 · 벌 근무 · 몰래 꺼내 먹기 · 잔치
        new VisitActivity(), // v12.7 문병
        new HoldActivity(), // v14.1 잠깐 그 자리에서 기다린다
        new HobbyActivity(), // v14.3 취미 (물건을 가져와서 하고 제자리에)
        new TidyActivity(), // v14.3 두고 온 물건 찾아오기 · 정리
        new MendActivity(), // v14.3 망가진 물건 고쳐 주기
        new ReachOutActivity(), new ReclaimActivity(), new PartTestActivity(), new WashUpActivity(), new LaundryActivity(), new DeconActivity(), new FlushActivity(), new ExtinguisherCheckActivity(), new MemorialVisitActivity(), new SharedMealActivity(), // v14.9 관행 · v14.8 급수관 씻어 내기 · v14.7 씻기 · 빨래 · 우주복 털기 · v14.6 부품 시험 · v14.5 두고 간 짐 · v14.4 목적 있는 말 걸기 (걱정 · 위로 · 신입 · 사과 · 진실 · 소문)
        new ShipRoundsActivity(), // v16.9 닳은 배의 아침 한 바퀴
        new AltCropActivity(), // v16.22 수경이 멎으면 다른 재배실을 한 번 더
        new ExpeditionActivity(), // v16.12 원정 출발 · 우주복 점검 · 무전 기다리기 · 마중 · 전리품 · 식탁 이야기
        new ResearchActivity(), // v16.14 실험 (연구자가 실험실 · 작업대에서 — 끊기면 노트 · 이어 하기)
        new AfterActivity(), // v17.5 사고 뒤 손질 (침구 널기 · 걷기 · 냉장고 고르기 · 독서등 · 불탄 그림 다시 그리기)
        new AnnexWorkActivity(), // v16.10 증축 공사 (선외 골조 · 외판 · 기밀 시험 · 배선 · 비닐 막 · 내장 · 개통식)
        new RoomWorkActivity(), // v16.17 방 공사 (분리 · 같이 들기 · 카트 · 다시 잇기 · 칸막이 · 표지판 · 선실 꾸미기 · 땀방 운동)
        new PortableActivity(), // v16.7 이동식 장비 (꺼내 와 설치 · 배터리 · 회수 · 뽑기 · 기다리기)
        new InspectActivity(), // v14.4 소문을 듣고 확인하러 간다
        new MatterActivity(), // v16.4 고무 매트 · 불 곁 천 치우기 · 그을리는 것 · 젖은 러그 · 접속부 · 통로 · 손잡이
        new OpenDoorActivity(), new BodyUpkeepActivity(), // v16.3 잠긴 문 열어 주기 · 배 손보기 (뚜껑 · 패널 · 문 · 유리 · 빈 걸이)
        SceneActivity.Instance, // v16.1 일상 장면 (체스 · 커피 · 영화 · 닦기 · 간식 · 몽유병 · 소품 · 인수인계 확인)
        new CheckSmellActivity(), new SavedPlateActivity(), new SetAsidePlateActivity(), new FollowSmellActivity(), // v16.8 탄내 확인 · 남겨 둔 접시 · 냄새를 따라
        PlanActivity.Instance, new OutageActivity(), new FireBeliefActivity(), new TellActivity(), // v16.15 두뇌 2.0: 계획대로 · 정전 대처 · 믿음대로 불 확인 · 알리러 감
        new HaircutActivity(), new SuitFitActivity(), new JogActivity(), new SweepClipsActivity(), // v17.1 이발 · 우주복 치수 조정 · 몸 관리 달리기 · 머리카락 치우기
        new LendHandActivity(), new SpectateActivity(), new SpaceTidyActivity(), new CoffeeRunActivity(), // v17.4 잡아 주기 · 구경 · 통로 상자 치우기 · 커피 줄
        new InfoActivity(), // v17.3 소리 확인 · 따지기 · 해명 · 사과 · 물건 찾기 · 못 끝낸 일 · 설거지 · 사진
        new MateActivity(), // v16.27 훈련 집결 · 컴퓨터가 부탁한 안부 · 컴퓨터 개조 공사
        new ReactActivity(), // v17.8 반응에서 이어지는 짧은 행동 (장비 · 담요 · 창가 · 소리 확인 · 말 걸기 · 위로 · 구경)
        new ChatActivity(),
        new RelaxActivity(),
        new WanderActivity(),
    };

    public const float Noise = 0.05f;

    public static readonly int ThinkInterval = SimTime.Minutes(10);

    /// <summary>판단 횟수 (성능 점검용).</summary>
    public static long ThinkCount;

    private static Job? PlanTimed(Activity a, CrewMember c, World w, DistanceField dist)
    {
        long t = Prof.Now;
        var job = a.Plan(c, w, dist);
        Prof.Lap(a.PlanKey, t);
        return job;
    }

    private static bool SwitchTimed(ChoresActivity a, CrewMember c, World w, DistanceField dist, WorkOrder cur)
    {
        long t = Prof.Now;
        bool r = a.ShouldSwitch(c, w, dist, cur);
        Prof.Lap("chores.ShouldSwitch", t);
        return r;
    }

    public static void Think(CrewMember c, World w)
    {
        ThinkCount++;
        ChoresActivity.ResetMemo(); // v14.2
        var dist = w.Paths.Flood(c.Cell, c.PathProfile);
        var evals = new List<Evaluation>(Activities.Length);
        long pt = Prof.Now;
        foreach (var a in Activities)
        {
            var (score, reason) = a.Score(c, w, dist);
            pt = Prof.Lap(a.ScoreKey, pt);
            // 위기 판단: 비상·생존 위기에는 잠·휴식을 미룬다 (탈진 직전이면 쪽잠)
            if (a is RelaxActivity or ChatActivity or WanderActivity or HobbyActivity or MendActivity or ReachOutActivity) score *= w.Society.LeisureFactor; // v13.4 휴식·여가 방침
            w.Brain2.Tilt(c, a, ref score, ref reason); // v16.15 장 · 중기 목표와 감정이 점수를 기울인다
            float damp = Crisis.Damp(c, w, a, out var note);
            if (damp < 1f && score > 0f) { score *= damp; if (note != null) reason += $" · {note}"; }
            w.Scale.Damp(c, a, ref score, ref reason); // v16.18 배 전체 · 우주급을 느끼면 일상을 멈춘다
            w.CrisisCrew.Damp(c, a, ref score, ref reason); // v16.21 제 비상 자리가 있으면 잠 · 끼니 · 여가가 생명 일을 이기지 않게
            if (score > 0f) score += w.Rng.Range(-Noise, Noise);
            evals.Add(new Evaluation(a, score < 0f ? 0f : score, reason));
        }
        evals.Sort((x, y) => y.Score.CompareTo(x.Score));
        c.LastEvaluations = evals;
        c.LastThinkTick = w.Tick;

        if (c.Job != null)
        {
            float current = 0f;
            foreach (var e in evals)
                if (e.Activity == c.Job.Activity) { current = e.Score; break; }

            var best = evals[0];
            if (best.Activity == c.Job.Activity)
            {
                // 같은 "작업"이라도 훨씬 급한 일이 올라오면 하던 정비를 내려놓고 간다
                if (best.Activity is ChoresActivity chores && c.Job.Order is WorkOrder cur && SwitchTimed(chores, c, w, dist, cur))
                {
                    var urgent = PlanTimed(chores, c, w, dist);
                    if (urgent != null)
                    {
                        c.EndJob(w, ToilStatus.Interrupted);
                        c.StartJob(urgent, w, best);
                    }
                }
                return;
            }
            // 손에 익은 일을 절반 넘게 했으면 더 버틴다
            float margin = c.Job.InterruptMargin + (c.Job.Current is WorkToil { Progress: > 0.4f } ? 0.2f : 0f);
            // 위기에는 쉬던 사람(잠·휴식·수다)이 금방 일어난다
            if (c.Job.Activity is SleepActivity or RelaxActivity or ChatActivity or WanderActivity or DutyActivity or HobbyActivity or TidyActivity or MendActivity && Crisis.Acting(w))
                margin = System.MathF.Min(margin, 0.08f);
            // v10.5: 긴 개조·정비 중에도 굶주리면 손을 놓고 먹으러 간다 (급한 일은 예외 — 불 끄던 사람은 버틴다)
            if (!c.Job.Urgent && c.Needs.Hunger > 0.85f && best.Activity is EatActivity) margin = 0f; // v10.10: 0.9 → 0.85 (급한 수리 뒤 끼니를 놓치던 것)
            if (best.Score < current + margin) return;

            var next = PlanTimed(best.Activity, c, w, dist);
            if (next == null) return;
            c.EndJob(w, ToilStatus.Interrupted);
            c.StartJob(next, w, best);
            return;
        }

        foreach (var e in evals)
        {
            if (e.Score <= 0f) continue;
            var job = PlanTimed(e.Activity, c, w, dist);
            if (job == null) continue;
            c.StartJob(job, w, e);
            return;
        }
        c.StartJob(IdleJob.Create(), w, null);
    }
}
