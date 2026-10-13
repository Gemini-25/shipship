using System;
using System.Linq;
using ShipSim.Core;

// v23 볼거리 점검 (--funtest):
//   반려동물 (개 · 새 · 물고기 · 벌 — 돌아다니고 쓰다듬는다 · 마음 돌봄에 더한다) · 살아 있는 행성 (생명 · 표본 · 연구 · 길들임 · 탈출)
//   · 다른 배 넷 (교역 · 구조 · 세대선 방문 · 약탈) · 기항지 상륙 · 축제 (장식 불빛) · 저장.
public static partial class Program
{
    private static int RunFunTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"볼거리 점검 (v23) · 시드 {seed}\n");
        try
        {
            GenerationSystem.QuietOff = true; PlanetSystem.Off = true; PurposeSystem.Off = true;
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(1));
            var pets = w.Pets;
            int crew = w.Crew.Count(c => !c.Dead);
            var start = pets.Pets.ToDictionary(p => p.Id, p => p.Pos);
            Check("반려동물이 배에 탄다 (사람 수 · 재배실 · 수조에 따라)", pets.Pets.Count >= 1,
                $"승무원 {crew} · " + string.Join(" · ", pets.Pets.Select(p => $"{PetSystem.KindName(p.Kind)} {p.Name}")));

            // 1) 하루 — 돌아다니고 쓰다듬는다
            Run(w, SimTime.TicksPerDay);
            int moved = pets.Pets.Count(p => start.TryGetValue(p.Id, out var s) && (p.Pos - s).LengthSquared() > 1f);
            Check("하루 — 돌아다니고 사람이 쓰다듬는다", moved >= 1 && pets.Pettings >= 1,
                $"움직인 동물 {moved} · 쓰다듬기 {pets.Pettings} · 마음 병 곁 {pets.Comforts} · " + string.Join(" · ", pets.Pets.Select(p => $"{p.Name} {p.Doing}")));
            var walker = pets.Pets.FirstOrDefault(p => p.Kind is PetKind.Dog or PetKind.Bird);
            if (walker != null)
            {
                var near = w.Crew.Where(c => !c.Dead).OrderBy(c => (c.Position - walker.Pos).LengthSquared()).First();
                walker.PettedAt = w.Tick;
                walker.Pos = near.Position;
                Check("쓰다듬은 동물 곁 — 마음 돌봄 회복에 더한다", pets.ComfortGain(near) > 0f, $"{near.Name} +{pets.ComfortGain(near):0.0}");
            }

            // 2) 살아 있는 행성
            var rng = new Rng(seed);
            int none = 0, rich = 0;
            var lines = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < 300; i++)
            {
                var pl = PlanetGen.Make(rng, 700 + i, 1 + i % 8);
                float l = PetSystem.LifeOf(pl);
                if (l <= 0.05f) none++; else if (l >= 0.6f) rich++;
                var ln = PetSystem.LifeLine(pl);
                lines[ln] = (lines.TryGetValue(ln, out var nn) ? nn : 0) + 1;
            }
            Check("살아 있는 행성 — 생명 없는 곳부터 숲 · 무리 지은 동물까지", none > 30 && lines.Count >= 3,
                string.Join(" · ", lines.Select(kv => $"{kv.Key} {kv.Value}")));

            // 3) 표본 → 연구 → 결과 (씨앗 · 길들임 · 탈출 · 약 재료)
            var home = PlanetGen.Make(rng, 999, 3);
            string[] kinds = { "식물", "식물", "동물", "동물", "동물", "미생물" };
            foreach (var k in kinds) pets.Collect(home, k);
            float research0 = w.Research;
            Run(w, SimTime.TicksPerDay * 4);
            int done = pets.Specimens.Count(s => s.Done);
            Check("표본 여섯 — 연구 끝 · 결과가 갈린다 (씨앗 · 길들임 · 탈출 · 약)", done >= 3 && w.Research > research0,
                $"끝남 {done}/{pets.Specimens.Count} · 좋은 씨앗 {pets.SeedsGood} · 나쁜 씨앗 {pets.SeedsBad} · 길들임 {pets.Tamed} · 탈출 {pets.Escapes} · 연구 +{w.Research - research0:0}");

            // 4) 탈출한 외계 생물 — 사람을 놀라게 하고 로봇이 잡는다
            int caught0 = pets.Caught;
            var wild = pets.Escape("모래 두더지");
            Run(w, SimTime.TicksPerDay * 2);
            Check("숨어든 외계 생물 — 이틀 안에 붙잡힌다", wild != null && pets.Caught > caught0, $"잡음 {pets.Caught - caught0} · 남은 것 {pets.Line() ?? "없음"}");

            // 5) 다른 배 넷
            var meet = w.Encounters;
            int alive0 = w.Crew.Count(c => !c.Dead && !c.LeftShip);
            var outs = new System.Collections.Generic.List<string>();
            foreach (var k in Enum.GetValues<MeetKind>())
            {
                var m = meet.Meet(k);
                outs.Add($"{EncounterSystem.KindName(k)}: {m.Outcome}");
                Run(w, SimTime.Hours(15));
            }
            Check("다른 배 넷 — 교역 · 구조 · 세대선 방문 · 약탈 (지나가면 멀어진다)", meet.Meets.Count == 4 && meet.Meets.All(m => m.Done && m.Outcome.Length > 0) && meet.Traded == 1,
                string.Join(" | ", outs) + $" · 구조 {meet.Rescued} (승무원 {alive0}→{w.Crew.Count(c => !c.Dead && !c.LeftShip)})");

            // 6) 기항지 상륙
            float stress0 = w.Crew.Where(c => !c.Dead).Average(c => c.Needs.Stress);
            foreach (var c in w.Crew.Where(c => !c.Dead)) c.Needs.Stress = MathF.Max(c.Needs.Stress, 0.5f);
            meet.ShoreLeave("새벽 항구");
            float stress1 = w.Crew.Where(c => !c.Dead).Average(c => c.Needs.Stress);
            Check("기항지 상륙 — 장보기 · 기념품 · 술집 다툼, 쉬고 온다", meet.ShoreLeaves == 1 && stress1 < 0.5f,
                $"스트레스 0.50→{stress1:0.00} · 기념품 {meet.Souvenirs} · 다툼 {meet.Brawls} · {w.Log.Entries.Where(e => e.Text.Contains("새벽 항구")).Select(e => e.Text).LastOrDefault()}");

            // 7) 축제 — 식당에 장식 불빛 · 잔치
            int fest0 = meet.Festivals;
            bool ok = meet.Celebrate("수확 축제");
            for (int i = 0; i < 48 && !ok; i++) { Run(w, SimTime.Hours(1)); ok = meet.Celebrate("수확 축제"); } // 이미 잡힌 잔치가 끝나길 기다린다
            bool lit = meet.DecorRoom >= 0 && meet.DecorUntil > w.Tick;
            bool feasted = false;
            long feastAt = w.Motions.FeastAt, t0 = w.Tick;
            for (int i = 0; i < 30 * 6 && !feasted; i++) { Run(w, SimTime.Minutes(10)); feasted |= w.Motions.Feasting; }
            Check("수확 축제 — 장식 불빛 · 잔치가 열린다", ok && lit && meet.Festivals > fest0 && feasted,
                $"{meet.FestivalLog.LastOrDefault()} · 잔치 {SimTime.Clock(feastAt)} (지금 {SimTime.Clock(t0)}, +{(feastAt - t0) / (float)SimTime.Hours(1):0.0}시간) · 장식 방 {w.Ship.Rooms.FirstOrDefault(r => r.Id == meet.DecorRoom)?.Name}");

            // 8) 저장
            var snap = StateSave.Write(w);
            Check("상태 저장", snap.Length > 0, $"{snap.Length / 1024}KB · 반려동물 {pets.Line()}");
        }
        catch (Exception ex) { Check("예외 없이", false, ex.ToString()); }
        finally { GenerationSystem.QuietOff = false; PlanetSystem.Off = false; PurposeSystem.Off = false; }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
