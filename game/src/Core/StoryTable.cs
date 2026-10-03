using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.16 개인 이야기의 틀 (데이터 표). 틀 + 조각: 글 속 {kin} {home} {friend} {rival} {room} {ship} {sum} {prev} 자리에
//   그 사람(가족 · 고향) · 관계(털어놓은 사람 · 앙숙) · 장소 · 배 상태가 끼워져 사람마다 다른 이야기가 된다. 조사는 {kin:이} 처럼.
// 단계를 여는 것(Gate): 시간 · 장소(그 방에 가 본다) · 털어놓기(가까운 사람 — 대화 카드) · 배가 다침 · 마음이 무거워짐 · 다툼 · 누군가의 죽음.
// 끝: 해결 · 실패(→ 새 갈래 틀) · 비극. 결말은 가까운 사람의 이야기를 밀거나 새 이야기를 연다 (Story.cs).

public enum ArcTheme : byte { Family, Debt, Guilt, Illness, Revenge, Dream, Secret, Faith, Love }
public enum ArcGate : byte { Time, Place, Confide, Hurt, Strain, Quarrel, Loss }
public enum ArcEnd : byte { None, Resolved, Failed, Tragic }

/// <summary>이야기의 한 단계: 글 · 다음으로 넘어가는 문 · 최소 시간 · 장소 · 그동안 마음이 쏠리는 일.</summary>
public sealed record ArcStep(string Text, ArcGate Gate, float Hours, RoomType[]? Rooms = null, ActCat Act = ActCat.None);

public sealed record ArcSpec(string Key, string Name, ArcTheme Theme, string Emblem, Skill? Skill, Func<CrewMember, float> Fit,
    ArcStep[] Steps, string Win, string Lose, string Ruin, string Branch = "", bool Seed = true);

public static class StoryTable
{
    public static string ThemeName(ArcTheme t) => t switch
    {
        ArcTheme.Family => "가족", ArcTheme.Debt => "빚", ArcTheme.Guilt => "죄책감", ArcTheme.Illness => "숨긴 병", ArcTheme.Revenge => "복수",
        ArcTheme.Dream => "꿈", ArcTheme.Secret => "비밀", ArcTheme.Faith => "믿음", _ => "사랑",
    };

    public static string EndName(ArcEnd e) => e switch { ArcEnd.Resolved => "풀렸다", ArcEnd.Failed => "어긋났다", ArcEnd.Tragic => "무너졌다", _ => "이어지는 중" };

    private static ArcStep T(string t, float h, ActCat a = ActCat.None) => new(t, ArcGate.Time, h, null, a);
    private static ArcStep Pl(string t, float h, params RoomType[] rooms) => new(t, ArcGate.Place, h, rooms, ActCat.Hobby);
    private static ArcStep Cf(string t, float h = 4f) => new(t, ArcGate.Confide, h, null, ActCat.Social);
    private static ArcStep Hu(string t, float h = 6f) => new(t, ArcGate.Hurt, h);
    private static ArcStep St(string t, float h = 4f) => new(t, ArcGate.Strain, h);
    private static ArcStep Qu(string t, float h = 4f) => new(t, ArcGate.Quarrel, h);
    private static ArcStep Lo(string t, float h = 8f) => new(t, ArcGate.Loss, h);

    private static bool Bg(CrewMember c, params Background[] b) => Array.IndexOf(b, c.Background) >= 0;
    private static bool Hb(CrewMember c, params Hobby[] h) => h.Any(c.Hobbies.Contains);
    private static bool Hab(CrewMember c, Habit h) => c.Habits.Contains(h);

    private static ArcSpec A(string key, string name, ArcTheme th, string emblem, Skill? sk, Func<CrewMember, float> fit, ArcStep[] steps,
        string win, string lose, string ruin, string branch = "", bool seed = true) => new(key, name, th, emblem, sk, fit, steps, win, lose, ruin, branch, seed);

    public static readonly ArcSpec[] All = Build();
    private static Dictionary<string, ArcSpec>? _by;
    public static ArcSpec? Get(string key) => (_by ??= All.ToDictionary(s => s.Key))!.GetValueOrDefault(key);

    private static ArcSpec[] Build()
    {
        var r = new List<ArcSpec>
        {
            // ── 가족
            A("sick_parent", "아픈 {kin}", ArcTheme.Family, "letter", null, c => 1f + (Hab(c, Habit.Homesick) ? 2f : 0f), new[]
            {
                T("고향 {home}에서 {kin:이} 쓰러졌다는 짧은 편지가 왔다", 5f),
                Pl("근무가 끝나면 통신실 앞을 서성인다 — 답장이 늦다", 6f, RoomType.Comms, RoomType.Bridge, RoomType.Navigation),
                Cf("{friend}에게 {kin} 이야기를 털어놓았다"),
                St("{ship} — 돌아갈 길이 없다는 게 밤마다 사무친다"),
            }, "{kin:이} 일어나 앉았다는 답장이 왔다 — 제일 먼저 {friend}에게 보여 줬다", "답장이 끊겼다 — 소식을 모르는 채로 항해가 이어진다",
               "{kin}의 부고가 석 달 늦게 도착했다", "unsent_letter"),
            A("sibling_wedding", "{kin}의 결혼식", ArcTheme.Family, "ring", null, c => c.Age < 40f ? 1.5f : 0.6f, new[]
            {
                T("{kin}의 청첩장이 메신저로 왔다 — 식은 다음 달 {home}에서", 4f),
                Pl("휴게실 화면 앞에서 축하 영상을 몇 번이고 다시 찍는다", 6f, RoomType.Lounge, RoomType.Theater, RoomType.Comms),
                Cf("{friend:이} 영상 속 웃음이 어색하다고 솔직하게 말해 줬다"),
                St("전송 대기열이 길다 — 식 날짜 안에 닿을지 모르겠다"),
            }, "식장 화면에 내 영상이 나왔다는 사진이 왔다 — {kin:이} 울었다고", "영상이 대기열에서 밀려 식이 끝난 뒤에야 닿았다",
               "{kin:이} 왜 안 왔냐는 원망의 편지를 보냈다", "family_rift"),
            A("child_growing", "자라는 {kin}", ArcTheme.Family, "drawing", null, c => c.Age > 30f ? 1.6f : 0.3f, new[]
            {
                T("{kin:이} 그린 배 그림이 사진으로 왔다 — 창문이 열두 개다", 5f),
                Pl("침대 옆에 그림을 붙이고 잠들기 전에 한참 본다", 4f, RoomType.Quarters, RoomType.PrivateCabins, RoomType.QuietQuarters),
                Hu("배가 흔들린 날, 다시 못 볼까 봐 겁이 났다"),
                Cf("{friend}에게 {kin} 사진을 보여 주며 웃었다"),
            }, "{kin}의 졸업 사진이 왔다 — '우리 집 사람이 탄 배'를 발표했단다", "그림이 뜸해졌다 — 아이가 바빠진 모양이다",
               "{kin:이} 영상 통화에서 내 얼굴을 낯설어했다", "family_rift"),
            A("family_rift", "{kin:와}의 틀어진 사이", ArcTheme.Family, "torn_photo", null, c => 0.4f, new[]
            {
                T("{prev} — {kin:와}는 그 뒤로 말을 안 한다", 6f),
                St("메신저 창을 열었다 닫았다 한다"),
                Cf("{friend:이} '먼저 쓰는 쪽이 이기는 거야'라고 했다"),
            }, "짧은 사과 편지를 보냈고, 더 짧은 답이 왔다 — 그걸로 됐다", "보낼 말을 끝내 고르지 못했다", "{kin:이} 연락처를 지웠다는 걸 알았다"),
            A("unsent_letter", "부치지 못한 편지", ArcTheme.Family, "envelope", null, c => 0.3f, new[]
            {
                T("{prev} — 쓰다 만 편지가 서랍에 쌓인다", 6f),
                Pl("창가에 앉아 편지를 소리 내 읽어 본다", 4f, RoomType.Observatory, RoomType.Lounge, RoomType.Garden),
                Cf("{friend:이} 편지를 같이 읽어 줬다"),
            }, "편지를 부쳤다 — 닿든 말든 마음이 가벼워졌다", "편지는 서랍에 그대로다", "편지를 태워 버렸다", seed: false),

            // ── 빚
            A("gambling_debt", "도박 빚", ArcTheme.Debt, "chips", null, c => 0.6f + (Hab(c, Habit.Daredevil) ? 1.5f : 0f) + (Hb(c, Hobby.Cards) ? 1f : 0f), new[]
            {
                T("출항 전에 진 빚 {sum}이 아직 남았다 — 독촉 편지가 왔다", 4f),
                T("남의 당직까지 맡아 수당을 모은다", 10f, ActCat.Work),
                Cf("{friend}에게 돈 이야기를 꺼냈다"),
                St("{rival:이} 빚 이야기를 듣고 수군거린다는 걸 알았다"),
            }, "마지막 몫을 부쳤다 — {friend}에게 빌린 것도 갚았다", "갚는 날짜를 또 미뤘다", "빚쟁이가 고향의 {kin}에게 찾아갔다", "debt_collector"),
            A("shop_debt", "{home}의 가게 빚", ArcTheme.Debt, "ledger", Skill.Cooking, c => Bg(c, Background.Chef, Background.Baker, Background.Accountant) ? 2f : 0.6f, new[]
            {
                T("{kin:이} 하던 {home}의 가게가 빚에 넘어가게 생겼다", 5f),
                T("수당 계산을 몇 번이고 다시 한다 — 근무를 늘렸다", 8f, ActCat.Work),
                Cf("{friend}에게 장부를 보여 주며 한숨을 쉬었다"),
                St("이자가 또 올랐다"),
            }, "밀린 몫을 한 번에 보냈다 — 가게 간판이 다시 켜졌다는 사진이 왔다", "가게는 반만 지켰다", "가게 문이 닫혔다는 소식이 왔다", "debt_collector"),
            A("debt_collector", "빚쟁이의 그림자", ArcTheme.Debt, "stamp", null, c => 0.3f, new[]
            {
                T("{prev} — 빚쟁이의 편지가 배 주소로 오기 시작했다", 5f),
                St("편지 봉투만 봐도 손이 차가워진다"),
                Cf("{friend:이} 기항지에서 같이 가 주겠다고 했다"),
            }, "기항지에서 나눠 갚기로 도장을 찍었다", "편지를 뜯지 않고 쌓아 둔다", "{ship} 회사에 압류 통지가 왔다", seed: false),

            // ── 죄책감
            A("mine_collapse", "무너진 갱도", ArcTheme.Guilt, "helmet", null, c => Bg(c, Background.Miner, Background.Climber, Background.Welder) ? 3f : 0.2f, new[]
            {
                Pl("{room}의 낮은 천장 아래서 숨이 막혔다 — 옛 갱도가 떠올랐다", 3f, RoomType.Storage, RoomType.Cargo, RoomType.PumpRoom, RoomType.Engine),
                Hu("배가 흔들린 날 손이 멈추지 않았다"),
                Cf("그날 두고 나온 동료의 이름을 {friend} 앞에서 처음으로 말했다"),
                St("또 그날 꿈을 꿨다"),
            }, "{friend}의 말에 처음으로 그날 일을 내려놓았다", "아직 그 이름을 부르면 목이 멘다", "사고 현장에서 또 얼어붙었다", "frozen_again"),
            A("lost_patient", "놓친 환자", ArcTheme.Guilt, "pulse", Skill.Medicine, c => c.Role == CrewRole.Medic || Bg(c, Background.MedStudent, Background.Nurse, Background.Paramedic) ? 3f : 0.1f, new[]
            {
                T("의무실 침대 시트를 갈다가 그 환자가 떠올랐다", 4f),
                Lo("배에서 사람을 잃었다 — 이번에도 늦었다는 생각이 떠나지 않는다"),
                Cf("{friend}에게 그날 기록을 처음으로 보여 줬다"),
                Pl("빈 의무실에서 혼자 처치 순서를 연습한다", 4f, RoomType.Medbay, RoomType.Triage),
            }, "연습한 손이 다음 환자를 붙잡았다", "그 기록은 다시 서랍에 들어갔다", "손이 굳어 처치를 남에게 넘겼다", "frozen_again"),
            A("my_fire", "내가 낸 불", ArcTheme.Guilt, "match", Skill.Electrical, c => c.Role is CrewRole.Engineer or CrewRole.Electrician || Bg(c, Background.Firefighter) ? 2f : 0.3f, new[]
            {
                T("지난 배에서 내 배선 실수로 불이 났었다 — 아무도 모른다", 5f),
                Pl("{room}의 배선함을 몇 번이고 다시 연다", 4f, RoomType.Power, RoomType.Substation, RoomType.BatteryRoom, RoomType.Engine),
                Hu("불 냄새를 맡자 그날로 돌아갔다"),
                Cf("{friend}에게 그 일을 털어놨다 — 처음이다"),
            }, "점검표를 만들어 배에 붙였다 — 이제 남의 실수도 막는다", "점검만 늘었다 — 잠은 줄었다", "확인하느라 정작 급한 순간에 늦었다", "frozen_again"),
            A("survivor_guilt", "알아챘어야 했다", ArcTheme.Guilt, "candle", null, c => 0.2f, new[]
            {
                T("{prev} — 그 사람이 힘든 줄 알면서 지나쳤다", 4f),
                Pl("그 사람이 앉던 자리에 혼자 앉아 본다", 4f, RoomType.Mess, RoomType.Lounge, RoomType.Chapel),
                Cf("{friend:이} '네 탓이 아니야'라고 했다"),
            }, "그 사람 몫까지 남을 챙기기로 했다", "그 자리를 피해 다닌다", "잠을 못 이루는 날이 길어졌다", seed: false),
            A("frozen_again", "다시 얼어붙은 날", ArcTheme.Guilt, "ice", null, c => 0.2f, new[]
            {
                T("{prev} — 다들 괜찮다고 하지만 눈을 못 마주치겠다", 5f),
                Cf("{friend:이} 훈련을 같이 해 주겠다고 했다"),
                Hu("다시 경보가 울렸다 — 이번엔 움직일 수 있을까"),
            }, "이번엔 발이 먼저 움직였다", "아직 경보 소리에 숨이 멎는다", "근무에서 빼 달라고 했다", seed: false),

            // ── 숨긴 병
            A("hidden_tremor", "숨긴 손 떨림", ArcTheme.Illness, "pills", Skill.Medicine, c => c.Age > 34f ? 1.5f : 0.4f, new[]
            {
                T("손이 떨리는 걸 처음 알았다 — 아무에게도 말하지 않았다", 4f),
                Pl("몰래 의무실에 들러 약을 챙긴다", 4f, RoomType.Medbay, RoomType.Triage),
                St("떨리는 손으로 나사를 떨어뜨렸다 — {rival:이} 봤다"),
                Cf("{friend}에게만 병 이야기를 했다"),
            }, "검사해 보니 다스릴 수 있는 병이었다 — 약이 맞았다", "약을 늘렸다 — 아직 아무도 모른다", "작업 중 손이 미끄러져 크게 다쳤다", "frozen_again"),
            A("failing_eyes", "흐려지는 눈", ArcTheme.Illness, "glasses", Skill.Piloting, c => c.Role == CrewRole.Pilot ? 2.5f : 0.4f, new[]
            {
                T("계기판 숫자가 번져 보인다 — 눈을 비볐다", 4f),
                Pl("밤에 몰래 함교에 와서 계기를 읽어 본다", 4f, RoomType.Bridge, RoomType.Navigation, RoomType.BackupBridge),
                Cf("{friend}에게 읽어 달라고 부탁했다 — 이유는 말하지 않았다"),
                St("조종석 자리를 뺏길까 겁난다"),
            }, "의무실에서 렌즈를 깎아 줬다 — 다시 선명하다", "글씨를 크게 바꿔 버틴다", "착각해 항로를 잘못 넣을 뻔했다"),

            // ── 복수
            A("corp_revenge", "{kin:을} 잃게 한 회사", ArcTheme.Revenge, "badge", Skill.Engineering, c => 0.5f + (c.Value == CrewValue.Freedom ? 1f : 0f), new[]
            {
                T("이 배의 주인이 {kin:을} 잃게 한 그 회사라는 걸 알았다", 5f),
                Pl("밤마다 배의 옛 기록을 뒤진다", 5f, RoomType.ComputerRoom, RoomType.Archive, RoomType.ServerRoom, RoomType.Bridge),
                Qu("회사 이야기만 나오면 언성이 높아진다"),
                Cf("{friend}에게 모은 기록을 보여 줬다"),
            }, "모은 기록을 고향 기자에게 보냈다 — 답이 왔다", "기록은 모았지만 보낼 곳이 없다", "기록을 빼내려다 들켜 근무에서 빠졌다", "revenge_regret"),
            A("old_betrayer", "배신한 옛 동료", ArcTheme.Revenge, "scar", null, c => 0.6f + (Hab(c, Habit.ShortTempered) ? 1f : 0f), new[]
            {
                T("{rival:을} 보자마자 알아봤다 — 그 일의 장본인이다", 4f),
                Qu("{rival:와} 같은 방에 있을 때마다 공기가 얼어붙는다"),
                Cf("{friend}에게 {rival:와}의 옛일을 털어놓았다"),
                St("{rival}의 웃음소리가 귀에 박힌다"),
            }, "{rival}에게서 그날 일에 대한 사과를 받아 냈다", "아무 말도 못 하고 근무를 바꿨다", "{rival}에게 주먹을 휘둘렀다", "revenge_regret"),
            A("revenge_regret", "복수의 뒷맛", ArcTheme.Revenge, "ash", null, c => 0.2f, new[]
            {
                T("{prev} — 시원할 줄 알았는데 속이 비었다", 6f),
                Cf("{friend:이} 말없이 차를 끓여 줬다"),
                St("거울 속 얼굴이 낯설다"),
            }, "그 일을 내려놓고 내 할 일로 돌아왔다", "아직도 그 이름을 들으면 주먹이 쥐어진다", "배에서 가장 외로운 사람이 됐다", seed: false),

            // ── 꿈
            A("name_a_star", "별에 이름 붙이기", ArcTheme.Dream, "star", Skill.Piloting, c => Bg(c, Background.Astronomer, Background.Physicist) || Hb(c, Hobby.Stargazing) ? 3f : 0.3f, new[]
            {
                Pl("관측 창에서 목록에 없는 희미한 별을 봤다", 4f, RoomType.Observatory, RoomType.Bridge, RoomType.Navigation),
                T("밤마다 같은 자리를 찍어 비교한다", 8f, ActCat.Hobby),
                Cf("{friend}에게 찍은 사진을 보여 줬다 — '{kin}의 이름을 붙이고 싶어'"),
                Hu("배가 흔들려 망원경 초점이 다 날아갔다"),
            }, "새 별이 정식으로 {kin}의 이름을 얻었다", "확인해 보니 이미 이름이 있는 별이었다", "관측 기록이 정전에 통째로 날아갔다", "unsent_letter"),
            A("write_book", "항해 일기로 책 쓰기", ArcTheme.Dream, "book", null, c => Bg(c, Background.Writer, Background.Reporter, Background.Teacher) || Hb(c, Hobby.Writing, Hobby.Journaling) ? 3f : 0.3f, new[]
            {
                T("이 항해를 책으로 남기기로 했다 — 첫 줄을 썼다", 5f, ActCat.Hobby),
                Pl("조용한 방에서 밤늦게 원고를 고친다", 5f, RoomType.Archive, RoomType.QuietQuarters, RoomType.Quarters, RoomType.Lounge),
                Cf("{friend}에게 첫 장을 읽어 줬다"),
                St("{ship} 이야기를 써도 되는지 망설여진다"),
            }, "원고를 다 썼다 — 식당에서 한 장씩 돌려 읽었다", "원고가 서랍 속에서 멈췄다", "원고 파일이 지워졌다 — 백업도 없었다", "unsent_letter"),
            A("finish_song", "끝내지 못한 노래", ArcTheme.Dream, "note", null, c => Bg(c, Background.Musician, Background.Artist) || Hb(c, Hobby.Music, Hobby.Instrument, Hobby.Singing) ? 3f : 0.2f, new[]
            {
                T("{kin:이} 시작하고 못 끝낸 노래가 떠올랐다", 4f),
                Pl("{room} 구석에서 가락을 흥얼거린다", 4f, RoomType.Lounge, RoomType.Theater, RoomType.Garden, RoomType.Gym),
                Cf("{friend:이} 후렴을 같이 불러 줬다"),
                St("마지막 마디가 안 나온다"),
            }, "식당에서 처음으로 끝까지 불렀다 — 다들 따라 불렀다", "노래는 아직 후렴에서 멈춰 있다", "목이 상해 노래를 접었다"),
            A("home_garden", "고향의 꽃", ArcTheme.Dream, "seed", Skill.Botany, c => c.Role == CrewRole.Botanist || Bg(c, Background.Gardener, Background.FarmResearcher) || Hb(c, Hobby.Gardening) ? 3f : 0.3f, new[]
            {
                T("{home}에서 가져온 씨앗 봉투를 찾았다 — 아직 살아 있을까", 4f),
                Pl("재배실 한쪽에 화분을 놓고 날마다 들여다본다", 6f, RoomType.Hydroponics, RoomType.Garden, RoomType.SeedVault),
                Hu("배가 흔들려 화분이 쓰러졌다"),
                Cf("{friend}에게 첫 싹을 보여 줬다"),
            }, "{home}의 꽃이 배에서 처음 피었다", "싹은 났지만 꽃은 아직이다", "씨앗이 다 말라 버렸다"),
            A("open_restaurant", "작은 식당의 꿈", ArcTheme.Dream, "ladle", Skill.Cooking, c => c.Role == CrewRole.Cook || Bg(c, Background.Chef, Background.Baker) || Hb(c, Hobby.Cooking, Hobby.Baking) ? 3f : 0.3f, new[]
            {
                T("내리면 {home}에 작은 식당을 내겠다고 공책에 적었다", 4f),
                Pl("주방에서 고향 음식을 몰래 시험해 본다", 5f, RoomType.Galley, RoomType.Mess),
                Cf("{friend}에게 첫 접시를 먹여 봤다"),
                St("재료가 모자라 맛이 안 난다"),
            }, "식당 이름을 정했다 — {friend}의 이름을 한 글자 땄다", "공책 속 차림표만 늘었다", "모아 둔 돈을 빚 갚는 데 다 썼다", "debt_collector"),

            // ── 비밀
            A("fake_license", "가짜 자격증", ArcTheme.Secret, "card", null, c => 0.5f + (c.Quals.Count > 2 ? 1f : 0f), new[]
            {
                T("내 자격증 하나는 돈 주고 산 것이다 — 아무도 모른다", 5f),
                Pl("밤에 몰래 교본을 펴고 공부한다", 6f, RoomType.School, RoomType.Archive, RoomType.Quarters, RoomType.Lounge),
                St("{rival:이} 내 자격 이야기를 꺼냈다 — 등에 식은땀이 흘렀다"),
                Cf("{friend}에게만 사실을 말했다"),
            }, "시험을 다시 봐서 진짜 자격을 땄다", "공부는 하지만 아직 털어놓지 못했다", "회의에서 자격 이야기가 터졌다", "frozen_again"),
            A("other_name", "다른 이름", ArcTheme.Secret, "mask", null, c => 0.5f, new[]
            {
                T("이 배에서 쓰는 이름은 내 진짜 이름이 아니다", 5f),
                St("기항지 명단 이야기가 나올 때마다 숨이 멎는다"),
                Cf("{friend}에게 진짜 이름을 알려 줬다"),
                Hu("사고 기록에 내 옛 이름이 뜰까 봐 겁이 났다"),
            }, "진짜 이름으로 서명했다 — 아무 일도 일어나지 않았다", "이름은 그대로 묻어 두기로 했다", "옛 이름을 아는 사람이 기항지에서 탔다", "family_rift"),

            // ── 믿음
            A("faith_shaken", "흔들리는 믿음", ArcTheme.Faith, "beads", null, c => Bg(c, Background.Monk) || Hab(c, Habit.Superstitious) ? 2.5f : 0.4f, new[]
            {
                T("밤 기도가 자꾸 끊긴다 — 뭘 빌어야 할지 모르겠다", 5f),
                Lo("누가 죽었다 — 왜 그 사람이었는지 묻게 된다"),
                Pl("기도실에 혼자 오래 앉아 있다", 4f, RoomType.Chapel, RoomType.Meditation, RoomType.Observatory),
                Cf("{friend:와} 밤새 믿음 이야기를 했다"),
            }, "다른 모양으로 다시 기도하게 됐다", "기도는 멈췄지만 기도실엔 여전히 간다", "묵주를 창밖으로 던져 버렸다", "survivor_guilt"),

            // ── 사랑 (지난 사랑 · 끝난 사랑)
            A("old_flame", "옛 연인의 편지", ArcTheme.Love, "pressed_flower", null, c => 0.6f, new[]
            {
                T("{home}에 두고 온 사람에게서 편지가 왔다 — 결혼한다고", 4f),
                St("편지를 몇 번이고 다시 읽는다"),
                Cf("{friend}에게 그 사람 이야기를 처음 했다"),
            }, "축하한다고 답장했다 — 진심이었다", "답장을 쓰다 말았다", "편지를 찢고 일주일 말이 없었다", "unsent_letter"),
            A("heartbreak", "끝난 사랑", ArcTheme.Love, "broken_cup", null, c => 0.1f, new[]
            {
                T("{prev} — 같은 식당에서 마주치는 게 괴롭다", 5f),
                Pl("식사 시간을 피해 혼자 늦게 먹는다", 4f, RoomType.Galley, RoomType.Mess),
                Cf("{friend:이} 밤늦게 이야기를 들어 줬다"),
            }, "웃으며 인사할 수 있게 됐다", "아직 그 사람 근무 시간은 피한다", "근무를 다른 조로 바꿔 달라고 했다", seed: false),
        };
        return r.ToArray();
    }

    // ───────────────────────────── 조각 끼우기 ─────────────────────────────

    /// <summary>{slot} · {slot:조사} 자리를 채운다 (조사: 이/가 · 은/는 · 을/를 · 와/과 · 로/으로).</summary>
    public static string Fill(string text, Func<string, string> slot)
    {
        if (text.IndexOf('{') < 0) return text;
        var sb = new System.Text.StringBuilder(text.Length + 16);
        int i = 0;
        while (i < text.Length)
        {
            int a = text.IndexOf('{', i);
            if (a < 0) { sb.Append(text, i, text.Length - i); break; }
            int b = text.IndexOf('}', a);
            if (b < 0) { sb.Append(text, i, text.Length - i); break; }
            sb.Append(text, i, a - i);
            string inner = text.Substring(a + 1, b - a - 1);
            int colon = inner.IndexOf(':');
            string key = colon < 0 ? inner : inner[..colon], josa = colon < 0 ? "" : inner[(colon + 1)..];
            string v = slot(key);
            sb.Append(josa switch
            {
                "이" or "가" => Ko.IGa(v), "은" or "는" => Ko.EunNeun(v), "을" or "를" => Ko.EulReul(v),
                "와" or "과" => Ko.WaGwa(v), "로" or "으로" => Ko.EuRo(v), _ => v,
            });
            i = b + 1;
        }
        return sb.ToString();
    }
}
