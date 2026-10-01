using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v14.0 승무원 개성: 경력 40 · 습관 40 · 자격 20 · 취미 30 · 두려움 20 · 말버릇 30.
//
// 경력: 솜씨 하나와 자격, 기우는 가치관, 생기기 쉬운 취미.
// 습관: 숫자로 된 효과 — 스트레스 · 교류 욕구 · 식욕 · 실수 · 손 빠르기 · 공황 · 설득력 · 잠드는 시각(각자 일과일 때) · 부딪히는 습관.
// 자격: 그 일을 제대로 하는 자격 (없으면 서툴고 실수가 잦다, 자격 있는 사람이 깨어 있으면 급하지 않은 일은 그 사람 몫) — 솜씨가 차면 딴다.
// 취미: 쉴 때 찾아가는 방 (그 방에서 쉬면 더 풀린다), 같은 취미끼리 곁에서 쉬면 더 가까워진다.
// 두려움: 그 상황이면 공황이 잦고, 그 일을 꺼린다.
// 말버릇: 회의 발언과 일기에 묻어난다.

public enum Hobby
{
    Reading, Music, Instrument, Painting, Photography, Cooking, Baking, Gardening, Workout, Yoga,
    Meditation, Games, Cards, Chess, ModelBuilding, Knitting, Writing, Journaling, Stargazing, Movies,
    Singing, Dancing, Puzzles, Collecting, VirtualReality, Woodwork, Electronics, Tea, Storytelling, Walking,
}

public enum Fear
{
    Dark, Confined, Vacuum, Fire, Blood, Isolation, Spacewalk, Suffocation, Water, Radiation,
    Disease, Death, Failure, Command, Noise, Electricity, Cold, Gas, Meteors, Machines,
}

public sealed record BackgroundSpec(Background Id, string Name, Skill Skill, float Bonus, Qual[] Quals, CrewValue? Lean, Hobby[] Hobbies);

public sealed record HabitSpec(Habit Id, string Name, float Stress = 1f, float Social = 1f, float Appetite = 1f, float Mistake = 1f,
    float Speed = 1f, float Panic = 1f, float Persuade = 0f, float BedShift = 0f, float Rest = 1f, float Clash = 1f, Habit[]? Rivals = null, string About = "");

public sealed record QualSpec(Qual Id, string Name, Skill Skill, float Need, string Note);
public sealed record HobbySpec(Hobby Id, string Name, RoomType[] Rooms, string Doing);
public sealed record FearSpec(Fear Id, string Name, string Note);

public static class Persona
{
    private static readonly Qual[] None = Array.Empty<Qual>();
    private static Qual[] Q(params Qual[] q) => q;
    private static Hobby[] H(params Hobby[] h) => h;

    public static readonly BackgroundSpec[] Backgrounds =
    {
        new(Background.Miner, "소행성 광부", Skill.Mechanics, 0.08f, Q(Qual.Eva), null, H(Hobby.Collecting, Hobby.Cards)),
        new(Background.MilitaryTech, "군 정비병", Skill.Electrical, 0.08f, Q(Qual.Eva, Qual.Electrical), CrewValue.Rules, H(Hobby.Workout, Hobby.Cards)),
        new(Background.FarmResearcher, "농업 연구원", Skill.Botany, 0.1f, Q(Qual.Agronomy), CrewValue.Safety, H(Hobby.Gardening, Hobby.Reading)),
        new(Background.CargoPilot, "화물선 항해사", Skill.Piloting, 0.1f, Q(Qual.Helm), CrewValue.Efficiency, H(Hobby.Stargazing, Hobby.Music)),
        new(Background.MedStudent, "의대 중퇴", Skill.Medicine, 0.08f, Q(Qual.Medic), null, H(Hobby.Reading, Hobby.Puzzles)),
        new(Background.Reporter, "기자", Skill.Mechanics, 0.03f, None, CrewValue.Freedom, H(Hobby.Writing, Hobby.Photography)),
        new(Background.Teacher, "교사", Skill.Mechanics, 0.03f, Q(Qual.Instructor), CrewValue.People, H(Hobby.Reading, Hobby.Storytelling)),
        new(Background.Chef, "요리사", Skill.Cooking, 0.12f, Q(Qual.FoodSafety), null, H(Hobby.Cooking, Hobby.Tea)),
        new(Background.Lineworker, "송전 기사", Skill.Electrical, 0.1f, Q(Qual.Electrical), null, H(Hobby.Cards, Hobby.Music)),
        new(Background.Programmer, "프로그래머", Skill.Engineering, 0.06f, Q(Qual.Computer), CrewValue.Efficiency, H(Hobby.Games, Hobby.VirtualReality)),
        new(Background.Artist, "화가", Skill.Mechanics, 0.03f, None, CrewValue.Freedom, H(Hobby.Painting, Hobby.Photography)),
        new(Background.Athlete, "운동선수", Skill.Mechanics, 0.03f, None, null, H(Hobby.Workout, Hobby.Dancing)),
        new(Background.Firefighter, "소방관", Skill.Mechanics, 0.06f, Q(Qual.Firefighting, Qual.RescueTeam), CrewValue.Safety, H(Hobby.Workout, Hobby.Cards)),
        new(Background.Nurse, "간호사", Skill.Medicine, 0.1f, Q(Qual.Medic), CrewValue.People, H(Hobby.Knitting, Hobby.Tea)),
        new(Background.Welder, "용접공", Skill.Mechanics, 0.1f, Q(Qual.Welding), null, H(Hobby.ModelBuilding, Hobby.Music)),
        new(Background.Plumber, "배관공", Skill.Mechanics, 0.08f, Q(Qual.Plumbing), null, H(Hobby.Puzzles, Hobby.Cards)),
        new(Background.Chemist, "화학자", Skill.Engineering, 0.06f, Q(Qual.Chemistry), CrewValue.Safety, H(Hobby.Chess, Hobby.Collecting)),
        new(Background.Physicist, "물리학자", Skill.Engineering, 0.08f, Q(Qual.Radiation), CrewValue.Efficiency, H(Hobby.Stargazing, Hobby.Chess)),
        new(Background.Astronomer, "천문학자", Skill.Piloting, 0.06f, None, CrewValue.Freedom, H(Hobby.Stargazing, Hobby.Photography)),
        new(Background.Diver, "잠수부", Skill.Mechanics, 0.05f, Q(Qual.Eva), null, H(Hobby.Meditation, Hobby.Collecting)),
        new(Background.Paramedic, "구급대원", Skill.Medicine, 0.08f, Q(Qual.Medic, Qual.RescueTeam), CrewValue.People, H(Hobby.Workout, Hobby.Movies)),
        new(Background.Soldier, "군인", Skill.Mechanics, 0.05f, Q(Qual.RescueTeam), CrewValue.Rules, H(Hobby.Workout, Hobby.Cards)),
        new(Background.Police, "경찰", Skill.Mechanics, 0.03f, None, CrewValue.Rules, H(Hobby.Cards, Hobby.Movies)),
        new(Background.Lawyer, "변호사", Skill.Engineering, 0.02f, None, CrewValue.Rules, H(Hobby.Reading, Hobby.Chess)),
        new(Background.Accountant, "회계사", Skill.Engineering, 0.03f, None, CrewValue.Efficiency, H(Hobby.Puzzles, Hobby.Tea)),
        new(Background.Monk, "수도승", Skill.Botany, 0.04f, Q(Qual.Counseling), CrewValue.People, H(Hobby.Meditation, Hobby.Gardening)),
        new(Background.Psychologist, "심리학자", Skill.Medicine, 0.04f, Q(Qual.Counseling), CrewValue.People, H(Hobby.Reading, Hobby.Journaling)),
        new(Background.AutoMechanic, "자동차 정비사", Skill.Mechanics, 0.08f, None, null, H(Hobby.ModelBuilding, Hobby.Music)),
        new(Background.Carpenter, "목수", Skill.Mechanics, 0.07f, Q(Qual.Structure), null, H(Hobby.Woodwork, Hobby.Tea)),
        new(Background.Gardener, "정원사", Skill.Botany, 0.08f, Q(Qual.Agronomy), null, H(Hobby.Gardening, Hobby.Painting)),
        new(Background.Baker, "제빵사", Skill.Cooking, 0.1f, Q(Qual.FoodSafety), null, H(Hobby.Baking, Hobby.Storytelling)),
        new(Background.Veterinarian, "수의사", Skill.Medicine, 0.07f, Q(Qual.Medic), CrewValue.People, H(Hobby.Gardening, Hobby.Reading)),
        new(Background.DroneRacer, "드론 레이서", Skill.Electrical, 0.05f, Q(Qual.DronePilot), CrewValue.Freedom, H(Hobby.Games, Hobby.Electronics)),
        new(Background.Roboticist, "로봇공학자", Skill.Engineering, 0.08f, Q(Qual.RobotTech), CrewValue.Efficiency, H(Hobby.Electronics, Hobby.ModelBuilding)),
        new(Background.SysAdmin, "서버 관리자", Skill.Electrical, 0.06f, Q(Qual.Computer), CrewValue.Rules, H(Hobby.Games, Hobby.Puzzles)),
        new(Background.Musician, "음악가", Skill.Mechanics, 0.02f, None, CrewValue.Freedom, H(Hobby.Instrument, Hobby.Singing)),
        new(Background.Writer, "작가", Skill.Mechanics, 0.02f, None, CrewValue.Freedom, H(Hobby.Writing, Hobby.Reading)),
        new(Background.TruckDriver, "트럭 운전사", Skill.Piloting, 0.06f, None, null, H(Hobby.Music, Hobby.Storytelling)),
        new(Background.Climber, "산악인", Skill.Mechanics, 0.05f, Q(Qual.Eva), CrewValue.Freedom, H(Hobby.Workout, Hobby.Photography)),
        new(Background.SafetyInspector, "안전 감독관", Skill.Engineering, 0.05f, Q(Qual.Firefighting, Qual.LifeSupport), CrewValue.Safety, H(Hobby.Puzzles, Hobby.Journaling)),
    };

    public static readonly HabitSpec[] Habits =
    {
        new(Habit.NightOwl, "올빼미", BedShift: 1.5f, Rivals: new[] { Habit.EarlyBird }, About: "밤늦게 내는 소리를"),
        new(Habit.EarlyBird, "아침형", BedShift: -1.5f),
        new(Habit.NeatFreak, "정리광", Mistake: 0.75f, Rivals: new[] { Habit.Messy, Habit.Hoarder }, About: "어질러 둔 공구를"),
        new(Habit.Messy, "어지르기", Mistake: 1.4f),
        new(Habit.Talker, "수다쟁이", Social: 1.3f, Rivals: new[] { Habit.Loner }, About: "쉴 새 없는 수다를"),
        new(Habit.Loner, "혼자가 편함", Social: 0.5f),
        new(Habit.GymRat, "운동광", Stress: 0.9f),
        new(Habit.Snacker, "군것질", Appetite: 1.15f),
        new(Habit.Worrier, "걱정이 많음", Stress: 1.1f, Mistake: 0.75f, Panic: 1.3f),
        new(Habit.Tinkerer, "만지작거림", Speed: 1.02f),
        new(Habit.Perfectionist, "완벽주의", Stress: 1.1f, Mistake: 0.7f, Speed: 0.94f, Rivals: new[] { Habit.Hasty }, About: "대충 하는 버릇을"),
        new(Habit.Hasty, "서두름", Mistake: 1.3f, Speed: 1.08f),
        new(Habit.Procrastinator, "미루기", Speed: 0.95f, Stress: 0.95f),
        new(Habit.Optimist, "낙천가", Stress: 0.85f, Panic: 0.8f),
        new(Habit.Pessimist, "비관적", Stress: 1.15f, Persuade: -0.05f, Rivals: new[] { Habit.Optimist }, About: "늘 최악을 말하는 것을"),
        new(Habit.Joker, "농담꾼", Social: 0.9f, Persuade: 0.03f, Rivals: new[] { Habit.Serious }, About: "가벼운 농담을"),
        new(Habit.Serious, "진지함", Persuade: 0.05f),
        new(Habit.Superstitious, "미신을 믿음", Panic: 1.2f),
        new(Habit.Insomniac, "불면증", Rest: 0.85f, Stress: 1.05f),
        new(Habit.HeavySleeper, "잠꾸러기", Rest: 1.1f),
        new(Habit.CoffeeAddict, "커피 중독", Speed: 1.03f, Stress: 1.05f),
        new(Habit.TeaLover, "차 애호가", Stress: 0.95f),
        new(Habit.Hoarder, "모아 두기"),
        new(Habit.Generous, "퍼주기", Persuade: 0.02f, Clash: 0.85f),
        new(Habit.Grumbler, "투덜이", Stress: 1.05f, Rivals: new[] { Habit.Optimist, Habit.Cheerful }, About: "투덜거림을"),
        new(Habit.Hummer, "흥얼거림", Social: 0.95f, Rivals: new[] { Habit.Loner, Habit.Serious }, About: "흥얼거리는 소리를"),
        new(Habit.Bookworm, "책벌레", Stress: 0.95f, Social: 0.85f),
        new(Habit.Gazer, "창밖 보기", Stress: 0.95f),
        new(Habit.Fidgety, "안절부절", Panic: 1.15f, Speed: 1.02f),
        new(Habit.Methodical, "꼼꼼함", Mistake: 0.8f, Speed: 0.97f),
        new(Habit.Daredevil, "겁 없음", Panic: 0.7f, Mistake: 1.1f),
        new(Habit.Homesick, "향수병", Stress: 1.08f),
        new(Habit.Cheerful, "명랑함", Social: 0.9f, Persuade: 0.03f, Stress: 0.95f),
        new(Habit.ShortTempered, "다혈질", Clash: 1.4f),
        new(Habit.Patient, "참을성", Clash: 0.7f),
        new(Habit.Forgetful, "깜빡함", Mistake: 1.25f),
        new(Habit.Leader, "앞장서기", Persuade: 0.08f),
        new(Habit.Follower, "따르기", Persuade: -0.04f),
        new(Habit.Collector, "모으기"),
        new(Habit.Prankster, "장난꾸러기", Social: 0.9f, Rivals: new[] { Habit.Serious, Habit.Perfectionist }, About: "장난을"),
    };

    public static readonly QualSpec[] Quals =
    {
        new(Qual.Reactor, "원자로 운전", Skill.Engineering, 0.6f, "원자로 재기동"),
        new(Qual.Eva, "선외 작업", Skill.Mechanics, 0.6f, "선체 밖 일"),
        new(Qual.Medic, "응급 의료", Skill.Medicine, 0.5f, "치료·구조"),
        new(Qual.Helm, "조타", Skill.Piloting, 0.55f, "관제석 수동 조종"),
        new(Qual.Electrical, "고압 전기", Skill.Electrical, 0.55f, "분전함·배전반"),
        new(Qual.Welding, "용접", Skill.Mechanics, 0.65f, "격벽 용접·외벽 수리"),
        new(Qual.Plumbing, "배관", Skill.Mechanics, 0.6f, "관 땜질·갈기·밸브"),
        new(Qual.LifeSupport, "생명유지", Skill.Engineering, 0.55f, "산소 발생기·세정기·정수기"),
        new(Qual.Computer, "컴퓨터", Skill.Electrical, 0.6f, "주 컴퓨터·감지기 교정"),
        new(Qual.Chemistry, "화학 가스", Skill.Engineering, 0.6f, "가스 빼기·산소관"),
        new(Qual.Firefighting, "소방", Skill.Mechanics, 0.55f, "화재 진압"),
        new(Qual.RescueTeam, "구조대", Skill.Medicine, 0.45f, "쓰러진 사람 구조"),
        new(Qual.FoodSafety, "조리 위생", Skill.Cooking, 0.55f, "조리"),
        new(Qual.Agronomy, "재배", Skill.Botany, 0.55f, "재배대 돌보기·수확"),
        new(Qual.RobotTech, "로봇 정비", Skill.Engineering, 0.55f, "로봇 정비·수리"),
        new(Qual.DronePilot, "드론 조종", Skill.Piloting, 0.5f, "드론 직접 조종·정비"),
        new(Qual.Radiation, "방사선 안전", Skill.Engineering, 0.65f, "원자로 노심 수리"),
        new(Qual.Structure, "골조", Skill.Mechanics, 0.7f, "연결부·트러스·골조 재건"),
        new(Qual.Counseling, "심리 상담", Skill.Medicine, 0.45f, "중재·문병"),
        new(Qual.Instructor, "교관", Skill.Mechanics, 0.8f, "가르치기"),
    };

    public static readonly HobbySpec[] Hobbies =
    {
        new(Hobby.Reading, "독서", new[] { RoomType.Archive, RoomType.Lounge }, "책을 읽는다"),
        new(Hobby.Music, "음악 감상", new[] { RoomType.Lounge, RoomType.Theater }, "음악을 듣는다"),
        new(Hobby.Instrument, "악기 연주", new[] { RoomType.Theater, RoomType.Lounge }, "악기를 탄다"),
        new(Hobby.Painting, "그림", new[] { RoomType.Garden, RoomType.Lounge }, "그림을 그린다"),
        new(Hobby.Photography, "별 사진", new[] { RoomType.Observatory, RoomType.Lounge }, "별을 찍는다"),
        new(Hobby.Cooking, "요리", new[] { RoomType.Mess, RoomType.Galley }, "새 요리를 궁리한다"),
        new(Hobby.Baking, "빵 굽기", new[] { RoomType.Mess, RoomType.Galley }, "반죽을 치댄다"),
        new(Hobby.Gardening, "원예", new[] { RoomType.Garden, RoomType.Hydroponics }, "화분을 돌본다"),
        new(Hobby.Workout, "운동", new[] { RoomType.Gym }, "땀을 뺀다"),
        new(Hobby.Yoga, "요가", new[] { RoomType.Gym, RoomType.Meditation }, "몸을 푼다"),
        new(Hobby.Meditation, "명상", new[] { RoomType.Meditation, RoomType.Chapel }, "눈을 감는다"),
        new(Hobby.Games, "게임", new[] { RoomType.Lounge }, "게임을 한다"),
        new(Hobby.Cards, "카드놀이", new[] { RoomType.Mess, RoomType.Lounge }, "카드를 친다"),
        new(Hobby.Chess, "체스", new[] { RoomType.Lounge, RoomType.Mess }, "체스를 둔다"),
        new(Hobby.ModelBuilding, "모형 만들기", new[] { RoomType.Lounge, RoomType.Workshop }, "모형을 조립한다"),
        new(Hobby.Knitting, "뜨개질", new[] { RoomType.Lounge, RoomType.Mess }, "뜨개질을 한다"),
        new(Hobby.Writing, "글쓰기", new[] { RoomType.Archive, RoomType.Lounge }, "글을 쓴다"),
        new(Hobby.Journaling, "일기 쓰기", new[] { RoomType.Lounge, RoomType.Quarters }, "일기를 쓴다"),
        new(Hobby.Stargazing, "별 보기", new[] { RoomType.Observatory, RoomType.Lounge }, "별을 본다"),
        new(Hobby.Movies, "영화", new[] { RoomType.Theater, RoomType.Lounge }, "옛 영화를 본다"),
        new(Hobby.Singing, "노래", new[] { RoomType.Theater, RoomType.Lounge }, "노래를 부른다"),
        new(Hobby.Dancing, "춤", new[] { RoomType.Gym, RoomType.Theater }, "춤을 춘다"),
        new(Hobby.Puzzles, "퍼즐", new[] { RoomType.Lounge, RoomType.Mess }, "퍼즐을 맞춘다"),
        new(Hobby.Collecting, "돌 수집", new[] { RoomType.Lab, RoomType.Lounge }, "모은 돌을 닦는다"),
        new(Hobby.VirtualReality, "가상 현실", new[] { RoomType.Lounge, RoomType.Theater }, "가상 현실에서 바다를 걷는다"),
        new(Hobby.Woodwork, "목공", new[] { RoomType.Workshop, RoomType.Lounge }, "나무를 깎는다"),
        new(Hobby.Electronics, "전자 공작", new[] { RoomType.ElectronicsLab, RoomType.Workshop }, "회로를 만진다"),
        new(Hobby.Tea, "차 마시기", new[] { RoomType.Mess, RoomType.Garden }, "차를 우린다"),
        new(Hobby.Storytelling, "옛이야기", new[] { RoomType.Mess, RoomType.Lounge }, "옛이야기를 들려준다"),
        new(Hobby.Walking, "통로 걷기", new[] { RoomType.Corridor, RoomType.Garden }, "통로를 걷는다"),
    };

    public static readonly FearSpec[] Fears =
    {
        new(Fear.Dark, "어둠", "캄캄한 방"),
        new(Fear.Confined, "좁은 곳", "작은 방"),
        new(Fear.Vacuum, "진공", "새는 방"),
        new(Fear.Fire, "불", "불난 곳"),
        new(Fear.Blood, "피", "다친 사람"),
        new(Fear.Isolation, "고립", "혼자 위험한 곳"),
        new(Fear.Spacewalk, "선체 밖", "선외 작업"),
        new(Fear.Suffocation, "질식", "산소가 묽은 방"),
        new(Fear.Water, "물", "물 찬 방"),
        new(Fear.Radiation, "방사선", "원자로실·방사선"),
        new(Fear.Disease, "병", "전염병"),
        new(Fear.Death, "죽음", "시신"),
        new(Fear.Failure, "실패", "제 실수"),
        new(Fear.Command, "책임", "지휘를 맡을 때"),
        new(Fear.Noise, "큰 소리", "폭발·충돌"),
        new(Fear.Electricity, "전기", "전기 일·감전"),
        new(Fear.Cold, "추위", "얼어붙는 방"),
        new(Fear.Gas, "독가스", "유독 가스"),
        new(Fear.Meteors, "운석", "운석 경보"),
        new(Fear.Machines, "기계", "로봇·드론"),
    };

    public static readonly string[] Quirks =
    {
        "말하자면, {0}", "{0} — 확실해", "솔직히 {0}", "{0}, 그렇지 않아?", "음… {0}", "아무튼 {0}", "{0}. 이상.", "내 말 들어 봐, {0}",
        "{0}, 진짜로", "어쨌든 {0}", "{0} …아마도", "괜찮아, 괜찮아. {0}", "원칙대로 하자. {0}", "{0}, 하하", "잠깐, {0}", "있잖아, {0}",
        "{0}. 끝.", "그러니까 말이야, {0}", "{0} …걱정되네", "확실한 건, {0}", "내 고향에선 말이지, {0}", "{0}, 안 그래?", "에이, {0}", "자, {0}",
        "들어 봐요, {0}", "제 생각엔 {0}", "{0} — 별수 없지", "기록해 둬, {0}", "{0}. 내가 장담해", "글쎄다, {0}",
    };

    public static BackgroundSpec Bg(Background b) => Backgrounds.FirstOrDefault(x => x.Id == b) ?? Backgrounds[0];
    private static HabitSpec[]? _habitIndex;
    public static HabitSpec Of(Habit h)
    {
        _habitIndex ??= Enum.GetValues<Habit>().Select(x => Habits.FirstOrDefault(s => s.Id == x) ?? Habits[0]).ToArray();
        return (int)h >= 0 && (int)h < _habitIndex.Length ? _habitIndex[(int)h] : Habits[0];
    }

    /// <summary>예전부터 있던 무거운 자격 (없으면 실수가 훨씬 잦다) — 새 자격은 조금만.</summary>
    public static bool Core(Qual q) => q is Qual.Reactor or Qual.Eva or Qual.Medic or Qual.Helm or Qual.Electrical or Qual.RescueTeam or Qual.Radiation;

    /// <summary>두 사람의 습관이 부딪히나 (가장 센 것).</summary>
    public static (float clash, string about)? Rival(CrewMember a, CrewMember b)
    {
        (float, string)? best = null;
        foreach (var (x, y) in new[] { (a, b), (b, a) })
            foreach (var h in x.Habits)
            {
                var s = Of(h);
                if (s.Rivals == null || !s.Rivals.Any(y.Habits.Contains)) continue;
                float k = h switch { Habit.NeatFreak => 0.9f, Habit.Talker => 0.5f, Habit.NightOwl => 0.6f, _ => 0.55f };
                if (best == null || k > best.Value.Item1) best = (k, s.About);
            }
        return best;
    }
    public static QualSpec Of(Qual q) => Quals.FirstOrDefault(x => x.Id == q) ?? Quals[0];
    public static HobbySpec Of(Hobby h) => Hobbies.First(x => x.Id == h);
    public static FearSpec Of(Fear f) => Fears.First(x => x.Id == f);

    /// <summary>습관 효과를 곱한 값 (여러 습관이면 모두 곱한다).</summary>
    public static float Mul(CrewMember c, Func<HabitSpec, float> pick)
    {
        float v = 1f;
        foreach (var h in c.Habits) v *= pick(Of(h));
        return v;
    }

    public static float Add(CrewMember c, Func<HabitSpec, float> pick)
    {
        float v = 0f;
        foreach (var h in c.Habits) v += pick(Of(h));
        return v;
    }

    /// <summary>말버릇을 입힌 한마디.</summary>
    public static string Say(CrewMember c, string text) => c.Quirk >= 0 && c.Quirk < Quirks.Length ? string.Format(Quirks[c.Quirk], text) : text;

    /// <summary>역할과 어울리는 경력들 (첫 출항 때 그중 하나가 잦다).</summary>
    public static Background[] Fits(CrewRole r) => r switch
    {
        CrewRole.Medic => new[] { Background.MedStudent, Background.Nurse, Background.Paramedic, Background.Veterinarian },
        CrewRole.Pilot => new[] { Background.CargoPilot, Background.Astronomer, Background.TruckDriver },
        CrewRole.Electrician => new[] { Background.Lineworker, Background.SysAdmin, Background.DroneRacer },
        CrewRole.Botanist => new[] { Background.FarmResearcher, Background.Gardener },
        CrewRole.Cook => new[] { Background.Chef, Background.Baker },
        CrewRole.Technician => new[] { Background.Miner, Background.Welder, Background.Plumber, Background.AutoMechanic, Background.Carpenter },
        CrewRole.Engineer => new[] { Background.MilitaryTech, Background.Physicist, Background.Chemist, Background.Roboticist },
        _ => Array.Empty<Background>(),
    };

    /// <summary>취미 · 두려움 · 말버릇 (본 난수와 따로).</summary>
    public static void Extras(World w, CrewMember c)
    {
        var rng = new Rng(unchecked(w.Seed * 9187 + c.Id * 7919 + 37));
        c.Hobbies.Clear();
        var lean = Bg(c.Background).Hobbies;
        var first = lean.Length > 0 && rng.Chance(0.6f) ? lean[rng.Range(0, lean.Length)] : (Hobby)rng.Range(0, Hobbies.Length);
        c.Hobbies.Add(first);
        if (rng.Chance(0.55f))
        {
            var second = (Hobby)rng.Range(0, Hobbies.Length);
            if (second != first) c.Hobbies.Add(second);
        }
        c.Fears.Clear();
        if (rng.Chance(0.7f)) c.Fears.Add((Fear)rng.Range(0, Fears.Length));
        if (rng.Chance(0.15f)) { var f2 = (Fear)rng.Range(0, Fears.Length); if (!c.Fears.Contains(f2)) c.Fears.Add(f2); }
        c.Quirk = rng.Range(0, Quirks.Length);
    }

    // ── 두려움이 걸리는 상황 ──

    /// <summary>지금 이 사람의 두려움이 건드려지나 (공황이 잦아진다).</summary>
    public static Fear? Triggered(World w, CrewMember c)
    {
        if (c.Fears.Count == 0) return null;
        var r = c.Room;
        foreach (var f in c.Fears)
        {
            bool hit = f switch
            {
                Fear.Dark => r is { Dark: true },
                Fear.Confined => r != null && r.Volume < 18f,
                Fear.Vacuum => r is { Leaking: true } || r != null && r.Air.Pressure < 70f,
                Fear.Fire => r != null && w.Fire.CountIn(r) > 0,
                Fear.Blood => w.Crew.Any(o => o != c && !o.Dead && o.Down && (o.Position - c.Position).LengthSquared() < 9f),
                Fear.Isolation => r != null && Atmosphere.Danger(r) > 0.2f && !w.Crew.Any(o => o != c && !o.Dead && o.Room == r),
                Fear.Spacewalk => c.Outside,
                Fear.Suffocation => r != null && r.Air.O2 < 17f && c.Suit == null,
                Fear.Water => r != null && MoistureSystem.Depth(r) > 0.08f,
                Fear.Radiation => r is { Type: RoomType.Reactor } && Crisis.Level(w) >= CrisisLevel.Alert || c.Dose > 0.3f,
                Fear.Disease => w.Crew.Any(o => o != c && !o.Dead && DiseaseSystem.Sick(o) && o.Room == r),
                Fear.Death => w.Crew.Any(o => o.Dead && o.Room == r && r != null),
                Fear.Command => w.Command.Active && w.Command.Commander == c,
                Fear.Noise => w.Volatile.Blasts.Any(b => w.Tick - b.Tick < SimTime.Minutes(10)) || w.Impacts.Any(i => w.Tick - i.Tick < SimTime.Minutes(10)),
                Fear.Electricity => r != null && MoistureSystem.Depth(r) > 0.05f && r.Powered,
                Fear.Cold => r != null && r.Air.Temperature < 10f,
                Fear.Gas => r != null && r.Air.Toxin > 0.1f,
                Fear.Meteors => w.Sensors.Alarm is IncomingMeteor m && m.MinutesLeft(w.Tick) > 0f,
                Fear.Machines => w.Robots.Robots.Any(b => (b.Position - c.Position).LengthSquared() < 4f),
                _ => false,
            };
            if (hit) return f;
        }
        return null;
    }

    /// <summary>당장 몸이 굳는 두려움 (아니면 위험할 때만 공황에 겹치고, 평소엔 스트레스만).</summary>
    public static bool Acute(Fear f) => f is not (Fear.Dark or Fear.Confined or Fear.Machines or Fear.Command or Fear.Disease or Fear.Failure or Fear.Isolation or Fear.Cold);

    /// <summary>두려움이 걸린 일 (꺼린다).</summary>
    public static Fear? FearOf(CrewMember c, WorkOrder o)
    {
        if (c.Fears.Count == 0) return null;
        var room = o.Target.CurrentRoom;
        foreach (var f in c.Fears)
        {
            bool hit = f switch
            {
                Fear.Spacewalk => o.External || o.Target.Outside,
                Fear.Fire => o.Kind == WorkKind.Extinguish,
                Fear.Blood => o.Kind is WorkKind.Treat or WorkKind.Rescue or WorkKind.RecoverBody,
                Fear.Death => o.Kind == WorkKind.RecoverBody,
                Fear.Electricity => o.Skill == Skill.Electrical && o.Urgency >= 0.5f,
                Fear.Radiation => room is { Type: RoomType.Reactor },
                Fear.Vacuum => room is { Leaking: true } || o.Kind == WorkKind.SealBreach,
                Fear.Dark => room is { Dark: true },
                Fear.Water => o.Kind == WorkKind.PumpOut,
                Fear.Machines => o.Kind is WorkKind.ServiceRobot or WorkKind.RepairRobot or WorkKind.ServiceDrone,
                Fear.Gas => room != null && room.Air.Toxin > 0.1f,
                Fear.Confined => room != null && room.Volume < 18f,
                _ => false,
            };
            if (hit) return f;
        }
        return null;
    }

    /// <summary>같은 취미가 있나.</summary>
    public static Hobby? Shared(CrewMember a, CrewMember b)
    {
        foreach (var h in a.Hobbies) if (b.Hobbies.Contains(h)) return h;
        return null;
    }

    /// <summary>이 방이 이 사람 취미의 방인가.</summary>
    public static Hobby? HobbyIn(CrewMember c, Room? r) =>
        r == null ? null : c.Hobbies.Cast<Hobby?>().FirstOrDefault(h => Of(h!.Value).Rooms.Contains(r.Type));

    public static string Line(CrewMember c) =>
        (c.Hobbies.Count > 0 ? "취미 " + string.Join("·", c.Hobbies.Select(h => Of(h).Name)) : "")
        + (c.Fears.Count > 0 ? " · 두려움 " + string.Join("·", c.Fears.Select(f => Of(f).Name)) : "");
}
