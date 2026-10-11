using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShipSim.Art;
using ShipSim.TexGen;


// 도트 그림 확인: days seed --dotsheet=경로.png [--dotscale=4] [--dotwhat=people|scene|all]
//   같은 코드가 게임 텍스처를 만든다 — 여기서는 PNG로 뽑아 눈으로 본다.
public static class DotSheet
{
    public static int Run(string[] args)
    {
        string path = Arg(args, "--dotsheet=") ?? "dotsheet.png";
        int scale = int.TryParse(Arg(args, "--dotscale="), out var s) ? s : 4;
        string what = Arg(args, "--dotwhat=") ?? "people";
        if (what == "fsizes")
        {
            // 배마다 설비 종류 · 크기 (도트 설비 그림의 틀을 정하려고)
            var sizes = new SortedDictionary<string, SortedSet<string>>();
            foreach (var t in ShipSim.Core.ShipCatalog.All)
            {
                var w = ShipSim.Core.World.CreateDefault(7, 0, t.Key);
                foreach (var f in w.Ship.Furniture)
                {
                    var k = f.Type.ToString();
                    if (!sizes.TryGetValue(k, out var set)) sizes[k] = set = new SortedSet<string>();
                    set.Add($"{f.Width}x{f.Height}");
                }
            }
            foreach (var (k, set) in sizes) Console.WriteLine($"{k}: {string.Join(" ", set)}");
            return 0;
        }
        if (what == "speed")
        {
            var cast0 = Cast();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int n = 0;
            for (int rep = 0; rep < 40; rep++)
                foreach (var l in cast0)
                    for (int f = 0; f < 4; f++) { var dd = DotPeople.Draw(l, (DotFacing)f, DotPose.Walk, rep & 3); var im = dd.ToImg(); n += im.W; }
            Console.WriteLine($"사람 한 장면 그리기: {sw.Elapsed.TotalMilliseconds * 1000 / (40 * cast0.Count * 4):0} µs");
            return 0;
        }
        Img img = what switch
        {
            "scene" => Scene(),
            "parts" => Parts(),
            "bots" => Bots(),
            "tiles" => Tiles(),
            "props" => Props(),
            "fixtures" => Fixtures(Arg(args, "--dotstate=") ?? "on"),
            _ => People(),
        };
        var big = img.OnColor(Rgba.Hex("#2b2f38")).Scale(scale);
        File.WriteAllBytes(path, Png.Encode(big.W, big.H, big.Px));
        Console.WriteLine($"도트 그림: {path} ({img.W}×{img.H} ×{scale})");
        return 0;
    }

    private static string? Arg(string[] a, string key)
    {
        foreach (var x in a) if (x.StartsWith(key)) return x[key.Length..];
        return null;
    }

    public static List<PersonLook> Cast() => new()
    {
        new() { Female = false, Age = DotAge.Adult, Skin = 1, HairRamp = Ramps.HairDark, Hair = DotHair.Crop, Cloth = Ramps.ClothOrange, Accent = Ramps.ClothTeal, Badge = 1, Stubble = true, Held = DotHeld.Wrench, Seed = 1 },
        new() { Female = true, Age = DotAge.Adult, Skin = 0, HairRamp = Ramps.HairAuburn, Hair = DotHair.Ponytail, Cloth = Ramps.ClothBlue, Accent = Ramps.ClothYellow, Badge = 3, Seed = 2 },
        new() { Female = false, Age = DotAge.Middle, Build = DotBuild.Heavy, Skin = 3, HairRamp = Ramps.HairBlack, Hair = DotHair.Bald, Cloth = Ramps.ClothWhite, Accent = Ramps.ClothRed, Badge = 2, Glasses = true, Mustache = true, Held = DotHeld.Tablet, Seed = 3 },
        new() { Female = true, Age = DotAge.Elder, Skin = 2, HairRamp = Ramps.HairWhite, Hair = DotHair.Bun, Cloth = Ramps.ClothGreen, Accent = Ramps.ClothPurple, Badge = 5, Cane = true, Glasses = true, Seed = 4 },
        new() { Female = false, Age = DotAge.Child, Skin = 4, HairRamp = Ramps.HairBlack, Hair = DotHair.Curly, Accent = Ramps.ClothYellow, Pattern = 1, Seed = 5 },
        new() { Female = true, Age = DotAge.Teen, Build = DotBuild.Thin, Skin = 5, HairRamp = Ramps.HairDark, Hair = DotHair.Bob, Cloth = Ramps.ClothPurple, Accent = Ramps.ClothTeal, Casual = true, Pattern = 4, Seed = 6 },
        new() { Female = false, Age = DotAge.Adult, Build = DotBuild.Strong, Skin = 2, HairRamp = Ramps.HairBlonde, Hair = DotHair.Side, Cloth = Ramps.ClothTeal, Accent = Ramps.ClothOrange, Badge = 4, Beard = true, Hurt = DotHurt.HeadWrap | DotHurt.SlingL | DotHurt.CastLegR | DotHurt.Bleed, Seed = 7 },
        new() { Female = true, Age = DotAge.Adult, Skin = 3, HairRamp = Ramps.HairTeal, Hair = DotHair.Long, Cloth = Ramps.ClothPurple, Accent = Ramps.ClothGreen, Badge = 6, Hurt = DotHurt.MetalArmR | DotHurt.Burn | DotHurt.EyePatch, Seed = 8 },
        new() { Female = false, Age = DotAge.Elder, Build = DotBuild.Thin, Skin = 0, HairRamp = Ramps.HairGrey, Hair = DotHair.Receding, Cloth = Ramps.ClothBeige, Accent = Ramps.ClothNavy, Casual = true, Pattern = 3, Beard = true, Seed = 9 },
        new() { Female = true, Age = DotAge.Adult, Skin = 4, HairRamp = Ramps.HairBlack, Hair = DotHair.Braid, Cloth = Ramps.ClothOrange, Accent = Ramps.ClothBlue, Badge = 1, Hurt = DotHurt.Sick | DotHurt.Sweat, Seed = 10 },
        new() { Female = false, Age = DotAge.Teen, Skin = 1, HairRamp = Ramps.HairPink, Hair = DotHair.Buzz, Accent = Ramps.ClothRed, Casual = true, Pattern = 2, Hurt = DotHurt.Bruise | DotHurt.NoArmL, Seed = 11 },
        new() { Female = true, Age = DotAge.Child, Skin = 0, HairRamp = Ramps.HairBlonde, Hair = DotHair.Ponytail, Accent = Ramps.ClothPurple, Pattern = 1, Seed = 12 },
    };

    private static Img People()
    {
        var cast = Cast();
        int fw = DotPeople.FW, fh = DotPeople.FH, pad = 2;
        // 1줄: 12명 앞모습 · 2줄: 같은 12명 옆 · 3줄: 뒤 · 4줄: 첫 사람 걷기(앞 4 · 옆 4) · 앉기 · 일하기 · 들기 · 웅크림
        int cols = cast.Count;
        var img = new Img(cols * (fw + pad) + pad, 5 * (fh + pad) + pad);
        for (int i = 0; i < cols; i++)
        {
            Put(img, DotPeople.Draw(cast[i], DotFacing.S), i, 0);
            Put(img, DotPeople.Draw(cast[i], DotFacing.E), i, 1);
            Put(img, DotPeople.Draw(cast[i], DotFacing.N), i, 2);
        }
        var p = cast[1];
        int c = 0;
        for (int f = 0; f < 4; f++) Put(img, DotPeople.Draw(p, DotFacing.S, DotPose.Walk, f), c++, 3);
        for (int f = 0; f < 4; f++) Put(img, DotPeople.Draw(p, DotFacing.E, DotPose.Walk, f), c++, 3);
        Put(img, DotPeople.Draw(cast[0], DotFacing.S, DotPose.Sit), c++, 3);
        var welder = cast[0]; var keep = welder.Held; welder.Held = DotHeld.Welder;
        Put(img, DotPeople.Draw(welder, DotFacing.E, DotPose.Work), c++, 3); welder.Held = keep;
        var box = cast[6]; var k2 = box.Held; var h2 = box.Hurt; box.Held = DotHeld.Box; box.Hurt = DotHurt.None;
        Put(img, DotPeople.Draw(box, DotFacing.S, DotPose.Carry), c++, 3); box.Held = k2; box.Hurt = h2;
        Put(img, DotPeople.Draw(cast[2], DotFacing.S, DotPose.Cower), c++, 3);
        // 5줄: 왼팔을 잃고 오른다리 깁스 (앞 · 동 · 서 · 뒤 — 좌우가 맞는지) · 오른팔 의수 · 우주복 셋 · 무릎 · 잠든 얼굴 · 숨짐
        var t = new PersonLook { Female = false, Skin = 2, HairRamp = Ramps.HairBrown, Hair = DotHair.Crop, Cloth = Ramps.ClothTeal, Accent = Ramps.ClothRed, Badge = 4, Hurt = DotHurt.NoArmL | DotHurt.CastLegR, Seed = 21 };
        c = 0;
        foreach (var f in new[] { DotFacing.S, DotFacing.E, DotFacing.W, DotFacing.N }) Put(img, DotPeople.Draw(t, f), c++, 4);
        var m = new PersonLook { Female = true, Skin = 4, HairRamp = Ramps.HairBlack, Hair = DotHair.Bob, Cloth = Ramps.ClothPurple, Accent = Ramps.ClothYellow, Badge = 6, Hurt = DotHurt.MetalArmR | DotHurt.NoLegL, Seed = 22 };
        Put(img, DotPeople.Draw(m, DotFacing.S), c++, 4); Put(img, DotPeople.Draw(m, DotFacing.E), c++, 4);
        var suit = cast[0]; suit.Suit = true;
        Put(img, DotPeople.Draw(suit, DotFacing.S), c++, 4); Put(img, DotPeople.Draw(suit, DotFacing.E, DotPose.Walk, 1), c++, 4); Put(img, DotPeople.Draw(suit, DotFacing.N), c++, 4); suit.Suit = false;
        Put(img, DotPeople.Draw(cast[1], DotFacing.S, DotPose.Kneel), c++, 4);
        var sl = cast[4]; sl.EyesClosed = true; Put(img, DotPeople.Draw(sl, DotFacing.S), c++, 4); sl.EyesClosed = false;
        var dead = cast[9]; dead.Dead = true; dead.EyesClosed = true; Put(img, DotPeople.Draw(dead, DotFacing.S), c++, 4); dead.Dead = false; dead.EyesClosed = false;
        return img;
    }

    private static void Put(Img img, Dot d, int col, int row)
    {
        int x = 2 + col * (DotPeople.FW + 2), y = 2 + row * (DotPeople.FH + 2);
        var sprite = d.ToImg();
        img.DrawShadowOf(sprite, x, y);
        img.Draw(sprite, x, y);
    }

    // ───────────────────────────── 설비 · 가구 137종 ─────────────────────────────

    private static readonly Dictionary<string, (int w, int h)> CommonSize = new()
    {
        ["AuxGenerator"] = (2, 1), ["Battery"] = (2, 2), ["Bed"] = (1, 2), ["Collector"] = (2, 1), ["CoolantPump"] = (2, 2), ["EngineCore"] = (3, 3),
        ["Fridge"] = (2, 1), ["GrowBed"] = (3, 1), ["MainComputer"] = (2, 2), ["MedBed"] = (1, 2), ["OxygenGenerator"] = (2, 2), ["PowerPanel"] = (3, 1),
        ["ReactorCore"] = (3, 3), ["Refinery"] = (2, 2), ["SensorArray"] = (2, 1), ["Shelf"] = (2, 1), ["Stove"] = (2, 1), ["SuitLocker"] = (1, 2),
        ["Table"] = (2, 1), ["WaterRecycler"] = (2, 2), ["Workbench"] = (3, 1), ["OperatingTable"] = (1, 2),
    };

    private static Img Fixtures(string state)
    {
        var st = state switch { "off" => FixState.Off, "broken" => FixState.Broken, _ => FixState.On };
        var types = (ShipSim.Core.FurnitureType[])Enum.GetValues(typeof(ShipSim.Core.FurnitureType));
        int cols = 12, cell = 104;
        int rows = (types.Length + cols - 1) / cols;
        var img = new Img(cols * cell, rows * cell);
        for (int i = 0; i < types.Length; i++)
        {
            var t = types[i];
            var (w, h) = CommonSize.TryGetValue(t.ToString(), out var sz) ? sz : (1, 1);
            var body = DotMachines.Body(t, w, h, -1, 1, 1, i);
            var life = DotMachines.Life(t, w, h, st, 2, 0.8f);
            int x = (i % cols) * cell + (cell - body.W) / 2, y = (i / cols) * cell + (cell - body.H) / 2;
            // 바닥 칸 (자리)
            img.Fill(x, y + DotMachines.RiseOf(t), w * 32, h * 32, Rgba.Hex("#3a404b"));
            img.Draw(body, x, y); img.Draw(life, x, y);
        }
        return img;
    }

    // ───────────────────────────── 벽 소품 · 바닥 꾸밈 ─────────────────────────────

    private static Img Props()
    {
        var props = (DotProp[])Enum.GetValues(typeof(DotProp));
        var img = new Img(props.Length * 40 + 8, 210);
        img.Fill(0, 0, img.W, img.H, Rgba.Hex("#2a2f38"));
        for (int i = 0; i < props.Length; i++)
        {
            int x = 4 + i * 40;
            // 앞면: 벽 앞면 띠(10줄) 위에
            img.Fill(x, 4, 32, 10, Rgba.Hex("#3a4250"));
            img.Draw(DotProps.Wall(props[i], PropSide.Face, Ramps.ClothTeal, Ramps.Leaf, i, "MS-12"), x, 4);
            // 옆 · 뒤
            img.Fill(x, 34, 6, 32, Rgba.Hex("#151a22"));
            img.Draw(DotProps.Wall(props[i], PropSide.Side, Ramps.ClothTeal, Ramps.Cyan, i), x + 6, 38);
            img.Fill(x, 72, 32, 6, Rgba.Hex("#151a22"));
            img.Draw(DotProps.Wall(props[i], PropSide.Back, Ramps.ClothTeal, Ramps.Cyan, i), x + 4, 68);
        }
        img.Draw(DotProps.Grate(), 4, 90);
        img.Draw(DotProps.Drain(), 30, 94);
        img.Draw(DotProps.Rug(120, 70, Ramps.ClothBrown, 3), 50, 90);
        img.Draw(DotProps.Rug(36, 10, Ramps.ClothRed, 5), 180, 90);
        img.Draw(DotProps.HazardFrame(110, 100, 5), 230, 90);
        return img;
    }

    // ───────────────────────────── 바닥 · 벽 타일 ─────────────────────────────

    private static Img Tiles()
    {
        var fa = DotTiles.FloorAtlas();
        var wa = DotTiles.WallAtlas();
        // 작은 방 둘: 바닥 재질을 섞고 벽은 이웃을 따라 이어 붙인다
        string[] map =
        {
            "HHHHHHHHHHHHHH",
            "HmmmmmPtttttH.",
            "HmggggPttttttH",
            "HmggggDttttttH",
            "HmeeeePtttcccH",
            "HmeeeePrrrcccH",
            "HHHHHHHHHHHHHH",
        };
        int W = map[0].Length, H = map.Length;
        var room = new Img(W * 32, H * 32);
        bool IsWall(int x, int y) => y >= 0 && y < H && x >= 0 && x < W && map[y][x] is 'H' or 'P' or 'D';
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                char ch = map[y][x];
                if (ch == '.') continue;
                if (ch is 'H' or 'P')
                {
                    int k = ch == 'H' ? 2 : 1;
                    int mask = (IsWall(x, y - 1) ? 1 : 0) | (IsWall(x + 1, y) ? 2 : 0) | (IsWall(x, y + 1) ? 4 : 0) | (IsWall(x - 1, y) ? 8 : 0);
                    Blit(room, wa, mask * 32, k * 32, 32, 32, x * 32, y * 32);
                    continue;
                }
                int fk = ch switch { 'g' => 1, 't' => 2, 'r' => 3, 'c' => 4, 'e' => 5, _ => 0 };
                int v = (int)(DotHash.U(x, y) % 4);
                Blit(room, fa, v * 32, fk * 32, 32, 32, x * 32, y * 32);
            }
        // 남쪽 앞면
        for (int y = 0; y < H - 1; y++)
            for (int x = 0; x < W; x++)
            {
                if (!IsWall(x, y) || IsWall(x, y + 1) || map[y + 1][x] == '.') continue;
                var kind = map[y][x] == 'H' ? DotWall.Hull : DotWall.Partition;
                var (fx, fy) = DotTiles.FaceCell(kind, !IsWall(x - 1, y) || IsWall(x - 1, y + 1), !IsWall(x + 1, y) || IsWall(x + 1, y + 1), x % 4 == 2);
                Blit(room, wa, fx, fy, 32, DotTiles.Face, x * 32, (y + 1) * 32);
            }
        var img = new Img(Math.Max(room.W, wa.W) + 8, fa.H + wa.H + room.H + 24);
        img.Draw(fa, 4, 4); img.Draw(wa, 4, fa.H + 12); img.Draw(room, 4, fa.H + wa.H + 20);
        return img;
    }

    private static void Blit(Img dst, Img src, int sx, int sy, int w, int h, int dx, int dy)
    {
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { var c = src.Get(sx + x, sy + y); if (c.A > 0) dst.Blend(dx + x, dy + y, c); }
    }

    // ───────────────────────────── 로봇 · 드론 장면표 ─────────────────────────────

    private static Img Bots()
    {
        var img = new Img(8 * 34 + 4, 6 * 34 + 2 * 28 + 8);
        var shapes = new[] { (DotBotKind.Maintainer, Ramps.Hazard, 6), (DotBotKind.Hauler, Ramps.ClothOrange, 1), (DotBotKind.Gardener, Ramps.ClothGreen, 7), (DotBotKind.Safety, Ramps.ClothRed, 10), (DotBotKind.Nurse, Ramps.ClothWhite, 0), (DotBotKind.Stretcher, Ramps.ClothWhite, 0) };
        int y = 2;
        foreach (var (shape, paint, badge) in shapes)
        {
            int x = 2;
            var l = new BotLook { Shape = shape, Paint = paint, Badge = badge, Seed = 3, Cargo = shape == DotBotKind.Hauler ? Ramps.Wood : -1 };
            foreach (var f in new[] { DotFacing.S, DotFacing.E, DotFacing.W, DotFacing.N })
            {
                var r = DotBots.Robot(l, f, 0);
                img.Oval(x + 16, y + 30, 12, 3, new Rgba(0, 0, 0), 0.3f);
                img.Draw(r, x, y); x += 34;
            }
            foreach (var st in new[] { DotBotState.Working, DotBotState.Broken, DotBotState.Off })
            {
                l.State = st;
                var r = DotBots.Robot(l, DotFacing.S, 1);
                img.Oval(x + 16, y + 30, 12, 3, new Rgba(0, 0, 0), 0.3f);
                img.Draw(r, x, y); x += 34;
            }
            l.State = DotBotState.Ok; l.Cargo = -1;
            if (shape == DotBotKind.Hauler) { img.Draw(DotBots.Robot(l, DotFacing.E, 2), x, y); }
            y += 34;
        }
        int dx = 2;
        foreach (var k in new[] { DotDroneKind.Inspect, DotDroneKind.Repair, DotDroneKind.Tow, DotDroneKind.Build })
        {
            img.Draw(DotBots.Drone(k, DotBotState.Ok, 0, -1, false, (int)k + 1), dx, y); dx += 30;
            img.Draw(DotBots.Drone(k, DotBotState.Working, 1), dx, y); dx += 30;
        }
        y += 28; dx = 2;
        img.Draw(DotBots.Drone(DotDroneKind.Repair, DotBotState.Ok, 0, Ramps.ClothOrange, brokenRotor: true), dx, y); dx += 30;
        img.Draw(DotBots.Drone(DotDroneKind.Inspect, DotBotState.Off, 0), dx, y); dx += 30;
        img.Draw(DotBots.Drone(DotDroneKind.Tow, DotBotState.Broken, 0), dx, y); dx += 30;
        return img;
    }

    // ───────────────────────────── 설비 · 로봇 · 드론 · 폭발 장면표 ─────────────────────────────

    private static Img Parts()
    {
        var img = new Img(660, 330);
        int x = 4;
        foreach (var k in new[] { DotBotKind.Maintainer, DotBotKind.Hauler, DotBotKind.Gardener, DotBotKind.Safety })
            foreach (var st in new[] { DotBotState.Ok, DotBotState.Working, DotBotState.Broken, DotBotState.Off })
            {
                if (k == DotBotKind.Hauler && st == DotBotState.Working) continue;
                if (k is DotBotKind.Gardener or DotBotKind.Safety && st is DotBotState.Working or DotBotState.Off) continue;
                var r = DotBots.Robot(k, st, 0, 3);
                img.Oval(x + 15, 31, 12, 3, new Rgba(0, 0, 0), 0.3f);
                img.Draw(r, x, 2); x += 33;
            }
        for (int f = 0; f < 3; f++) { var dr = DotBots.Drone(f == 2 ? DotBotState.Broken : DotBotState.Ok, f); img.Oval(x + 13, 34, 8, 2.5f, new Rgba(0, 0, 0), 0.25f); img.Draw(dr, x, 4 - (f & 1)); x += 28; }
        // 설비 상태줄
        int y = 44; x = 4;
        foreach (var st in new[] { DotMachineState.On, DotMachineState.Hot, DotMachineState.Off })
        { img.Draw(DotFixtures.Battery(st, 0.8f, 0), x, y); x += 68; }
        foreach (var st in new[] { DotMachineState.On, DotMachineState.Broken })
        { img.Draw(DotFixtures.Oxygen(st, 1), x, y - 4); x += 68; }
        foreach (var st in new[] { DotMachineState.On, DotMachineState.Hot })
        { img.Draw(DotFixtures.PowerPanel(st, 2), x, y); x += 36; }
        y = 100; x = 4;
        foreach (var st in new[] { DotMachineState.On, DotMachineState.Off })
        { img.Draw(DotFixtures.Stove(st, 1), x, y + 8); x += 68; }
        foreach (var st in new[] { DotMachineState.On, DotMachineState.Broken })
        { img.Draw(DotFixtures.Pump(st, 1), x, y); x += 52; }
        var head = DotPeople.Draw(Cast()[1], DotFacing.S);
        img.Draw(DotFixtures.Bed(Ramps.ClothTeal, HeadOnly(head)), x, y - 20); x += 36;
        img.Draw(DotFixtures.Bed(Ramps.ClothRed), x, y - 20); x += 36;
        img.Draw(DotFixtures.Door(0f), x, y); x += 34; img.Draw(DotFixtures.Door(0.7f), x, y); x += 34;
        img.Draw(DotFixtures.Table(), x, y + 10); x += 68;
        // 폭발 장면 (연료 · 수소 · 배터리 · 아크 · 증기 · 분진) — 줄마다 한 종류, 12장면 중 짝수만
        y = 150;
        var kinds = new[] { DotBlast.Fuel, DotBlast.Battery, DotBlast.Hydrogen };
        for (int k = 0; k < kinds.Length; k++)
            for (int f = 0; f < 12; f++)
            {
                if ((f & 1) == 1) continue;
                img.Draw(DotFx.Blast(kinds[k], f, 56, 3 + k), 4 + (f / 2) * 58, y + k * 58);
            }
        for (int f = 0; f < 4; f++) img.Draw(DotFx.FireTile(f, 0.9f, 2), 360 + f * 34, 150);
        img.Draw(DotFx.Blast(DotBlast.Steam, 4, 56, 9), 360, 196);
        img.Draw(DotFx.Blast(DotBlast.Dust, 4, 56, 11), 420, 196);
        img.Draw(DotFx.Blast(DotBlast.Arc, 2, 56, 13), 480, 196);
        return img;
    }

    /// <summary>앞모습에서 머리만 (베개 위).</summary>
    private static Dot HeadOnly(Dot person)
    {
        var d = new Dot(person.W, 18);
        for (int y = 0; y < 18; y++)
            for (int x = 0; x < person.W; x++)
                if (person.Has(x, y + 6)) d.Set(x, y, person.RampAt(x, y + 6), person.ShadeAt(x, y + 6));
        return d;
    }

    // ───────────────────────────── 시험 장면: 기관실 폭발 · 옆 휴게실 ─────────────────────────────

    private sealed record Sprite(Img Img, int X, int Y, int SortY, bool Shadow = true, float ShadowW = 0);

    private static Img Scene()
    {
        const int T = 32, TW = 22, TH = 13;
        var img = new Img(TW * T, TH * T);
        bool Wall(int cx, int cy) => cx <= 0 || cy <= 0 || cx >= TW - 1 || cy >= TH - 1 || (cx == 11 && cy != 6);
        // 바닥
        for (int cy = 0; cy < TH; cy++)
            for (int cx = 0; cx < TW; cx++)
                if (!Wall(cx, cy)) img.Draw(DotFixtures.FloorTile(cx, cy, cx < 11 && (cy is 5 or 6 or 7) ? 1 : 0), cx * T, cy * T);
        // 바닥 자국: 그을음 · 피 · 잔해 · 깨진 컵
        float bx = 3 * T, by = 1.7f * T;
        DotFx.Scorch(img, bx, by + 30, 70, 4);
        DotFx.Blood(img, 4.3f * T, 4.9f * T, 6, 2, trailX: -18, trailY: -10);
        DotFx.Rubble(img, bx + 20, by + 50, 40, 14, 5);
        // 벽 윗면
        for (int cy = 0; cy < TH; cy++)
            for (int cx = 0; cx < TW; cx++)
                if (Wall(cx, cy)) img.Draw(DotFixtures.WallTopTile(cy > 0 && Wall(cx, cy - 1), cx < TW - 1 && Wall(cx + 1, cy), cy < TH - 1 && Wall(cx, cy + 1), cx > 0 && Wall(cx - 1, cy)), cx * T, cy * T);

        var list = new List<Sprite>();
        // 벽 앞면 (아래 칸이 바닥이면)
        for (int cy = 0; cy < TH - 1; cy++)
            for (int cx = 0; cx < TW; cx++)
                if (Wall(cx, cy) && !Wall(cx, cy + 1)) list.Add(new(DotFixtures.WallFaceTile(cx, cx % 4 == 2), cx * T, (cy + 1) * T, (cy + 1) * T + 1, false));
        list.Add(new(DotFixtures.Door(0.35f), 11 * T, 6 * T - 12, 7 * T - 1, false));

        // 기관실 설비
        list.Add(new(DotFixtures.Battery(DotMachineState.Burning, 0.6f, 1), 2 * T, 2 * T - 40, 2 * T, false));
        list.Add(new(DotFixtures.PowerPanel(DotMachineState.On, 0), 5 * T, 1 * T + 2, 1 * T + 40, false));
        list.Add(new(DotFixtures.Oxygen(DotMachineState.On, 2), 6 * T, 2 * T - 44, 2 * T, false));
        list.Add(new(DotFixtures.Pump(DotMachineState.Hot, 1), 8 * T, 4 * T - 38, 4 * T, false));
        // 휴게실 설비
        list.Add(new(DotFixtures.Stove(DotMachineState.On, 2), 12 * T, 2 * T - 36, 2 * T, false));
        var cast = Cast();
        var sleeper = cast[8];
        list.Add(new(DotFixtures.Bed(Ramps.ClothNavy, HeadOnly(DotPeople.Draw(sleeper, DotFacing.S))), 20 * T, 1 * T, 3 * T, false));
        list.Add(new(DotFixtures.Table(), 14 * T, 5 * T - 6, 5 * T + 16, false));
        list.Add(new(DotFixtures.Cup(true), 14 * T + 10, 5 * T - 4, 5 * T + 17, false));
        list.Add(new(DotFixtures.Cup(false), 15 * T + 14, 5 * T - 3, 5 * T + 17, false));
        list.Add(new(DotFixtures.Plant(3), 20 * T + 8, 10 * T - 4, 11 * T - 4, false));
        list.Add(new(DotFixtures.Plant(8), 12 * T + 4, 10 * T - 4, 11 * T - 4, false));
        list.Add(new(DotFixtures.Chair(false), 15 * T - 8, 4 * T + 6, 4 * T + 20, false));
        list.Add(new(DotFixtures.Chair(true), 15 * T + 8, 5 * T + 8, 6 * T + 6, false));

        void Person(PersonLook l, DotFacing f, DotPose p, int frame, int px, int py)
        {
            var spr = DotPeople.Draw(l, f, p, frame).ToImg();
            list.Add(new(spr, px - DotPeople.Cx, py - DotPeople.Foot, py, true, 7));
        }
        // 기관실: 쓰러진 기관사(옆으로 누움) · 웅크린 의무관 · 소화기 들고 달려오는 사람 · 다친 정비사
        var down = DotPeople.Draw(cast[0], DotFacing.S).Rotate(cw: false).ToImg();
        list.Add(new(down, (int)(3.6f * T), (int)(4.4f * T), (int)(5.1f * T), true, 12));
        Person(cast[2], DotFacing.S, DotPose.Cower, 0, 6 * T, 4 * T + 20);
        var runner = cast[9]; runner.Held = DotHeld.Extinguisher; runner.Hurt = DotHurt.None;
        Person(runner, DotFacing.W, DotPose.Walk, 1, 8 * T + 12, 7 * T + 8);
        Person(cast[6], DotFacing.S, DotPose.Stand, 0, 3 * T, 9 * T + 6);
        Person(cast[7], DotFacing.E, DotPose.Walk, 3, 5 * T, 10 * T + 14);
        // 로봇 · 드론
        var welder = DotBots.Robot(DotBotKind.Maintainer, DotBotState.Working, 1, 2);
        list.Add(new(welder, 9 * T - 4, 5 * T - 6, 5 * T + 26, true, 12));
        var broken = DotBots.Robot(DotBotKind.Maintainer, DotBotState.Broken, 0, 5);
        list.Add(new(broken, 1 * T + 6, 6 * T + 2, 7 * T + 2, true, 12));
        var hauler = DotBots.Robot(DotBotKind.Hauler, DotBotState.Ok, 0, 1);
        list.Add(new(hauler, 6 * T + 4, 8 * T + 4, 9 * T + 4, true, 13));
        var drone = DotBots.Drone(DotBotState.Ok, 1);
        // 휴게실 사람들
        Person(cast[3], DotFacing.S, DotPose.Walk, 1, 17 * T, 9 * T + 4);        // 지팡이 노인
        Person(cast[4], DotFacing.E, DotPose.Walk, 1, 13 * T + 10, 8 * T + 16);    // 달리는 아이
        Person(cast[11], DotFacing.W, DotPose.Stand, 0, 15 * T, 9 * T + 10);      // 여자아이
        Person(cast[5], DotFacing.S, DotPose.Sit, 0, 15 * T, 5 * T - 4);          // 앉은 청소년 (탁자 뒤)
        var cupper = cast[1]; cupper.Held = DotHeld.Cup;
        Person(cupper, DotFacing.S, DotPose.Stand, 0, 17 * T + 8, 6 * T);          // 컵 든 조종사
        Person(cast[10], DotFacing.E, DotPose.Stand, 0, 19 * T, 7 * T + 4);       // 팔 잃은 청소년
        Person(cast[11 - 2], DotFacing.N, DotPose.Walk, 2, 18 * T + 16, 10 * T + 18);

        foreach (var s in list.OrderBy(s => s.SortY))
        {
            if (s.Shadow) img.Oval(s.X + s.Img.W / 2f, s.SortY - 1, s.ShadowW, 2.6f, new Rgba(4, 6, 12), 0.35f);
            img.Draw(s.Img, s.X, s.Y);
        }
        // 드론 (공중 — 그림자는 한참 아래)
        img.Oval(9.6f * T, 8.6f * T, 8, 2.5f, new Rgba(4, 6, 12), 0.25f);
        img.Draw(drone, (int)(9.2f * T), (int)(7.2f * T));

        // 효과: 칸 불 · 폭발 · 충격파 · 파편 · 연기 · 빛
        img.Draw(DotFx.FireTile(2, 0.8f, 3), 2 * T, 2 * T - 30);
        img.Draw(DotFx.FireTile(4, 0.6f, 7), 3 * T + 4, 2 * T - 26);
        img.Draw(DotFx.FireTile(1, 0.45f, 11), 1 * T + 6, 3 * T - 34);
        DotFx.Ring(img, bx, by + 6, 74, 0.45f);
        var blast = DotFx.Blast(DotBlast.Battery, 4, 96, 3);
        img.Draw(blast, (int)bx - 48, (int)by - 44);
        DotFx.Debris(img, bx, by, 110, 26, 9, DotBlast.Battery);
        DotFx.Puff(img, bx + 40, by - 18, 12, 3, 0.75f, DotFx.SmokeGreen);
        DotFx.Puff(img, bx + 58, by - 8, 9, 2, 0.6f, DotFx.SmokeGreen);
        img.Glow(bx, by, 110, Ramps.Get(Ramps.Fire, 5), 0.28f);
        img.Glow(12.8f * T, 1.4f * T, 30, Ramps.Get(Ramps.Fire, 5), 0.2f);
        // 연기 기둥 (불에서 위로 · 위쪽은 옅게)
        for (int i = 0; i < 6; i++) DotFx.Puff(img, 2.6f * T + i * 3 + (i & 1) * 4, 1.6f * T - i * 9, 7 + i * 1.5f, 2 + (i > 2 ? 1 : 0), 0.55f - i * 0.06f);
        // 표시: 놀람 · 잠
        Bang(img, 6 * T - 1, 4 * T - 26);
        Bang(img, 8 * T + 12, 6 * T - 28);
        Zzz(img, 20 * T + 20, 1 * T - 4);
        return img;
    }

    private static void Bang(Img img, int x, int y)
    {
        // 놀람 표시: 테두리 진 노란 느낌표 (2도트 폭)
        var c = Ramps.Get(Ramps.Hazard, 4); var k = Ramps.Get(Ramps.Ink, 0);
        img.Fill(x - 1, y - 1, 4, 8, k); img.Fill(x - 1, y + 8, 4, 4, k);
        img.Fill(x, y, 2, 6, c); img.Fill(x, y + 9, 2, 2, c);
    }

    private static void Zzz(Img img, int x, int y)
    {
        var c = Ramps.Get(Ramps.Bandage, 3);
        void Z(int zx, int zy, int s) { for (int i = 0; i < s; i++) { img.Set(zx + i, zy, c); img.Set(zx + i, zy + s - 1, c); img.Set(zx + s - 1 - i, zy + i, c); } }
        Z(x, y, 4); Z(x + 5, y - 5, 3);
    }
}

