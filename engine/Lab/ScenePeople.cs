using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>사람 시안 (그래픽 결정 6) — 세계의 몸 · 마음 값을 바꿔 한 방에 세워 본다: 덤덤 · 웃음(기쁨) · 겁 · 슬픔 · 아픔(통증 + 피 · 화상 · 덮은 상처) ·
/// 열이 나 땀 흘림 · 동상 · 방사선에 빠진 머리 · 기어감(낮은 머리) · 누움. 세 배로 키워 PNG로.</summary>
public static partial class Program
{
    private static void PeopleScene(string dir)
    {
        var s = ShipWorld.Crewed("Hannuri", 3, 12, 0);
        while (Time.HourOfDay(s.Tick) < 10) s.Run(25);
        var L = s.Layout; var m = s.Get<Mind>(); var life = s.Get<Life>();
        var room = L.Rooms.Where(r => r.Type == "Mess").OrderByDescending(r => r.Cells.Length).First();
        var free = room.Cells.Where(c => L.FurnitureAt[c] < 0 && L.Kind[c] == CellKind.Floor).OrderBy(c => L.Y(c)).ThenBy(c => L.X(c)).ToList();
        var people = m.Agents.Where(a => a.IsPerson).Take(12).ToList();
        string[] what = { "덤덤", "웃음 (기쁨 0.8)", "겁 (두려움 0.8)", "슬픔 (0.8)", "아픔 (통증 0.7 · 피)", "화상 · 덮은 상처", "열 (38.5℃ · 땀)", "앓음 (옮는 병)", "동상", "방사선 3Gy (머리 빠짐)", "기어감 (낮은 머리)", "누움 (잠)" };
        int row = Math.Min(6, free.Count / 2);
        for (int i = 0; i < people.Count; i++)
        {
            var a = people[i];
            ref var c = ref a.C; ref var b = ref life[c.Body];
            c.Cell = free[(i % 6) * 2 + (i / 6) * Math.Max(1, free.Count / 2)];
            a.Path.Clear(); a.PathIdx = 0;
            c.Fear = c.Joy = c.Sad = c.Stress = 0; c.Mode = 0; b.Pain = 0; b.Bleed = 0; b.Burn = 0; b.Dressed = 0; b.CoreK = 310.1 - 0.9; b.Low = 0; b.Asleep = 0;
            switch (i)
            {
                case 1: c.Joy = 0.8f; break;
                case 2: c.Fear = 0.8f; break;
                case 3: c.Sad = 0.8f; break;
                case 4: b.Pain = 0.7f; b.Bleed = 0.3f; break;
                case 5: b.Burn = 0.4f; b.Dressed = 5; b.Pain = 0.3f; break;
                case 6: b.CoreK = 310.6; break;
                case 7: b.Ill = 2; break;
                case 8: b.Frost = 0.4f; break;
                case 9: b.RadGy = 3; break;
                case 10: b.Low = 1; break;
                case 11: b.Asleep = 1; break;
            }
        }
        var painter = new Painter(L);
        int x0 = room.Cells.Min(L.X) - 1, y0 = room.Cells.Min(L.Y) - 1, w = room.Cells.Max(L.X) - x0 + 2, h = room.Cells.Max(L.Y) - y0 + 2;
        var img = painter.Frame(s, x0, y0, w, h, 0).Scale(3);
        Png.WriteRgba(Path.Combine(dir, "people.png"), img.W, img.H, img.Px);
        for (int i = 0; i < people.Count; i++) Console.WriteLine($"    {i,2} {m.NameOf(people[i].Id)} ({L.X(people[i].C.Cell)},{L.Y(people[i].C.Cell)}) — {what[i]}");
        Console.WriteLine($"  people: {dir}/people.png ({img.W}×{img.H})");
        // 동작 칸 (그래픽 결정 9 · 10): 줄마다 여덟 장 — 걷기 앞 · 걷기 옆 · 손을 입으로(앉아 마시기) · 공구 돌리기(옆) · 말하며 손짓 · 졸기 · 기어가기
        var look = new PersonLook { Female = true, Skin = 2, HairRamp = Ramps.HairBrown, Hair = DotHair.Ponytail, Cloth = Ramps.ClothTeal, Accent = Ramps.ClothRed, Badge = 4, Seed = 7, Cycle8 = true };
        var rows = new (string name, DotFacing face, DotPose pose, DotMotion motion, DotHeld held, bool crawl)[]
        {
            ("걷기 (앞)", DotFacing.S, DotPose.Walk, DotMotion.None, DotHeld.None, false),
            ("걷기 (옆)", DotFacing.E, DotPose.Walk, DotMotion.None, DotHeld.None, false),
            ("마시기 (앉아)", DotFacing.S, DotPose.Sit, DotMotion.Drink, DotHeld.Cup, false),
            ("공구 돌리기 (옆)", DotFacing.E, DotPose.Work, DotMotion.Tool, DotHeld.Wrench, false),
            ("말하며 손짓", DotFacing.S, DotPose.Stand, DotMotion.Talk, DotHeld.None, false),
            ("졸기 (앉아)", DotFacing.S, DotPose.Sit, DotMotion.Doze, DotHeld.None, false),
            ("기어가기", DotFacing.E, DotPose.Walk, DotMotion.None, DotHeld.None, true),
        };
        const int cw = 40, ch = 44;
        var sheet = new Img(cw * 8, ch * rows.Length);
        sheet.Fill(0, 0, sheet.W, sheet.H, Rgba.Hex("#2a2f38"));
        for (int r = 0; r < rows.Length; r++)
        {
            var (name, face, pose, motion, held, crawl) = rows[r];
            look.Motion = motion; look.Held = held; look.EyesClosed = motion == DotMotion.Doze;
            for (int f = 0; f < 8; f++)
            {
                var d = DotPeople.Draw(look, crawl ? DotFacing.S : face, pose, f);
                if (crawl) d = d.Rotate(cw: true);
                var im = d.ToImg();
                sheet.Draw(im, f * cw + (cw - im.W) / 2, r * ch + (ch - im.H) / 2);
            }
            Console.WriteLine($"    줄 {r}: {name}");
        }
        var big = sheet.Scale(4);
        Png.WriteRgba(Path.Combine(dir, "sheet.png"), big.W, big.H, big.Px);
        Console.WriteLine($"  동작 칸: {dir}/sheet.png");
    }
}
