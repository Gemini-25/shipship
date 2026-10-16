using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.14 승무원이 스스로 꾸미는 일 — 표 (한 줄 = 한 가지. 줄만 더하면 새 일이 생긴다).
// 열: 열쇠 · 이름 · 동기(욕구 · 성격 · 처지) · 몇 명이서 · 준비 시간 · 얼마나 몰래 · 무엇으로 들키나 · 어디서 · 들키면/끝나면 · 그림(부품 조합 — 일마다 다르다) ·
//     꾸미는 사람의 속말(일기) · 정식이 되면 부르는 이름(또는 장난이 터진 장면) · 취미 · 조건 · 위험(불 · 고장 · 다침).

public enum SchemeCat : byte { Prank, Secret, Rule, Social, Trade, Politics, Personal }

[Flags]
public enum Drive : uint
{
    None = 0, Bored = 1, Stress = 2, Lonely = 4, Hungry = 8, Homesick = 16, Jest = 32, Free = 64, Craft = 128,
    Love = 256, Grief = 512, Greed = 1024, Grudge = 2048, Faith = 4096, Pride = 8192, Fear = 16384, Care = 32768,
}

[Flags]
public enum Tell : ushort { None = 0, Smell = 1, Noise = 2, Sight = 4, Ledger = 8, Sensor = 16, Power = 32, Talk = 64, Smoke = 128 }

public enum Crewing : byte { Solo, Pair, Group }

/// <summary>들키거나 무르익으면 어떻게 끝나나.</summary>
public enum Fate : byte
{
    Laugh,     // 장난 — 당한 사람이 웃어넘기거나 앙금이 남는다
    Vote,      // 회의에 올라 금지냐 정식이냐 표결
    Trial,     // 고발 → 재판
    Grievance, // 공개 경고 안건
    Adopt,     // 들키면 모두의 것이 된다 (또는 함장이 치우게 한다)
    Keep,      // 개인 일 — 눈감아 주거나 치우라고 한다
    Club,      // 모임이 몇 번 이어지면 배의 관행이 된다
    Event,     // 한 번 크게 연다 — 반응이 좋으면 정식 행사로
    Motion,    // 회의 · 서명 · 선거로 이어진다
    Debt,      // 빚이 쌓이고 다툼이 난다
}

public enum Place : byte { Bunk, Hidden, Engine, Garden, Galley, Mess, Lounge, Comms, Gym, Bridge, Medbay, Workshop, Airlock, Escape, Chapel, Observatory, Core, Corridor, Cargo, Lab }

public enum Need : byte { None, Loot, Death, Couple, Captain, Plenty, Partner, Victim }

public sealed record SchemeSpec(string Key, string Name, SchemeCat Cat, Fate Fate, Drive Drives, Crewing Team, float Hours, float Secrecy, Tell Tells, Place Place,
    string Art, string Plan, string Legit, Hobby[] Hobbies, Need Need, float Risk);

public static class SchemeTable
{
    private static SchemeSpec S(SchemeCat cat, Fate fate, string key, string name, Drive d, Place p, string art, string plan, string legit = "",
        Tell t = Tell.Sight, Crewing tm = Crewing.Solo, float h = 2f, float sec = 0.6f, float risk = 0f, Need need = Need.None, params Hobby[] hb) =>
        new(key, name, cat, fate, d, tm, h, sec, t, p, art, plan, legit, hb, need, risk);

    private static SchemeSpec Prank(string key, string name, Place p, string art, string plan, string scene, float h = 0.4f, Drive more = Drive.None, Tell t = Tell.Sight, Need need = Need.Victim) =>
        S(SchemeCat.Prank, Fate.Laugh, key, name, Drive.Jest | Drive.Bored | more, p, art, plan, scene, t, Crewing.Solo, h, 0.7f, 0f, need);

    private static SchemeSpec Club(string key, string name, Place p, string art, string plan, string legit, Drive d, params Hobby[] hb) =>
        S(SchemeCat.Social, Fate.Club, key, name, d | Drive.Lonely, p, art, plan, legit, Tell.Talk, Crewing.Group, 1f, 0.15f, 0f, Need.None, hb);

    private static SchemeSpec Party(string key, string name, Place p, string art, string plan, string legit, Drive d, Need need = Need.None, float sec = 0.2f, params Hobby[] hb) =>
        S(SchemeCat.Social, Fate.Event, key, name, d, p, art, plan, legit, Tell.Talk | Tell.Noise, Crewing.Group, 2f, sec, 0f, need, hb);

    public static readonly SchemeSpec[] All =
    {
        // ── 장난
        Prank("salt_sugar", "소금과 설탕 바꾸기", Place.Galley, "salt sugar spoon", "주방 소금통과 설탕통을 몰래 바꿔 두기로 했다", "국에서 단맛이 났다"),
        Prank("voice_swap", "컴퓨터 목소리 바꾸기", Place.Core, "speaker duck wave", "주 컴퓨터 안내 목소리를 오리 소리로 바꿔 두기로 했다", "안내 방송이 꽥꽥거렸다", 0.8f, Drive.Craft, Tell.Sensor, Need.None),
        Prank("cushion_trap", "의자에 방석 함정", Place.Mess, "cushion chair horn", "식당 의자에 소리 나는 방석을 깔아 두기로 했다", "앉자마자 요란한 소리가 났다"),
        Prank("fake_note", "함장이 찾는다는 가짜 쪽지", Place.Lounge, "memo pin star", "함장이 찾는다는 쪽지를 몰래 붙여 두기로 했다", "함장실까지 갔다가 헛걸음했다"),
        Prank("glove_balloons", "장갑 풍선 매달기", Place.Workshop, "glove balloon string", "작업대 위에 부풀린 고무장갑을 주렁주렁 매달기로 했다", "작업대가 장갑 풍선 숲이 됐다"),
        Prank("glued_boots", "작업화 바닥에 붙이기", Place.Airlock, "boots glue drip", "에어록 앞 작업화 밑창에 접착제를 바르기로 했다", "작업화가 바닥에서 떨어지지 않았다"),
        Prank("blue_tea", "찻주전자에 식용 색소", Place.Galley, "teapot dye drop", "찻주전자에 파란 색소를 몇 방울 넣기로 했다", "차가 새파랬다"),
        Prank("short_sheet", "침대 시트 반 접기", Place.Bunk, "sheet fold zz", "침대 시트를 반으로 접어 다리가 안 들어가게 하기로 했다", "다리가 시트 끝에 걸렸다"),
        Prank("rubber_spider", "수경 선반에 가짜 거미", Place.Garden, "spider leafy web", "수경 선반 잎 밑에 고무 거미를 숨기기로 했다", "잎을 들추다 비명을 질렀다"),
        Prank("locker_confetti", "사물함 색종이 폭탄", Place.Bunk, "locker confetti spring", "사물함 문을 열면 색종이가 쏟아지게 하기로 했다", "문을 열자 색종이가 쏟아졌다"),
        Prank("helmet_smile", "헬멧에 웃는 얼굴 스티커", Place.Airlock, "helmet smile", "헬멧 창에 웃는 얼굴 스티커를 붙이기로 했다", "헬멧 창에 웃는 얼굴이 붙어 있었다"),
        Prank("glued_cup", "컵을 식탁에 붙이기", Place.Mess, "cup glue tablet", "식탁에 컵 바닥을 붙여 두기로 했다", "컵이 들리지 않았다"),
        Prank("clock_ahead", "휴게실 시계 한 시간 당기기", Place.Lounge, "clock arrow", "휴게실 시계를 한 시간 당겨 두기로 했다", "한 시간 일찍 근무하러 나왔다"),
        Prank("rooster_alarm", "기상 알람을 닭 울음으로", Place.Bunk, "rooster speaker note", "기상 알람을 닭 울음소리로 바꿔 두기로 했다", "닭 울음소리에 벌떡 일어났다", 0.5f, Drive.Craft),
        Prank("giant_carrot", "화분에 가짜 거대 당근", Place.Garden, "carrot pot sparkle", "수경 화분에 커다란 가짜 당근을 꽂아 두기로 했다", "이만한 당근이 났다며 자랑하러 뛰어갔다"),
        Prank("foam_handle", "문손잡이에 면도 거품", Place.Corridor, "handle foam", "복도 문손잡이에 면도 거품을 발라 두기로 했다", "손이 거품 범벅이 됐다"),
        Prank("portrait_mustache", "함장 사진에 콧수염", Place.Bridge, "frame mustache", "함교에 걸린 함장 사진에 콧수염을 그려 넣기로 했다", "함장 사진에 콧수염이 그려져 있었다", 0.3f, Drive.Grudge, Tell.Sight, Need.Captain),

        // ── 몰래 하는 일
        S(SchemeCat.Secret, Fate.Vote, "moonshine", "밀주 담그기", Drive.Bored | Drive.Stress | Drive.Free | Drive.Craft, Place.Engine, "barrel coil drip",
            "남은 감자 껍질과 효모로 몰래 술을 담가 보기로 했다", "주점의 밤", Tell.Smell | Tell.Sight, Crewing.Pair, 6f, 0.8f, 0.25f),
        S(SchemeCat.Secret, Fate.Adopt, "secret_garden", "비밀 정원", Drive.Bored | Drive.Homesick | Drive.Stress, Place.Hidden, "pot leafy lamp",
            "창고 구석에 몰래 꽃을 키워 보기로 했다", "공용 정원", Tell.Sight | Tell.Power, Crewing.Solo, 6f, 0.6f, 0f, Need.None, Hobby.Gardening),
        S(SchemeCat.Secret, Fate.Vote, "pirate_radio", "선내 해적 방송국", Drive.Bored | Drive.Free | Drive.Lonely, Place.Comms, "antenna mic speaker",
            "밤마다 몰래 음악 방송을 내보내기로 했다", "정식 밤 방송", Tell.Sensor | Tell.Talk | Tell.Noise, Crewing.Pair, 5f, 0.5f, 0f, Need.None, Hobby.Music),
        S(SchemeCat.Secret, Fate.Keep, "robot_pet", "손수 만든 로봇 애완동물", Drive.Craft | Drive.Lonely, Place.Workshop, "robot wheel eyes",
            "부품 상자를 뒤져 작은 로봇 강아지를 만들기로 했다", "배의 로봇 강아지", Tell.Sight | Tell.Power, Crewing.Solo, 9f, 0.4f, 0f, Need.None, Hobby.Electronics),
        S(SchemeCat.Secret, Fate.Keep, "ship_model", "배 모형", Drive.Craft | Drive.Bored | Drive.Homesick, Place.Bunk, "model stand glue",
            "이 배를 손바닥만 하게 깎아 만들기로 했다", "휴게실 진열장의 배 모형", Tell.Sight, Crewing.Solo, 12f, 0.3f, 0f, Need.None, Hobby.ModelBuilding, Hobby.Woodwork),
        S(SchemeCat.Secret, Fate.Vote, "pet_mouse", "몰래 키우는 생쥐", Drive.Lonely | Drive.Stress, Place.Hidden, "cage mouse wheel",
            "보급품 상자에 딸려 온 생쥐를 몰래 키우기로 했다", "배의 마스코트 생쥐", Tell.Sight | Tell.Ledger, Crewing.Solo, 3f, 0.55f),
        S(SchemeCat.Secret, Fate.Vote, "kimchi_jar", "김치 항아리", Drive.Homesick | Drive.Hungry, Place.Galley, "jar cabbage stone",
            "고향 맛이 그리워 배추를 몰래 절이기로 했다", "발효 선반", Tell.Smell, Crewing.Solo, 6f, 0.6f, 0f, Need.None, Hobby.Cooking),
        S(SchemeCat.Secret, Fate.Keep, "flower_perfume", "꽃잎 향수", Drive.Bored | Drive.Love, Place.Garden, "flask petal drop",
            "수경 꽃잎을 모아 향수를 내려 보기로 했다", "", Tell.Sight, Crewing.Solo, 4f, 0.6f),
        S(SchemeCat.Secret, Fate.Vote, "engine_sauna", "기관실 사우나", Drive.Stress | Drive.Bored | Drive.Free, Place.Engine, "stones steam bench",
            "냉각수 배관 옆에 몰래 사우나를 꾸미기로 했다", "기관실 사우나의 날", Tell.Power | Tell.Sensor | Tell.Sight, Crewing.Group, 5f, 0.5f, 0.3f),
        S(SchemeCat.Secret, Fate.Adopt, "book_cache", "숨겨 둔 종이책 책장", Drive.Bored | Drive.Homesick, Place.Hidden, "shelf book lamp",
            "출항 때 몰래 들고 탄 종이책을 꽂을 책장을 짜기로 했다", "작은 도서관", Tell.Sight, Crewing.Solo, 4f, 0.5f, 0f, Need.None, Hobby.Reading),
        S(SchemeCat.Secret, Fate.Keep, "ant_farm", "개미 키우기", Drive.Bored | Drive.Craft, Place.Lab, "glass ant tunnel",
            "흙 시료 속 개미를 유리판 사이에 키우기로 했다", "", Tell.Sight, Crewing.Solo, 3f, 0.5f),
        S(SchemeCat.Secret, Fate.Keep, "herb_tea", "몰래 말리는 약초", Drive.Stress | Drive.Homesick, Place.Garden, "rack herb twine",
            "수경 허브를 몰래 말려 차를 만들기로 했다", "", Tell.Smell, Crewing.Solo, 5f, 0.6f, 0f, Need.None, Hobby.Tea),
        S(SchemeCat.Secret, Fate.Vote, "coffee_roast", "몰래 커피 볶기", Drive.Bored | Drive.Stress, Place.Galley, "pan bean smoke",
            "남은 생두를 몰래 볶아 보기로 했다", "아침 커피 볶는 날", Tell.Smoke, Crewing.Solo, 2f, 0.6f, 0.15f, Need.None, Hobby.Cooking),
        S(SchemeCat.Secret, Fate.Vote, "bunk_mushroom", "침대 밑 버섯", Drive.Hungry | Drive.Craft, Place.Bunk, "mushroom log damp",
            "침대 밑 상자에 버섯을 키우기로 했다", "버섯 선반", Tell.Smell, Crewing.Solo, 7f, 0.7f),
        S(SchemeCat.Secret, Fate.Vote, "fruit_wine", "과일주 항아리", Drive.Bored | Drive.Love | Drive.Stress, Place.Hidden, "jug fruit cloth",
            "남는 딸기로 과일주를 담그기로 했다", "과일주 한 잔의 날", Tell.Smell, Crewing.Pair, 8f, 0.65f, 0.1f),
        S(SchemeCat.Secret, Fate.Adopt, "time_capsule", "타임캡슐 숨기기", Drive.Homesick | Drive.Lonely, Place.Hidden, "capsule tag rope",
            "배가 닿는 날 열어 볼 상자를 숨기기로 했다", "모두의 타임캡슐", Tell.Sight, Crewing.Group, 2f, 0.7f),
        S(SchemeCat.Secret, Fate.Laugh, "ship_zine", "손으로 만든 익명 소식지", Drive.Jest | Drive.Grudge | Drive.Free, Place.Lounge, "paper staple pen",
            "배 소식을 비꼬는 익명 소식지를 몰래 찍어 돌리기로 했다", "함장을 비꼰 소식지가 식탁마다 놓여 있었다", Tell.Sight | Tell.Talk, Crewing.Solo, 3f, 0.7f, 0f, Need.Captain, Hobby.Writing),
        S(SchemeCat.Secret, Fate.Keep, "star_listen", "먼 별 잡음 엿듣기", Drive.Bored | Drive.Lonely, Place.Comms, "dish headset wave",
            "통신 안테나로 먼 별의 잡음을 몰래 녹음하기로 했다", "별 잡음 감상회", Tell.Sensor, Crewing.Solo, 3f, 0.5f, 0f, Need.None, Hobby.Stargazing),
        S(SchemeCat.Secret, Fate.Keep, "snack_drawer", "야식 비밀 서랍", Drive.Hungry | Drive.Stress, Place.Hidden, "drawer snack crumb",
            "근무 끝나고 먹을 간식을 몰래 쟁여 두기로 했다", "", Tell.Ledger | Tell.Smell, Crewing.Solo, 1f, 0.6f),

        // ── 규칙 어기기
        S(SchemeCat.Rule, Fate.Keep, "restricted_zone", "금지 구역 몰래 들어가기", Drive.Bored | Drive.Free | Drive.Craft, Place.Core, "sign lock footprint",
            "출입 금지 표시가 붙은 뒷방을 몰래 들여다보기로 했다", "", Tell.Sensor, Crewing.Solo, 0.5f, 0.6f, 0.15f),
        S(SchemeCat.Rule, Fate.Vote, "secret_smoke", "몰래 담배", Drive.Stress | Drive.Free, Place.Airlock, "cigarette smoke ash",
            "에어록 구석에서 몰래 담배를 피우기로 했다", "흡연 칸", Tell.Smoke | Tell.Sensor, Crewing.Solo, 0.5f, 0.55f, 0.3f),
        S(SchemeCat.Rule, Fate.Trial, "ration_skim", "배급 빼돌리기", Drive.Hungry | Drive.Fear, Place.Hidden, "crate ration hand",
            "창고 비상식량을 몰래 조금씩 빼 두기로 했다", "", Tell.Ledger, Crewing.Solo, 0.5f, 0.7f),
        S(SchemeCat.Rule, Fate.Trial, "contraband", "원정 밀수품 숨기기", Drive.Greed | Drive.Craft, Place.Bunk, "box ore cloth",
            "원정에서 가져온 광석을 보고하지 않고 숨기기로 했다", "", Tell.Sight | Tell.Ledger, Crewing.Solo, 0.5f, 0.65f, 0f, Need.Loot),
        S(SchemeCat.Rule, Fate.Vote, "overclock", "설비 몰래 개조해 출력 올리기", Drive.Craft | Drive.Pride | Drive.Free, Place.Engine, "gear bolt dial",
            "기관 출력 제한을 몰래 풀어 힘을 올리기로 했다", "정식 개조", Tell.Power | Tell.Sensor, Crewing.Pair, 4f, 0.5f, 0.35f),
        S(SchemeCat.Rule, Fate.Keep, "computer_tamper", "컴퓨터 몰래 손대기", Drive.Free | Drive.Stress | Drive.Grudge, Place.Core, "console wire chip",
            "주 컴퓨터 근무표를 몰래 고쳐 내 당직을 빼기로 했다", "", Tell.Sensor, Crewing.Solo, 2f, 0.6f, 0.05f),
        S(SchemeCat.Rule, Fate.Grievance, "nap_on_watch", "당직 중 몰래 눈 붙이기", Drive.Stress | Drive.Bored, Place.Bridge, "recliner pillow zz",
            "밤 당직 때 의자를 젖히고 몰래 자기로 했다", "", Tell.Sight | Tell.Sensor, Crewing.Solo, 0.5f, 0.5f, 0.1f),
        S(SchemeCat.Rule, Fate.Grievance, "skip_drill", "훈련 빼먹고 숨기", Drive.Free | Drive.Stress, Place.Hidden, "mat blanket clock",
            "비상 훈련 시간에 창고 뒤에 숨어 있기로 했다", "", Tell.Sight, Crewing.Solo, 0.3f, 0.6f),
        S(SchemeCat.Rule, Fate.Trial, "shuttle_joyride", "셔틀 몰래 시동", Drive.Bored | Drive.Pride | Drive.Free, Place.Escape, "shuttle key fuel",
            "셔틀에 몰래 시동을 걸어 보기로 했다", "", Tell.Sensor | Tell.Power, Crewing.Pair, 1.5f, 0.4f, 0.3f),
        S(SchemeCat.Rule, Fate.Grievance, "long_shower", "몰래 긴 샤워", Drive.Stress | Drive.Free, Place.Bunk, "shower steam drop2",
            "물을 아끼자는 말을 어기고 몰래 오래 씻기로 했다", "", Tell.Ledger, Crewing.Solo, 0.4f, 0.6f),
        S(SchemeCat.Rule, Fate.Trial, "painkiller_pilfer", "진통제 몰래 꺼내기", Drive.Stress | Drive.Fear, Place.Medbay, "pill bottle hand",
            "의무실 진통제를 몰래 꺼내 두기로 했다", "", Tell.Ledger, Crewing.Solo, 0.3f, 0.6f, 0.1f),
        S(SchemeCat.Rule, Fate.Grievance, "diary_peek", "남의 일기 몰래 읽기", Drive.Love | Drive.Grudge | Drive.Lonely, Place.Bunk, "book eye lock",
            "같은 방 사람의 일기장을 몰래 펼쳐 보기로 했다", "", Tell.Sight, Crewing.Solo, 0.3f, 0.6f, 0f, Need.Victim),
        S(SchemeCat.Rule, Fate.Trial, "parts_pilfer", "예비 부품 몰래 빼 쓰기", Drive.Craft | Drive.Free, Place.Workshop, "bin part pocket",
            "예비 부품을 장부에 안 적고 꺼내 쓰기로 했다", "", Tell.Ledger, Crewing.Solo, 0.5f, 0.6f),
        S(SchemeCat.Rule, Fate.Grievance, "fake_sick", "꾀병", Drive.Stress | Drive.Bored, Place.Medbay, "thermo blanket2",
            "몸이 안 좋다고 하고 하루 쉬기로 했다", "", Tell.Sight, Crewing.Solo, 0.5f, 0.5f),
        S(SchemeCat.Rule, Fate.Keep, "duct_graffiti", "환기 덕트 낙서", Drive.Free | Drive.Pride | Drive.Bored, Place.Corridor, "duct spray tagline",
            "환기 덕트 안쪽에 몰래 이름을 써 넣기로 했다", "", Tell.Sight, Crewing.Solo, 1f, 0.6f, 0f, Need.None, Hobby.Painting),
        S(SchemeCat.Rule, Fate.Trial, "eva_stargaze", "기록 없이 선외로 나가 별 보기", Drive.Free | Drive.Bored | Drive.Love, Place.Airlock, "suit tether star",
            "기록을 남기지 않고 선외로 나가 별을 보기로 했다", "", Tell.Sensor, Crewing.Pair, 1f, 0.5f, 0.4f, Need.None, Hobby.Stargazing),
        S(SchemeCat.Rule, Fate.Grievance, "coffee_hoard", "커피 몰래 쟁이기", Drive.Hungry | Drive.Greed, Place.Hidden, "tin bean lid",
            "커피 봉지를 몰래 쟁여 두기로 했다", "", Tell.Ledger, Crewing.Solo, 0.3f, 0.6f, 0f, Need.None, Hobby.Tea),

        // ── 어울리기
        Club("book_club", "독서 동호회", Place.Lounge, "book chair lamp2", "책 좋아하는 사람끼리 저녁마다 모여 읽기로 했다", "독서 동호회 모임", Drive.Bored, Hobby.Reading),
        Club("chess_club", "체스 동호회", Place.Lounge, "board clock2 pawn", "체스 두는 사람들을 모아 판을 벌이기로 했다", "체스 동호회 밤", Drive.Bored | Drive.Pride, Hobby.Chess),
        Club("knit_circle", "뜨개 모임", Place.Lounge, "yarn needles scarf", "실타래를 모아 같이 뜨개질하기로 했다", "뜨개 모임", Drive.Stress, Hobby.Knitting),
        Club("sports_league", "운동 리그", Place.Gym, "ball net trophy", "운동실에서 조를 짜 공차기 리그를 하기로 했다", "운동 리그 날", Drive.Pride | Drive.Bored, Hobby.Workout),
        Party("singing_contest", "노래 대회", Place.Mess, "mic note stage", "식당에서 노래 대회를 열기로 했다", "노래 대회 날", Drive.Pride | Drive.Lonely, Need.None, 0.2f, Hobby.Singing),
        Party("festival", "비공식 축제", Place.Mess, "lantern banner cake", "다들 지쳐 보여 몰래 축제를 준비하기로 했다", "배의 축제날", Drive.Bored | Drive.Stress | Drive.Lonely, Need.Plenty, 0.4f),
        Party("wedding", "결혼식", Place.Chapel, "arch ring flower", "둘이 배 안에서 식을 올리기로 했다", "", Drive.Love, Need.Couple, 0.2f),
        Party("new_memorial", "새 추모 의식", Place.Observatory, "candle photo starlight", "떠난 사람의 이름을 별에 대고 부르는 밤을 만들기로 했다", "별에 이름 부르는 밤", Drive.Grief, Need.Death, 0.1f),
        Club("meditation", "명상 모임", Place.Chapel, "mat bell incense", "조용한 방에서 아침마다 같이 숨을 고르기로 했다", "아침 명상", Drive.Faith | Drive.Stress, Hobby.Meditation, Hobby.Yoga),
        Club("star_night", "별 보는 밤", Place.Observatory, "scope blanket star2", "관측실 불을 끄고 별을 보는 모임을 만들기로 했다", "별 보는 밤", Drive.Bored | Drive.Homesick, Hobby.Stargazing),
        Party("dance_night", "춤추는 밤", Place.Lounge, "disco note feet", "휴게실을 치우고 춤추는 밤을 열기로 했다", "춤추는 밤", Drive.Bored | Drive.Lonely, Need.None, 0.2f, Hobby.Dancing),
        Party("potluck", "각자 한 접시", Place.Mess, "plate bowl ladle", "다 같이 한 접시씩 만들어 와 나눠 먹기로 했다", "한 접시 저녁", Drive.Hungry | Drive.Lonely, Need.Plenty, 0.2f, Hobby.Cooking),
        Club("movie_club", "영화 동호회", Place.Lounge, "screen reel popcorn", "보관함 영화를 같이 보는 모임을 만들기로 했다", "영화의 밤", Drive.Bored, Hobby.Movies),
        Party("ghost_story", "괴담 이야기 밤", Place.Lounge, "torch ghost blanket", "불을 끄고 무서운 이야기를 돌려 하기로 했다", "", Drive.Bored | Drive.Jest, Need.None, 0.3f, Hobby.Storytelling),
        Party("surprise_birthday", "깜짝 생일 잔치", Place.Mess, "cake candle2 gift", "생일인 사람 몰래 잔치를 준비하기로 했다", "", Drive.Care | Drive.Lonely, Need.Victim, 0.7f),
        Club("ship_band", "선내 밴드", Place.Lounge, "guitar drum amp", "악기 다루는 사람끼리 밴드를 짜기로 했다", "밴드 연습 날", Drive.Pride | Drive.Bored, Hobby.Instrument, Hobby.Music),
        Club("language_class", "고향 말 배우기 모임", Place.Lounge, "chalk letter globe", "서로 고향 말을 가르쳐 주는 모임을 만들기로 했다", "고향 말 모임", Drive.Homesick | Drive.Lonely, Hobby.Writing),
        Party("cooking_duel", "요리 대결", Place.Galley, "wok flame medal", "누구 요리가 나은지 대결을 붙이기로 했다", "요리 대결의 날", Drive.Pride | Drive.Hungry, Need.Plenty, 0.2f, Hobby.Cooking, Hobby.Baking),
        Party("talent_show", "장기 자랑", Place.Lounge, "curtain spot hat", "저마다 숨은 재주를 보여 주는 밤을 열기로 했다", "장기 자랑의 밤", Drive.Pride | Drive.Bored),
        Party("pillow_fort", "베개 성 쌓기", Place.Lounge, "pillow fort pennant", "휴게실 베개를 다 모아 성을 쌓기로 했다", "", Drive.Bored | Drive.Jest),
        Club("tea_circle", "차 마시는 모임", Place.Lounge, "teapot2 cups leaf", "오후마다 차를 우려 같이 마시기로 했다", "오후 차 모임", Drive.Stress | Drive.Homesick, Hobby.Tea),
        Club("puzzle_night", "퍼즐 맞추기 밤", Place.Mess, "puzzle box2 lamp3", "큰 퍼즐 판을 식당에 펼쳐 놓고 같이 맞추기로 했다", "퍼즐의 밤", Drive.Bored, Hobby.Puzzles),
        Club("run_club", "복도 달리기 모임", Place.Corridor, "shoe stopwatch lane", "복도를 한 바퀴씩 같이 달리기로 했다", "아침 달리기", Drive.Pride | Drive.Stress, Hobby.Walking, Hobby.Workout),
        Club("photo_club", "사진 동호회", Place.Observatory, "camera flash frame2", "배 안 구석구석을 찍어 돌려 보는 모임을 만들기로 했다", "사진 모임", Drive.Bored, Hobby.Photography),
        Party("comedy_night", "만담의 밤", Place.Lounge, "mic2 laugh stool", "돌아가며 웃긴 이야기를 하는 밤을 열기로 했다", "만담의 밤", Drive.Jest | Drive.Bored),

        // ── 경제
        S(SchemeCat.Trade, Fate.Vote, "gambling_den", "도박판", Drive.Greed | Drive.Bored | Drive.Stress, Place.Hidden, "cards dice chips",
            "창고 구석에서 밤마다 판을 벌이기로 했다", "카드의 밤", Tell.Noise | Tell.Talk, Crewing.Group, 1f, 0.55f, 0f, Need.None, Hobby.Cards, Hobby.Games),
        S(SchemeCat.Trade, Fate.Trial, "black_market", "암시장", Drive.Greed | Drive.Hungry, Place.Cargo, "crate2 balance coin",
            "화물칸에서 물건을 몰래 사고팔기로 했다", "", Tell.Ledger | Tell.Talk, Crewing.Group, 1.5f, 0.6f),
        S(SchemeCat.Trade, Fate.Club, "barter_fair", "물물교환 장터", Drive.Greed | Drive.Lonely, Place.Mess, "rug swap tag",
            "안 쓰는 물건을 들고 나와 바꾸는 장터를 열기로 했다", "물물교환 장날", Tell.Talk, Crewing.Group, 1f, 0.15f, 0f, Need.None, Hobby.Collecting),
        S(SchemeCat.Trade, Fate.Vote, "betting_pool", "다음 고장 맞히기 내기", Drive.Greed | Drive.Bored, Place.Lounge, "chalkboard wrench coin2",
            "다음에 어느 설비가 고장 날지 내기를 걸기로 했다", "고장 맞히기 판", Tell.Talk, Crewing.Group, 0.5f, 0.4f),
        S(SchemeCat.Trade, Fate.Vote, "chocolate_money", "초콜릿 화폐", Drive.Greed | Drive.Hungry, Place.Mess, "bars stamp ledger2",
            "초콜릿 조각을 돈처럼 주고받기로 했다", "초콜릿 쿠폰", Tell.Ledger | Tell.Talk, Crewing.Group, 1f, 0.5f),
        S(SchemeCat.Trade, Fate.Vote, "shift_market", "근무 바꿔 주기 거래", Drive.Greed | Drive.Stress, Place.Lounge, "corkboard pin swap2",
            "간식을 받고 근무를 대신 서 주는 거래를 하기로 했다", "근무 교환 게시판", Tell.Talk | Tell.Sight, Crewing.Group, 0.5f, 0.4f),
        Party("ship_lottery", "선내 복권", Place.Mess, "drumcage ticket star3", "간식을 걸고 선내 복권을 돌리기로 했다", "", Drive.Greed | Drive.Bored),
        S(SchemeCat.Trade, Fate.Debt, "loan_book", "빌려주고 이자 받기", Drive.Greed, Place.Bunk, "ledger coin3 pen2",
            "궁한 사람에게 간식을 빌려주고 이자를 받기로 했다", "", Tell.Talk, Crewing.Pair, 0.5f, 0.6f),
        S(SchemeCat.Trade, Fate.Adopt, "repair_stall", "몰래 차린 수리 가게", Drive.Craft | Drive.Greed, Place.Workshop, "bench tag kettle",
            "고장 난 개인 물건을 간식 받고 고쳐 주기로 했다", "수리 가게", Tell.Sight, Crewing.Solo, 2f, 0.4f, 0f, Need.None, Hobby.Electronics),

        // ── 정치
        S(SchemeCat.Politics, Fate.Motion, "petition", "청원서 돌리기", Drive.Grudge | Drive.Stress | Drive.Free, Place.Mess, "clipboard pen3 sig",
            "쉬는 시간을 지켜 달라는 청원서를 돌리기로 했다", "", Tell.Talk, Crewing.Solo, 0.5f, 0.1f),
        S(SchemeCat.Politics, Fate.Motion, "strike", "파업", Drive.Grudge | Drive.Stress, Place.Mess, "placard fist chairs",
            "다 같이 일손을 놓고 식당에 앉아 있기로 했다", "", Tell.Sight | Tell.Talk, Crewing.Group, 1f, 0.3f),
        S(SchemeCat.Politics, Fate.Motion, "slowdown", "태업", Drive.Grudge | Drive.Stress, Place.Workshop, "snail clock3 tool",
            "일은 하되 아주 천천히 하기로 했다", "", Tell.Sight | Tell.Ledger, Crewing.Group, 1f, 0.6f),
        S(SchemeCat.Politics, Fate.Motion, "no_confidence", "함장 불신임", Drive.Grudge, Place.Lounge, "ballot cap cross",
            "함장을 바꾸자는 말을 몰래 모으기로 했다", "", Tell.Talk, Crewing.Group, 1f, 0.5f, 0f, Need.Captain),
        S(SchemeCat.Politics, Fate.Trial, "mutiny_plot", "반란 모의", Drive.Grudge | Drive.Fear | Drive.Free, Place.Hidden, "map candle3 key",
            "함교 열쇠를 손에 넣을 궁리를 몰래 하기로 했다", "", Tell.Talk | Tell.Sight, Crewing.Group, 4f, 0.8f, 0.2f, Need.Captain),
        S(SchemeCat.Politics, Fate.Club, "crew_union", "근무자 모임 결성", Drive.Grudge | Drive.Lonely, Place.Mess, "banner2 hands badge",
            "근무하는 사람끼리 모여 목소리를 내기로 했다", "근무자 모임", Tell.Talk, Crewing.Group, 1f, 0.3f),
        S(SchemeCat.Politics, Fate.Adopt, "suggestion_box", "익명 건의함", Drive.Grudge | Drive.Free, Place.Corridor, "box3 slot paper2",
            "아무나 말을 넣을 수 있는 건의함을 몰래 달기로 했다", "건의함", Tell.Sight, Crewing.Solo, 1f, 0.5f),
        S(SchemeCat.Politics, Fate.Motion, "log_demand", "컴퓨터 기록 공개 요구", Drive.Grudge | Drive.Free, Place.Core, "monitor scroll eye2",
            "주 컴퓨터가 본 기록을 다 보여 달라고 하기로 했다", "", Tell.Talk, Crewing.Solo, 0.5f, 0.2f),
        S(SchemeCat.Politics, Fate.Motion, "election_posters", "선거 벽보", Drive.Pride | Drive.Grudge, Place.Corridor, "poster face star4",
            "복도마다 내 이름을 건 벽보를 붙이기로 했다", "", Tell.Sight, Crewing.Group, 1f, 0.2f, 0f, Need.Captain),

        // ── 개인
        S(SchemeCat.Personal, Fate.Keep, "room_decor", "방 꾸미기", Drive.Homesick | Drive.Bored, Place.Bunk, "rug2 lamp4 poster2",
            "침실을 내 방처럼 꾸미기로 했다", "", Tell.Sight, Crewing.Solo, 3f, 0.2f),
        S(SchemeCat.Personal, Fate.Adopt, "mural", "벽화", Drive.Bored | Drive.Pride | Drive.Homesick, Place.Corridor, "paint brush ladder",
            "복도 벽에 고향 바다를 그리기로 했다", "모두의 벽화", Tell.Sight, Crewing.Solo, 9f, 0.3f, 0f, Need.None, Hobby.Painting),
        S(SchemeCat.Personal, Fate.Keep, "bunk_shelf", "침대 위 선반 개조", Drive.Craft, Place.Bunk, "shelf2 bracket bolt2",
            "침대 머리 위에 선반을 달아 내 물건을 올리기로 했다", "", Tell.Sight, Crewing.Solo, 2f, 0.3f, 0.1f, Need.None, Hobby.Woodwork),
        S(SchemeCat.Personal, Fate.Keep, "secret_romance", "몰래 연애", Drive.Love, Place.Hidden, "heart note2 rose",
            "남들 몰래 둘이 만나기로 했다", "", Tell.Sight | Tell.Talk, Crewing.Pair, 0.5f, 0.7f, 0f, Need.Partner),
        S(SchemeCat.Personal, Fate.Keep, "call_home", "고향에 몰래 무전", Drive.Homesick | Drive.Grief, Place.Comms, "handset photo2 wave2",
            "장거리 무전기로 고향에 몰래 말을 보내기로 했다", "", Tell.Sensor, Crewing.Solo, 0.5f, 0.6f),
        S(SchemeCat.Personal, Fate.Trial, "escape_pod", "탈출 포드로 도망 시도", Drive.Fear | Drive.Stress | Drive.Homesick, Place.Escape, "pod suitcase beacon",
            "탈출 포드에 짐을 실어 두고 떠날 날을 노리기로 했다", "", Tell.Sensor | Tell.Sight, Crewing.Solo, 3f, 0.6f, 0.5f),
        S(SchemeCat.Personal, Fate.Keep, "tattoo", "바늘 문신", Drive.Pride | Drive.Love | Drive.Free, Place.Bunk, "needle ink star5",
            "바늘과 잉크로 서로 문신을 새겨 주기로 했다", "", Tell.Sight, Crewing.Pair, 1.5f, 0.5f, 0.1f),
        S(SchemeCat.Personal, Fate.Keep, "hair_dye", "머리 염색", Drive.Bored | Drive.Jest, Place.Bunk, "dye comb towel",
            "남은 식용 색소로 머리를 물들이기로 했다", "", Tell.Sight, Crewing.Solo, 0.5f, 0.3f),
        S(SchemeCat.Personal, Fate.Keep, "home_shrine", "고향 흙 작은 제단", Drive.Homesick | Drive.Grief | Drive.Faith, Place.Bunk, "soil candle4 photo3",
            "고향에서 가져온 흙으로 작은 제단을 만들기로 했다", "", Tell.Sight, Crewing.Solo, 0.5f, 0.4f),
        S(SchemeCat.Personal, Fate.Adopt, "star_chart", "손으로 그린 별자리 지도", Drive.Bored | Drive.Homesick, Place.Observatory, "map2 star6 compass",
            "관측실 창으로 본 별을 손으로 옮겨 그리기로 했다", "관측실 별 지도", Tell.Sight, Crewing.Solo, 5f, 0.3f, 0f, Need.None, Hobby.Stargazing),
        S(SchemeCat.Personal, Fate.Keep, "secret_training", "몰래 몸 만들기", Drive.Pride, Place.Gym, "dumbbell chart towel2",
            "남들 잘 때 몰래 운동해 몸을 만들기로 했다", "", Tell.Sight, Crewing.Solo, 5f, 0.4f, 0f, Need.None, Hobby.Workout),
        S(SchemeCat.Personal, Fate.Keep, "letter_stack", "고향에 못 부친 편지 묶음", Drive.Homesick | Drive.Love, Place.Bunk, "letter ribbon stamp",
            "닿지도 않을 편지를 날마다 써서 묶어 두기로 했다", "", Tell.Sight, Crewing.Solo, 2f, 0.7f, 0f, Need.None, Hobby.Writing),
        S(SchemeCat.Personal, Fate.Adopt, "compose_song", "몰래 쓰는 노래", Drive.Pride | Drive.Homesick, Place.Bunk, "sheet2 note3 pencil",
            "이 배의 노래를 몰래 지어 보기로 했다", "배의 노래", Tell.Sight | Tell.Noise, Crewing.Solo, 5f, 0.6f, 0f, Need.None, Hobby.Music, Hobby.Instrument),
        S(SchemeCat.Personal, Fate.Adopt, "poem_wall", "복도에 시 붙이기", Drive.Lonely | Drive.Homesick, Place.Corridor, "paper3 poem tape",
            "아무도 모르게 복도 벽에 시를 한 장씩 붙이기로 했다", "시 게시판", Tell.Sight, Crewing.Solo, 0.5f, 0.5f, 0f, Need.None, Hobby.Writing),
        S(SchemeCat.Personal, Fate.Keep, "bunk_fort", "침대 커튼 아지트", Drive.Lonely | Drive.Stress, Place.Bunk, "curtain2 fairy pillow2",
            "침대에 커튼을 치고 작은 불을 달아 나만의 굴을 만들기로 했다", "", Tell.Sight, Crewing.Solo, 1.5f, 0.3f),
        S(SchemeCat.Personal, Fate.Keep, "photo_wall", "가족 사진 벽", Drive.Homesick, Place.Bunk, "frame3 frame4 twine2",
            "가족 사진을 침대 옆 벽에 줄줄이 붙이기로 했다", "", Tell.Sight, Crewing.Solo, 1f, 0.2f, 0f, Need.None, Hobby.Photography),
    };

    private static Dictionary<string, SchemeSpec>? _byKey;
    public static SchemeSpec? Get(string key) => (_byKey ??= All.ToDictionary(s => s.Key)).GetValueOrDefault(key);

    public static string CatName(SchemeCat c) => c switch
    {
        SchemeCat.Prank => "장난", SchemeCat.Secret => "몰래 하는 일", SchemeCat.Rule => "규칙 어기기", SchemeCat.Social => "어울리기",
        SchemeCat.Trade => "주고받기", SchemeCat.Politics => "목소리 내기", _ => "혼자 하는 일",
    };

    /// <summary>그림 부품 (화면이 부품마다 다른 모양을 그린다 — 표에 새 부품을 쓰면 화면에도 더한다).</summary>
    public static IEnumerable<string> Parts(SchemeSpec s) => s.Art.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
