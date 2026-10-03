using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v17.9 숨은 것 · 드문 것 · 도감 — 표. 항목마다 고유한 그림(작은 벡터 그림 · 단위 상자 -1~1)을 표에 함께 적는다.
// 그림 문법: "P x,y x,y …" 채운 다각형 · "p …" 테두리만 · "L …" 꺾은선 · "O x,y,r" 채운 원 · "o x,y,r" 고리 · "A x,y,r,도0,도1" 호 — ';'로 잇는다.

public enum CurioKind : byte { Hidden, Graffiti, Trace, Anomaly, Passing, Talent, Secret }
/// <summary>도감 칸: 사진(창밖 · 이상한 일) · 표본 · 유물(물건 · 낙서 · 흔적) · 사람(재능 · 비밀).</summary>
public enum CodexShelf : byte { Photo, Specimen, Relic, People }
/// <summary>재능이 드러나는 때.</summary>
public enum TalentCue : byte { None, Music, Meal, Night, Repair, Crisis, Bored, Window }

public sealed record CurioSpec(string Key, CurioKind Kind, CodexShelf Shelf, string Name, string Line, float Rarity, RoomType[] Where, string Color, string Art,
    TalentCue Cue = TalentCue.None, int Polarity = 0);

public static class CurioTable
{
    private static readonly RoomType[] Any = Array.Empty<RoomType>();
    private static RoomType[] In(params RoomType[] r) => r;

    public static readonly CurioSpec[] All =
    {
        // ── 숨은 물건 (유물 · 표본) ──
        new("music_box", CurioKind.Hidden, CodexShelf.Relic, "태엽 오르골", "침대 밑 상자에서 태엽 오르골이 나왔다 — 감으니 아직 돈다", 0.8f, In(RoomType.Quarters, RoomType.Lounge), "#d9a066",
            "P -.8,-.1 .8,-.1 .8,.7 -.8,.7;P -.85,-.35 .85,-.35 .8,-.1 -.8,-.1;L .8,.2 1,.2 1,.4;O 0,.3,.15;L -.5,.05 .5,.05"),
        new("old_photo", CurioKind.Hidden, CodexShelf.Relic, "빛바랜 사진", "선반 뒤에서 빛바랜 사진 한 장 — 모르는 두 사람이 웃고 있다", 1f, In(RoomType.Quarters, RoomType.Storage), "#c8b48a",
            "p -.8,-.7 .8,-.7 .8,.7 -.8,.7;O -.3,-.15,.18;P -.5,.45 -.1,.45 -.3,.05;O .3,-.2,.18;P .1,.45 .5,.45 .3,0"),
        new("brass_key", CurioKind.Hidden, CodexShelf.Relic, "놋쇠 열쇠", "어느 문에도 맞지 않는 놋쇠 열쇠를 찾았다", 0.9f, In(RoomType.Storage, RoomType.Cargo, RoomType.Workshop), "#e0b64a",
            "o -.55,0,.3;O -.55,0,.12;P -.25,-.07 .85,-.07 .85,.07 -.25,.07;P .5,.07 .6,.07 .6,.3 .5,.3;P .7,.07 .8,.07 .8,.22 .7,.22"),
        new("tin_toy", CurioKind.Hidden, CodexShelf.Relic, "양철 로봇 장난감", "환기구 안에서 양철 로봇 장난감이 굴러 나왔다", 0.7f, In(RoomType.Quarters, RoomType.Lounge, RoomType.HvacRoom), "#9fb3c8",
            "P -.35,-.75 .35,-.75 .35,-.25 -.35,-.25;P -.45,-.2 .45,-.2 .45,.45 -.45,.45;P -.4,.45 -.15,.45 -.15,.85 -.4,.85;P .15,.45 .4,.45 .4,.85 .15,.85;L 0,-.75 0,-.95;O 0,-.95,.06;O -.15,-.5,.07;O .15,-.5,.07"),
        new("cassette", CurioKind.Hidden, CodexShelf.Relic, "손글씨 녹음 테이프", "통신 콘솔 서랍에 손글씨 이름표가 붙은 녹음 테이프가 있었다", 0.6f, In(RoomType.Comms, RoomType.Lounge), "#b58cff",
            "P -.9,-.55 .9,-.55 .9,.55 -.9,.55;o -.4,-.05,.2;o .4,-.05,.2;P -.5,.3 .5,.3 .4,.55 -.4,.55;L -.6,-.4 .6,-.4"),
        new("coin", CurioKind.Hidden, CodexShelf.Relic, "옛 행성 동전", "바닥 틈에서 이제는 안 쓰는 행성 동전이 반짝였다", 1f, In(RoomType.Storage, RoomType.Cargo, RoomType.Mess), "#f2c66d",
            "O 0,0,.75;o 0,0,.6;P -.2,-.3 .2,-.3 .25,.3 -.25,.3;L -.35,0 .35,0"),
        new("pressed_leaf", CurioKind.Hidden, CodexShelf.Specimen, "책갈피 속 마른 잎", "낡은 책 사이에 눌러 말린 잎이 끼어 있었다 — 이 배에선 자라지 않는 잎이다", 0.8f, In(RoomType.Hydroponics, RoomType.Quarters, RoomType.Lounge), "#8fd65a",
            "P 0,-.9 .45,-.4 .5,.2 0,.75 -.5,.2 -.45,-.4;L 0,-.8 0,.95;L 0,-.2 .35,-.45;L 0,.1 -.35,-.15;L 0,.35 .3,.15"),
        new("seed_pouch", CurioKind.Hidden, CodexShelf.Specimen, "이름 없는 씨앗 주머니", "재배대 밑에서 이름 없는 씨앗 주머니를 찾았다 — 심어 볼까", 0.7f, In(RoomType.Hydroponics, RoomType.SeedVault), "#c9a66b",
            "P -.55,-.3 .55,-.3 .7,.6 0,.85 -.7,.6;P -.3,-.55 .3,-.55 .2,-.3 -.2,-.3;L -.35,-.3 .35,-.3;O -.2,.25,.08;O .15,.35,.08;O 0,.05,.08"),
        new("meteor_chip", CurioKind.Hidden, CodexShelf.Specimen, "선체에 박힌 운석 조각", "에어락 문틀에 박혀 있던 운석 조각을 빼냈다", 0.6f, In(RoomType.Airlock, RoomType.EvaPrep), "#a08a7a",
            "P -.6,-.3 -.2,-.75 .45,-.55 .75,0 .4,.6 -.3,.65 -.75,.2;O -.2,-.1,.12;O .25,.15,.09;O -.05,.35,.07"),
        new("feather", CurioKind.Hidden, CodexShelf.Specimen, "새 깃털", "옷장 구석에서 새 깃털 하나 — 누가 여기까지 가져왔을까", 0.5f, In(RoomType.Quarters), "#e6eaf2",
            "P 0,-.95 .3,-.5 .35,.1 .1,.6 0,.7 -.15,.5 -.3,0 -.25,-.5;L 0,-.9 .05,.95;L .02,-.5 .3,-.65;L .03,-.1 .33,-.25;L 0,.2 -.28,0"),
        new("shell", CurioKind.Hidden, CodexShelf.Specimen, "바다 고둥 껍데기", "휴게실 화분 흙 속에서 바다 고둥 껍데기가 나왔다", 0.5f, In(RoomType.Lounge, RoomType.Hydroponics), "#f0a6a0",
            "P -.75,.5 -.6,-.2 -.2,-.7 .3,-.75 .7,-.4 .75,.2 .4,.6 -.1,.7;A .1,-.1,.3,0,300;A .15,-.05,.15,0,300"),
        new("crystal", CurioKind.Hidden, CodexShelf.Specimen, "냉각관 결정", "식은 냉각관 이음매에 소금 결정이 꽃처럼 자라 있었다", 0.6f, In(RoomType.Cooling, RoomType.PumpRoom, RoomType.WaterPlant), "#7fdfff",
            "P 0,-.95 .3,-.4 .25,.7 -.25,.7 -.3,-.4;P .3,-.4 .7,-.6 .6,.3 .25,.5;P -.3,-.4 -.65,-.2 -.55,.6 -.25,.6;L 0,-.95 0,.7"),
        new("compass", CurioKind.Hidden, CodexShelf.Relic, "옛 나침반", "조종실 의자 밑에서 바늘 달린 옛 나침반이 나왔다 — 여기선 쓸모없지만 예쁘다", 0.6f, In(RoomType.Bridge, RoomType.Observatory), "#d4c27a",
            "o 0,0,.8;P 0,-.65 .15,0 0,.65 -.15,0;O 0,0,.08;L 0,-.95 0,-.8;L .8,0 .95,0"),
        new("pocket_watch", CurioKind.Hidden, CodexShelf.Relic, "멈춘 회중시계", "공구함 바닥에 멈춘 회중시계가 있었다 — 바늘은 어느 날의 새벽을 가리킨다", 0.5f, In(RoomType.Engine, RoomType.Workshop), "#c0a060",
            "O 0,.1,.7;o 0,.1,.55;L 0,.1 0,-.3;L 0,.1 .3,.25;P -.12,-.8 .12,-.8 .12,-.6 -.12,-.6;o 0,-.9,.12"),
        // ── 낙서 ──
        new("tally", CurioKind.Graffiti, CodexShelf.Relic, "날짜를 센 줄", "벽에 날짜를 센 줄이 새겨져 있다 — 누군가 여기서 오래 기다렸다", 1f, In(RoomType.Quarters, RoomType.Storage, RoomType.Shelter), "#9aa3b5",
            "L -.7,-.6 -.7,.6;L -.4,-.6 -.4,.6;L -.1,-.6 -.1,.6;L .2,-.6 .2,.6;L -.85,.45 .35,-.45;L .55,-.6 .55,.6;L .8,-.6 .8,.6"),
        new("heart_initials", CurioKind.Graffiti, CodexShelf.Relic, "하트 속 머리글자", "식탁 밑에 하트와 두 사람의 머리글자가 새겨져 있었다", 0.8f, In(RoomType.Mess, RoomType.Lounge), "#ff8fa3",
            "P 0,.75 -.75,-.05 -.7,-.5 -.4,-.7 0,-.4 .4,-.7 .7,-.5 .75,-.05;L -.35,-.25 -.35,.15;L -.45,-.25 -.25,-.25;L .2,-.25 .4,-.25 .2,.15 .4,.15"),
        new("star_map", CurioKind.Graffiti, CodexShelf.Relic, "손으로 그린 별자리", "천장 패널 안쪽에 손으로 그린 별자리 — 지금 창밖 하늘과 맞는다", 0.6f, In(RoomType.Observatory, RoomType.Bridge, RoomType.Quarters), "#a0c4ff",
            "O -.6,-.5,.08;O -.1,-.65,.08;O .3,-.2,.1;O .65,-.5,.07;O .1,.4,.09;O -.5,.5,.07;L -.6,-.5 -.1,-.65 .3,-.2 .65,-.5;L .3,-.2 .1,.4 -.5,.5"),
        new("ship_doodle", CurioKind.Graffiti, CodexShelf.Relic, "이 배를 그린 낙서", "작업대 옆판에 이 배를 그린 낙서가 있다 — 지금과 방 배치가 조금 다르다", 0.7f, In(RoomType.Workshop, RoomType.Engine), "#c9a66b",
            "P -.85,-.2 .55,-.2 .9,0 .55,.2 -.85,.2;P -.6,-.2 -.3,-.2 -.45,-.55;P -.6,.2 -.3,.2 -.45,.55;L -.85,-.1 -1,-.1;L -.85,.1 -1,.1"),
        new("poem_line", CurioKind.Graffiti, CodexShelf.Relic, "벽에 적힌 시 한 줄", "벽 모서리에 시 한 줄 — '별은 멀어도 손은 가깝다'", 0.5f, In(RoomType.Chapel, RoomType.Lounge, RoomType.Quarters), "#e6eaf2",
            "L -.85,-.4 .7,-.4;L -.85,-.1 .4,-.1;L -.85,.2 .8,.2;L -.85,.5 .1,.5;o .55,.5,.12"),
        new("warning_skull", CurioKind.Graffiti, CodexShelf.Relic, "'여기 조심' 해골", "배관 덮개에 해골과 '여기 조심'이 긁혀 있다 — 무엇을 조심하라는 걸까", 0.6f, In(RoomType.Reactor, RoomType.Cooling, RoomType.Engine), "#ff9a6b",
            "P -.5,-.6 .5,-.6 .65,-.1 .4,.3 -.4,.3 -.65,-.1;O -.25,-.2,.14;O .25,-.2,.14;P -.25,.3 .25,.3 .2,.6 -.2,.6;L -.6,.75 .6,.95;L .6,.75 -.6,.95"),
        // ── 전 승무원의 흔적 ──
        new("name_tag", CurioKind.Trace, CodexShelf.Relic, "전 승무원 이름표", "사물함 안쪽에 예전 승무원의 이름표가 붙어 있었다", 1f, In(RoomType.Quarters, RoomType.EvaPrep), "#7cc4ff",
            "P -.85,-.4 .85,-.4 .85,.4 -.85,.4;o -.65,-.2,.06;L -.4,-.1 .6,-.1;L -.4,.15 .3,.15;P -.85,-.4 -.6,-.4 -.6,.4 -.85,.4"),
        new("mug_chipped", CurioKind.Trace, CodexShelf.Relic, "이 빠진 머그잔", "찬장 맨 안쪽에 이름이 적힌 이 빠진 머그잔 — 아무도 그 이름을 모른다", 0.9f, In(RoomType.Mess, RoomType.Galley), "#ef8f5a",
            "P -.5,-.6 .3,-.6 .25,.7 -.45,.7;A .35,.05,.3,-90,90;P -.1,-.6 .05,-.6 -.02,-.45"),
        new("boots", CurioKind.Trace, CodexShelf.Relic, "닳은 작업화 한 켤레", "선외복 보관함 밑에 뒤꿈치가 닳은 작업화가 있다 — 발이 꽤 컸다", 0.8f, In(RoomType.EvaPrep, RoomType.Airlock, RoomType.Storage), "#8d7a64",
            "P -.85,-.7 -.45,-.7 -.45,.4 -.05,.45 -.05,.75 -.85,.75;P .1,-.6 .5,-.6 .5,.4 .9,.45 .9,.75 .1,.75;L -.85,.55 -.05,.55;L .1,.55 .9,.55"),
        new("logbook", CurioKind.Trace, CodexShelf.Relic, "전 기관장의 손 일지", "기관실 선반 뒤에서 손으로 쓴 일지가 나왔다 — 이 배의 버릇이 적혀 있다", 0.6f, In(RoomType.Engine, RoomType.Workshop, RoomType.Reactor), "#c0a0ff",
            "P -.7,-.8 .7,-.8 .7,.8 -.7,.8;P -.7,-.8 -.5,-.8 -.5,.8 -.7,.8;L -.3,-.45 .5,-.45;L -.3,-.2 .5,-.2;L -.3,.05 .3,.05;P .2,.8 .35,.8 .35,1 .275,.9 .2,1"),
        new("child_drawing", CurioKind.Trace, CodexShelf.Relic, "아이 그림", "침대 판자 뒤에 아이가 그린 집과 해 그림이 붙어 있었다", 0.7f, In(RoomType.Quarters, RoomType.Lounge), "#ffd166",
            "p -.85,-.75 .85,-.75 .85,.75 -.85,.75;O .5,-.4,.17;L -.6,.6 -.6,.1 -.35,-.15 -.1,.1 -.1,.6;P -.6,.1 -.35,-.4 -.1,.1;L .2,.6 .2,.25;O .2,.15,.12"),
        new("repair_note", CurioKind.Trace, CodexShelf.Relic, "패널 뒤 수리 메모", "분전반 덮개 안쪽에 '세 번째 단자는 손으로 눌러야 붙는다' 메모", 0.7f, In(RoomType.Power, RoomType.Substation, RoomType.BatteryRoom), "#f5d547",
            "P -.7,-.7 .6,-.7 .75,-.55 .75,.75 -.7,.75;L -.5,-.4 .5,-.4;L -.5,-.15 .3,-.15;L -.3,.2 .2,.45 .55,.05;O -.45,.35,.08"),
        // ── 아주 드문 이상 현상 (사진) ──
        new("floating_drops", CurioKind.Anomaly, CodexShelf.Photo, "떠오르는 물방울", "눅눅한 방에서 물방울이 바닥에서 떨어져 위로 떠올랐다", 0.6f, Any, "#5ec8e6",
            "O -.4,.4,.15;O .1,0,.18;O .45,-.45,.13;O -.2,-.5,.1;A .1,0,.3,200,250;A -.4,.4,.27,200,250"),
        new("lone_door", CurioKind.Anomaly, CodexShelf.Photo, "혼자 열린 문", "아무도 없는데 문이 스르륵 열렸다 닫혔다", 0.7f, Any, "#aab3c5",
            "p -.6,-.85 .6,-.85 .6,.85 -.6,.85;P -.55,-.8 .1,-.65 .1,.7 -.55,.8;O 0,0,.06;L .25,-.6 .25,.6"),
        new("empty_steps", CurioKind.Anomaly, CodexShelf.Photo, "빈 복도의 발소리", "한밤 빈 복도에서 또각또각 발소리가 지나갔다", 0.6f, In(RoomType.Corridor), "#9aa3b5",
            "P -.6,.3 -.35,.25 -.3,.55 -.55,.65;P .05,-.1 .3,-.15 .35,.15 .1,.25;P .45,-.6 .7,-.65 .75,-.35 .5,-.25;O -.45,.15,.05;O .2,-.25,.05"),
        new("frost_fern", CurioKind.Anomaly, CodexShelf.Photo, "서리 고사리", "차가운 벽에 고사리 모양 서리가 하룻밤 사이 피었다", 0.7f, Any, "#cfefff",
            "L 0,.9 0,-.9;L 0,-.5 -.4,-.8;L 0,-.5 .4,-.8;L 0,-.1 -.55,-.45;L 0,-.1 .55,-.45;L 0,.3 -.6,0;L 0,.3 .6,0;L 0,.65 -.4,.45;L 0,.65 .4,.45"),
        new("old_voice", CurioKind.Anomaly, CodexShelf.Photo, "옛 주파수의 목소리", "잡음 사이로 수십 년 전 주파수의 목소리가 잠깐 잡혔다", 0.4f, In(RoomType.Comms, RoomType.Bridge), "#6ee7b7",
            "P -.8,-.2 -.5,-.2 -.1,-.55 -.1,.55 -.5,.2 -.8,.2;A 0,0,.35,-50,50;A 0,0,.6,-50,50;A 0,0,.85,-50,50"),
        new("warm_spot", CurioKind.Anomaly, CodexShelf.Photo, "까닭 없이 따뜻한 자리", "바닥 한 칸만 손바닥처럼 따뜻하다 — 밑에 지나는 관도 없다", 0.6f, Any, "#ff7a5c",
            "o 0,.2,.25;o 0,.2,.5;o 0,.2,.75;L -.25,-.3 -.15,-.55 -.25,-.8;L .05,-.3 .15,-.55 .05,-.8;L .3,-.35 .4,-.6 .3,-.85"),
        new("spinning_needle", CurioKind.Anomaly, CodexShelf.Photo, "제멋대로 도는 바늘", "계기 바늘들이 한꺼번에 한 바퀴 돌고 제자리로 왔다", 0.5f, In(RoomType.Reactor, RoomType.Engine, RoomType.Power), "#f2b134",
            "o 0,0,.75;P -.65,-.1 0,0 .65,.1 0,0;L -.5,.5 .5,-.5;A 0,0,.9,20,80;A 0,0,.9,200,260"),
        new("afterglow", CurioKind.Anomaly, CodexShelf.Photo, "불 끈 뒤 남은 빛", "불이 나간 방에서 천장 등이 한참 희미하게 빛났다", 0.6f, Any, "#ffe8a0",
            "P -.3,-.6 .3,-.6 .4,0 .15,.25 -.15,.25 -.4,0;P -.15,.25 .15,.25 .12,.5 -.12,.5;o 0,-.2,.65;o 0,-.2,.9"),
        // ── 창밖에 드물게 지나가는 것 (사진) ──
        new("comet", CurioKind.Passing, CodexShelf.Photo, "꼬리 긴 혜성", "꼬리가 창 끝까지 늘어진 혜성이 지나갔다", 0.8f, Any, "#a0e0ff",
            "O .55,-.45,.2;P .45,-.6 -.9,.55 -.7,.75 .65,-.3;L .4,-.4 -.95,.85"),
        new("far_ship", CurioKind.Passing, CodexShelf.Photo, "멀리 지나는 다른 배", "멀리 다른 배의 불빛이 나란히 가다 멀어졌다 — 손을 흔든 사람도 있었다", 0.7f, Any, "#ffd166",
            "P -.8,-.12 .5,-.12 .85,0 .5,.12 -.8,.12;P -.5,-.12 -.2,-.12 -.35,-.4;O .55,0,.04;O -.9,0,.07;O -.98,0,.04"),
        new("nebula", CurioKind.Passing, CodexShelf.Photo, "분홍 성운", "창밖이 분홍빛 성운으로 물들었다", 0.8f, Any, "#f0a6f7",
            "P -.8,.1 -.5,-.5 0,-.3 .4,-.7 .8,-.2 .6,.4 .1,.3 -.3,.7 -.7,.5;O -.2,-.1,.06;O .4,-.1,.05;O .1,.5,.04"),
        new("aurora", CurioKind.Passing, CodexShelf.Photo, "배를 감싼 빛 띠", "자기장에 걸린 입자가 배 둘레에 초록 빛 띠를 둘렀다", 0.6f, Any, "#86efac",
            "L -.9,.3 -.5,-.2 -.1,.2 .3,-.3 .7,.1 .95,-.2;L -.9,.55 -.5,.05 -.1,.45 .3,-.05 .7,.35 .95,.05;L -.9,.8 -.5,.3 -.1,.7 .3,.2 .7,.6 .95,.3"),
        new("rogue_planet", CurioKind.Passing, CodexShelf.Photo, "떠돌이 행성", "별도 없이 떠도는 검은 행성이 별빛을 가리며 지나갔다", 0.4f, Any, "#7a6f8f",
            "O 0,0,.6;A 0,0,.6,100,260;L -.5,-.15 .45,-.25;L -.55,.2 .5,.1"),
        new("derelict", CurioKind.Passing, CodexShelf.Photo, "버려진 정거장", "부서진 정거장이 천천히 돌며 스쳐 갔다", 0.5f, Any, "#8d93a6",
            "P -.7,-.2 .2,-.2 .2,.2 -.7,.2;P -.1,-.7 .1,-.7 .1,.7 -.1,.7;P .2,-.1 .5,-.35 .6,-.25 .3,.05;L .55,.4 .85,.6;L .6,.5 .8,.75"),
        new("ice_cloud", CurioKind.Passing, CodexShelf.Photo, "반짝이는 얼음 구름", "얼음 알갱이 구름을 지나며 창이 반짝반짝 빛났다", 0.7f, Any, "#e0f4ff",
            "O -.4,0,.3;O .1,-.2,.35;O .45,.1,.28;O -.05,.25,.3;O .6,-.55,.04;O -.7,-.5,.04;O .8,.5,.04"),
        new("pulsar", CurioKind.Passing, CodexShelf.Photo, "깜빡이는 펄서", "멀리서 일정하게 깜빡이는 별 — 맥박처럼 센다", 0.5f, Any, "#c4b5fd",
            "O 0,0,.18;L 0,0 -.5,-.85;L 0,0 .5,.85;o 0,0,.4;A 0,0,.7,0,60;A 0,0,.7,180,240"),
        new("meteor_shower", CurioKind.Passing, CodexShelf.Photo, "유성우", "먼 바위 띠를 스치며 유성이 비처럼 그어졌다", 0.7f, Any, "#ffb070",
            "L .9,-.9 .3,-.3;L .6,-.95 .05,-.4;L .95,-.5 .45,0;L .2,-.75 -.2,-.35;O .3,-.3,.07;O .05,-.4,.06;O .45,0,.06;O -.2,-.35,.05"),
        new("twin_stars", CurioKind.Passing, CodexShelf.Photo, "쌍둥이 별의 가림", "두 별이 서로를 가리며 빛이 한 번 어두워졌다", 0.4f, Any, "#fde68a",
            "O -.35,0,.35;O .4,0,.3;P .1,-.1 .3,-.08 .3,.08 .1,.1;A -.35,0,.5,90,270;A .4,0,.45,-90,90"),
        // ── 숨은 재능 (사람) ──
        new("t_song", CurioKind.Talent, CodexShelf.People, "숨은 노래 솜씨", "흐르던 노래를 따라 부르는데 — 다들 말을 멈췄다", 1f, Any, "#ffb454",
            "O -.4,.5,.2;L -.22,.5 -.22,-.6;P -.22,-.6 .45,-.8 .45,-.55 -.22,-.35;O .27,.3,.2;L .45,.3 .45,-.7", TalentCue.Music),
        new("t_piano", CurioKind.Talent, CodexShelf.People, "건반 치는 손", "탁자를 건반 삼아 손가락으로 곡을 짚는다 — 진짜 칠 줄 안다", 0.8f, Any, "#e6eaf2",
            "P -.9,-.3 .9,-.3 .9,.6 -.9,.6;P -.6,-.3 -.45,-.3 -.45,.2 -.6,.2;P -.15,-.3 0,-.3 0,.2 -.15,.2;P .3,-.3 .45,-.3 .45,.2 .3,.2;L -.3,.2 -.3,.6;L .15,.2 .15,.6;L .6,.2 .6,.6", TalentCue.Music),
        new("t_draw", CurioKind.Talent, CodexShelf.People, "초상화 솜씨", "심심해서 끄적인 얼굴 그림이 너무 닮아서 다들 웃었다", 0.9f, Any, "#c9a66b",
            "P -.85,.85 -.6,.4 .55,-.75 .75,-.55 -.4,.6;P .55,-.75 .7,-.9 .9,-.7 .75,-.55;O -.75,.75,.08", TalentCue.Bored),
        new("t_juggle", CurioKind.Talent, CodexShelf.People, "저글링", "식사 자리에서 빵 세 개로 저글링을 해 보였다", 0.9f, Any, "#ffd166",
            "O -.55,.1,.15;O 0,-.55,.15;O .55,.1,.15;A 0,.2,.6,180,360;L -.4,.8 -.2,.55;L .4,.8 .2,.55", TalentCue.Meal),
        new("t_magic", CurioKind.Talent, CodexShelf.People, "카드 마술", "식탁에서 카드 마술을 했는데 아무도 속임수를 못 찾았다", 0.7f, Any, "#ff8fa3",
            "P -.5,-.75 .1,-.85 .3,.6 -.3,.7;P -.1,-.6 .5,-.7 .65,.55 .1,.65;O .27,0,.08;P -.6,.75 .75,.75 .75,.9 -.6,.9", TalentCue.Meal),
        new("t_stars", CurioKind.Talent, CodexShelf.People, "별 이름을 다 안다", "창밖 별을 하나하나 이름으로 불렀다 — 언제 다 외웠지", 0.8f, Any, "#a0c4ff",
            "P 0,-.85 .2,-.25 .8,-.25 .3,.1 .5,.7 0,.35 -.5,.7 -.3,.1 -.8,-.25 -.2,-.25;O .7,.75,.06;O -.75,.8,.05", TalentCue.Window),
        new("t_knots", CurioKind.Talent, CodexShelf.People, "매듭 솜씨", "끊어진 끈을 처음 보는 매듭으로 순식간에 묶었다", 0.8f, Any, "#b8e986",
            "o -.3,0,.35;o .3,0,.35;L -.95,.3 -.6,0;L .6,0 .95,.3", TalentCue.Repair),
        new("t_firstaid", CurioKind.Talent, CodexShelf.People, "응급 처치 손", "다친 사람 곁에서 의무관처럼 침착하게 지혈했다", 0.7f, Any, "#f47b7b",
            "P -.2,-.75 .2,-.75 .2,-.2 .75,-.2 .75,.2 .2,.2 .2,.75 -.2,.75 -.2,.2 -.75,.2 -.75,-.2 -.2,-.2;o 0,0,.95", TalentCue.Crisis),
        // ── 비밀 (사람) — 알게 되면 사이가 바뀐다 ──
        new("s_relative", CurioKind.Secret, CodexShelf.People, "전 승무원의 가족", "예전 이 배를 탔던 사람이 가족이었다 — 그래서 이 배를 골랐다", 0.6f, Any, "#7cc4ff",
            "P -.55,-.2 .55,-.2 .55,.75 -.55,.75;A 0,-.2,.35,180,360;O 0,.25,.15;L 0,.4 0,.6", Polarity: 1),
        new("s_garden", CurioKind.Secret, CodexShelf.People, "몰래 키운 화분", "사물함 안에서 몰래 키워 온 화분 — 꽃이 피려 한다", 0.8f, Any, "#8fd65a",
            "P -.45,.2 .45,.2 .35,.85 -.35,.85;L 0,.2 0,-.4;P 0,-.35 -.45,-.6 -.5,-.25 -.1,-.15;P 0,-.45 .4,-.8 .5,-.45 .1,-.3", Polarity: 1),
        new("s_poems", CurioKind.Secret, CodexShelf.People, "밤마다 쓰는 시", "밤마다 몰래 시를 쓴다 — 공책 한 권이 다 찼다", 0.8f, Any, "#e6eaf2",
            "P -.35,.9 -.2,.4 .6,-.85 .75,-.7 -.05,.5;L -.2,.4 -.05,.5;L -.85,.9 .2,.9;O -.3,.85,.05", Polarity: 1),
        new("s_ration", CurioKind.Secret, CodexShelf.People, "몰래 모은 식량", "사물함 바닥에 배급 식량을 몰래 모아 두었다", 0.6f, Any, "#f2b134",
            "P -.7,-.3 .7,-.3 .7,.7 -.7,.7;P -.75,-.5 .75,-.5 .7,-.3 -.7,-.3;O -.3,.2,.15;O .25,.15,.13;L -.7,.05 .7,.05", Polarity: -1),
        new("s_broke", CurioKind.Secret, CodexShelf.People, "망가뜨리고 숨긴 것", "예전에 남의 물건을 망가뜨리고 말없이 숨겨 두었다", 0.5f, Any, "#ff7a85",
            "P -.7,-.6 -.05,-.6 -.15,-.1 0,.1 -.1,.7 -.7,.7;P .05,-.6 .7,-.6 .7,.7 0,.7 .1,.15 -.05,-.05;L -.05,-.6 -.15,-.1 0,.1 -.1,.7", Polarity: -1),
        new("s_fear", CurioKind.Secret, CodexShelf.People, "어둠을 무서워한다", "불이 나가자 숨을 몰아쉬었다 — 어둠을 무서워해 왔다", 0.7f, Any, "#c4b5fd",
            "O 0,0,.55;P .05,-.7 .5,-.45 .65,0 .5,.45 .05,.7 .35,0;O -.2,-.1,.07;O .05,-.1,.07"),
    };

    private static readonly Dictionary<string, CurioSpec> _byKey = All.ToDictionary(s => s.Key);
    public static CurioSpec Of(string key) => _byKey[key];
    public static bool Has(string key) => _byKey.ContainsKey(key);
    public static IEnumerable<CurioSpec> Of(CurioKind k) => All.Where(s => s.Kind == k);
    public static string ShelfName(CodexShelf s) => s switch { CodexShelf.Photo => "사진", CodexShelf.Specimen => "표본", CodexShelf.Relic => "유물", _ => "사람들" };
    public static string KindName(CurioKind k) => k switch
    {
        CurioKind.Hidden => "숨은 물건", CurioKind.Graffiti => "낙서", CurioKind.Trace => "전 승무원의 흔적", CurioKind.Anomaly => "이상한 일",
        CurioKind.Passing => "창밖", CurioKind.Talent => "숨은 재능", _ => "비밀",
    };
}
