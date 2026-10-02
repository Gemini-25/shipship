using System;
using System.Collections.Generic;
using Godot;

namespace ShipSim.View;

// v18.14 꾸미는 일의 그림 부품: 표(SchemeTable)의 그림 칸에 쓴 부품마다 모양 · 색 · 비율이 다르다 (통 · 관 · 방울 · 화분 · 안테나 · 우리 · 카드 …).
// 한 가지 일은 부품 두세 개를 겹쳐 그려 저마다 실루엣이 다르다. 가까이 보면 띠 · 못 · 눈금 · 불빛 같은 잔 디테일, 움직이는 것(방울 · 연기 · 바퀴 · 전파)은 시간에 따라 움직인다.
// 읽기만 한다 — 시뮬레이션을 바꾸지 않는다.
public partial class ShipView
{
    private enum GShape : byte
    {
        Cask, Jar, Bottle, Box, Crate, Pot, Leafy, Flower, Mushroom, Coil, Drop, Flame, Puff, Disc, Sheet, Book, Frame, Screen, Console, Antenna, Dish, Speaker, Mic,
        Note, Star, Heart, Ring, Arch, Lamp, Candle, Critter, Robot, Wheel, Gear, Bolt, Stick, Cards, Dice, Ball, Net, Cup, Mat, Cushion, Seat, Bunting, Clock, Drape,
        Rack, Capsule, Sign, Hand, Eye, Splat, Sparkle, Wave, Cross, Arrow, Globe, Cake, Mug, Kettle, Pan, Dome, Suit, Boot, Hat, Face, Ghost, Glass, Map, Lock, Drum,
        Guitar, Scope, Camera, Torch, Rocks, Log, Fruit, Cord, Bars, Pill, Crystal, Zz,
    }

    private readonly record struct SGlyph(GShape S, Color C, float W, float H, int V);

    // 부품 = 모양 색 너비 높이 변형
    private const string GlyphTable =
        "amp Speaker 3a3f4a 0.8 0.9 1|ant Critter 2b2018 0.5 0.3 2|antenna Antenna 9aa6b5 0.5 1.3 0|arch Arch e8d8b0 1.3 1.2 0|arrow Arrow e05a3a 0.8 0.4 0|ash Rocks 8a8a8a 0.6 0.25 1|" +
        "badge Disc d4a83a 0.4 0.4 2|balance Stick c0a060 0.9 0.8 3|ball Ball e8e8e8 0.5 0.5 0|balloon Ball f06a8a 0.45 0.6 1|ballot Box c8c8d0 0.8 0.7 3|banner Bunting d04a4a 1.4 0.5 0|" +
        "banner2 Bunting 3a7ad0 1.4 0.5 0|barrel Cask 8a5a2b 0.9 1.1 0|bars Bars 6b3a20 0.7 0.4 0|beacon Lamp e04a3a 0.5 0.6 3|bean Fruit 6b4423 0.6 0.4 3|bell Cup d4a83a 0.5 0.6 3|" +
        "bench Seat 8a6a4a 1.3 0.6 1|bin Box 5a6878 0.8 0.7 4|blanket Drape 6a7ab0 1.1 0.7 0|blanket2 Drape b06a6a 1.0 0.6 0|board Mat e8e0c8 0.8 0.8 3|bolt Bolt f5d547 0.5 0.8 0|" +
        "bolt2 Stick 9aa6b5 0.3 0.6 10|book Book 8a3a3a 0.6 0.7 0|boots Boot 4a4a50 0.8 0.5 0|bottle Bottle 5a8a5a 0.4 1.0 0|bowl Mug e8e0d0 0.7 0.4 2|box Box a08060 0.9 0.7 0|" +
        "box2 Box c0a070 0.8 0.5 1|box3 Box 8a8a9a 0.8 0.7 8|bracket Rack 9aa6b5 0.6 0.6 2|brush Stick c08040 0.8 0.3 5|cabbage Fruit 8ac06a 0.6 0.6 1|cage Glass c0c0c0 0.8 0.9 1|" +
        "cake Cake f0d0e0 0.9 0.8 0|camera Camera 3a3a40 0.7 0.5 0|candle Candle f0e8d0 0.3 0.8 0|candle2 Candle f0a0c0 0.5 0.5 1|candle3 Candle d4a83a 0.4 0.9 2|candle4 Candle f0f0f0 0.4 0.3 3|" +
        "cap Hat 2a3a6a 0.7 0.4 1|capsule Capsule b0b8c0 0.9 0.6 0|cards Cards f0f0f0 0.8 0.6 0|carrot Fruit f08a2a 0.6 1.1 2|chair Seat 6a7a8a 0.6 0.8 0|chairs Seat 5a6a7a 1.4 0.7 2|" +
        "chalk Stick f0f0f0 0.4 0.2 11|chalkboard Sign 2a4a3a 1.0 1.1 2|chart Sheet f0f0e8 0.7 0.8 3|chip Bars 2a3a2a 0.5 0.4 2|chips Disc d04a4a 0.5 0.6 1|cigarette Stick f0f0f0 0.6 0.2 12|" +
        "clipboard Sheet c0a070 0.6 0.8 1|clock Clock f0f0f0 0.5 0.5 0|clock2 Clock 6a4a2a 0.8 0.5 1|clock3 Clock e0e0e0 0.5 0.5 2|cloth Drape c0b090 1.0 0.6 1|coil Coil c79a4a 0.7 0.8 0|" +
        "coin Disc d4a83a 0.35 0.35 0|coin2 Disc c0c8d0 0.35 0.35 4|coin3 Disc d4a83a 0.45 0.5 5|comb Stick 3a3a3a 0.6 0.25 13|compass Clock c0a060 0.5 0.5 3|confetti Sparkle f0a0c0 0.9 0.9 1|" +
        "console Console 3a4a5a 1.0 0.8 0|corkboard Sign b08050 1.0 0.9 3|crate Crate 8a6a40 0.9 0.8 0|crate2 Crate 6a7a5a 1.0 0.8 1|cross Cross d03a3a 0.6 0.6 0|crumb Sparkle a07040 0.6 0.4 2|" +
        "cup Mug e8e0d0 0.45 0.5 0|cups Mug c08a6a 0.8 0.5 1|curtain Drape a02a3a 1.2 1.2 2|curtain2 Drape 7a6ab0 1.1 1.1 3|cushion Cushion d06a8a 0.6 0.4 0|damp Splat 4a6a7a 0.9 0.5 1|" +
        "dial Clock e8e8e8 0.6 0.4 4|dice Dice f0f0f0 0.6 0.4 0|disco Ball c0c8d0 0.5 0.5 2|dish Dish c8ccd0 0.9 0.9 0|drawer Box 8a7050 0.8 0.6 5|drip Drop d8c070 0.3 0.4 0|" +
        "drop Drop 3a6ad0 0.3 0.4 1|drop2 Drop 8ac8f0 0.3 0.4 2|drum Drum c04a3a 0.7 0.7 0|drumcage Drum c0c0c0 0.8 0.8 1|duck Critter f0d040 0.5 0.45 3|duct Rack 7a8a9a 0.9 0.6 3|" +
        "dumbbell Bars 4a4a4a 0.9 0.4 3|dye Bottle b03ab0 0.3 0.6 1|eye Eye ffffff 0.6 0.35 0|eye2 Eye e04040 0.5 0.35 1|eyes Eye 60e0ff 0.6 0.3 2|face Face e0b090 0.6 0.6 0|" +
        "fairy Bunting f8e080 1.3 0.4 2|feet Boot 3a3a3a 0.8 0.6 1|fist Hand e0b090 0.5 0.7 1|flame Flame f08030 0.5 0.8 0|flash Sparkle ffffa0 0.8 0.8 3|flask Bottle a0d0e0 0.5 0.8 2|" +
        "flower Flower f06a9a 0.6 0.9 0|foam Puff f8f8f8 0.6 0.4 1|fold Sheet e8e8f0 0.8 0.5 4|footprint Boot 5a5a5a 0.9 0.6 2|fort Cushion c0a0d0 1.2 0.8 2|frame Frame 8a6a3a 0.7 0.8 0|" +
        "frame2 Frame 3a3a3a 0.6 0.5 1|frame3 Frame c0a060 0.5 0.6 2|frame4 Frame 6a4a3a 0.55 0.45 3|fruit Fruit d03a4a 0.6 0.5 0|fuel Cask c04a2a 0.7 0.9 1|gear Gear 8a9aaa 0.6 0.6 0|" +
        "ghost Ghost e8eef8 0.6 0.8 0|gift Box d04a6a 0.7 0.6 6|glass Glass a0c8d8 0.9 0.7 0|globe Globe 4a8ac0 0.5 0.6 0|glove Hand f0e0a0 0.6 0.7 2|glue Bottle f0f0f0 0.4 0.5 3|" +
        "guitar Guitar c08040 0.6 1.1 0|hand Hand e0b090 0.5 0.6 0|hands Hand d0a080 0.8 0.5 3|handle Stick 9aa6b5 0.6 0.4 8|handset Mic 3a3a3a 0.6 0.4 1|hat Hat 2a2a2a 0.6 0.6 0|" +
        "headset Dome 3a3a40 0.6 0.5 2|heart Heart e04a6a 0.5 0.5 0|helmet Dome e8e8e8 0.6 0.55 0|herb Leafy 6a9a4a 0.6 0.7 2|horn Speaker f0c030 0.6 0.4 2|incense Candle a06a3a 0.3 0.8 4|" +
        "ink Bottle 2a2a4a 0.4 0.4 4|jar Jar c0a080 0.6 0.8 0|jug Jar a07050 0.7 0.9 1|kettle Kettle 8a8a8a 0.6 0.7 1|key Stick d4a83a 0.6 0.3 1|ladder Rack a08060 0.5 1.3 4|" +
        "ladle Stick c0c0c0 0.6 0.4 7|lamp Lamp b070f0 0.9 0.6 0|lamp2 Lamp e0c060 0.5 0.7 1|lamp3 Lamp f0e0a0 0.4 0.7 2|lamp4 Lamp f0d090 0.5 1.2 4|lane Mat 3a6a8a 1.4 0.5 4|" +
        "lantern Lamp f08040 0.5 0.6 5|laugh Face f0d040 0.55 0.55 1|leaf Leafy 6ab04a 0.4 0.4 3|leafy Leafy 4a9a4a 0.8 0.9 0|ledger Book 3a5a3a 0.8 0.5 1|ledger2 Book 5a3a5a 0.6 0.5 2|" +
        "letter Sheet f0e8d0 0.7 0.45 2|lid Disc 9a9a9a 0.5 0.3 6|lock Lock c0a040 0.45 0.6 0|locker Box 6a7a8a 0.6 1.1 7|log Log 7a5a3a 1.0 0.5 0|map Map d8c898 0.9 0.7 0|" +
        "map2 Map 1a2a4a 0.9 0.7 1|mat Mat 7a5a9a 1.2 0.5 0|medal Cup d4a83a 0.4 0.7 2|memo Sheet f8e060 0.45 0.45 5|mic Mic 3a3a3a 0.4 0.7 0|mic2 Mic 2a2a2a 0.4 1.2 2|" +
        "model Capsule c0c8d0 0.9 0.6 1|monitor Screen 2a3a4a 0.9 0.8 0|mouse Critter b0a090 0.45 0.3 0|mushroom Mushroom c06a4a 0.6 0.6 0|mustache Cord 2a2a2a 0.6 0.25 1|needle Stick c0c0c0 0.5 0.2 2|" +
        "needles Stick d0a070 0.7 0.7 14|net Net e0e0e0 1.0 0.8 0|note Note 2a2a2a 0.35 0.5 0|note2 Sheet f8d0e0 0.5 0.4 2|note3 Note 4a2a6a 0.35 0.5 1|ore Crystal 8ab0d0 0.6 0.6 0|" +
        "paint Splat d04a4a 1.0 0.7 0|pan Pan 3a3a3a 0.8 0.4 0|paper Sheet f0f0e8 0.6 0.8 0|paper2 Sheet e8e8f8 0.5 0.6 6|paper3 Sheet f0e8d8 0.5 0.7 7|part Gear 9a8a7a 0.5 0.5 2|" +
        "pawn Bottle f0f0f0 0.35 0.6 5|pen Stick 2a4a8a 0.6 0.2 4|pen2 Stick 8a2a2a 0.6 0.2 4|pen3 Stick 2a2a2a 0.6 0.2 4|pencil Stick f0c030 0.7 0.2 16|pennant Bunting e0a030 0.7 0.6 1|" +
        "petal Flower f0a0c0 0.7 0.5 1|photo Frame f8f8f8 0.45 0.5 4|photo2 Frame f8f8f8 0.4 0.45 5|photo3 Frame f0f0e0 0.4 0.5 6|pill Pill f0f0f0 0.4 0.3 0|pillow Cushion e8e0f0 0.8 0.5 1|" +
        "pillow2 Cushion d0b0e0 0.6 0.5 3|pin Stick d03a3a 0.3 0.4 17|placard Sign f0e8d0 0.9 1.2 0|plate Disc f0f0f0 0.6 0.4 7|pocket Drape 5a6a7a 0.5 0.5 4|pod Capsule d0d0d0 1.1 0.9 2|" +
        "poem Sheet f8f0e0 0.6 0.7 8|popcorn Mug e04a3a 0.5 0.6 4|poster Sign f0e8d0 0.8 1.0 4|poster2 Sign e0d0f0 0.8 1.0 5|pot Pot b0603a 0.6 0.5 0|puzzle Mat 8ab0d0 1.0 0.7 5|" +
        "rack Rack a08060 0.9 0.9 0|ration Bars a0a060 0.7 0.4 1|recliner Seat 6a6a8a 0.9 0.7 3|reel Wheel 3a3a3a 0.6 0.6 1|ribbon Cord d04a6a 0.5 0.4 2|ring Ring d4a83a 0.4 0.4 0|" +
        "robot Robot a0a8b0 0.6 0.8 0|rooster Critter d04a3a 0.6 0.7 4|rope Cord a08050 0.7 0.5 0|rose Flower d02a3a 0.5 0.9 2|rug Mat a04a3a 1.2 0.8 1|rug2 Mat 4a7a9a 1.0 0.8 2|" +
        "salt Jar f0f0f0 0.35 0.6 2|scarf Drape d06a3a 1.0 0.4 5|scope Scope 6a7a8a 0.9 1.0 0|screen Screen f0f0f0 1.2 0.9 1|scroll Sheet e8e0c0 0.7 0.6 9|sheet Drape f0f0f8 1.0 0.6 6|" +
        "sheet2 Sheet f8f8f0 0.6 0.7 10|shelf Rack 8a6a4a 1.0 1.1 1|shelf2 Rack a07a50 1.0 0.5 5|shoe Boot e0e0e0 0.6 0.4 3|shower Drop c0c8d0 0.6 0.9 3|shuttle Capsule a0a8b8 1.2 0.7 3|" +
        "sig Cord 2a2a6a 0.6 0.3 3|sign Sign f0c030 0.7 0.7 6|slot Sheet f0f0f0 0.4 0.5 11|smile Face f8d830 0.5 0.5 2|smoke Puff 9a9a9a 0.6 0.9 0|snack Bars d06a3a 0.6 0.4 4|" +
        "snail Critter 9a7a4a 0.6 0.45 5|soil Rocks 5a3a20 0.6 0.4 2|sparkle Sparkle f8f0a0 0.8 0.8 0|speaker Speaker 4a4a5a 0.6 0.7 0|spider Critter 1a1a1a 0.5 0.4 1|spoon Stick c0c0c0 0.6 0.25 6|" +
        "spot Lamp f8f0c0 0.7 1.0 6|spray Bottle 3a8a5a 0.35 0.7 6|spring Coil 9aa6b5 0.4 0.7 1|stage Mat 6a4a3a 1.4 0.6 6|stamp Stick 8a3a3a 0.4 0.5 18|stand Rack 3a3a4a 0.9 0.7 6|" +
        "staple Box 4a4a5a 0.6 0.3 9|star Star f0c030 0.45 0.45 0|star2 Star f8f8f0 0.4 0.4 1|star3 Star f0d050 0.6 0.5 2|star4 Star d4a83a 0.45 0.45 3|star5 Star 2a2a4a 0.4 0.4 4|" +
        "star6 Star a0c0f0 0.7 0.6 5|starlight Star c0d8ff 0.6 0.6 6|steam Puff f0f0f8 0.6 0.9 2|stone Rocks 7a7a7a 0.5 0.4 0|stones Rocks 6a6a70 0.8 0.5 3|stool Seat 8a6a4a 0.5 0.6 4|" +
        "stopwatch Clock d0d0d0 0.45 0.5 5|string Cord e0e0e0 0.5 0.8 4|sugar Jar f0f0f8 0.6 0.7 3|suit Suit e8e8e8 0.8 1.2 0|suitcase Box 6a4a3a 0.9 0.7 10|swap Arrow 4aa06a 0.7 0.7 1|" +
        "swap2 Arrow 4a8ad0 0.7 0.5 2|tablet Seat 8a6a4a 0.9 0.6 5|tag Sheet f0e0a0 0.35 0.4 12|tagline Splat 3a3ad0 0.9 0.5 2|tape Cord f0d030 0.8 0.5 5|teapot Kettle e0e0f0 0.6 0.6 0|" +
        "teapot2 Kettle 4a7ab0 0.5 0.5 2|tether Cord f0f0f0 0.7 0.6 6|thermo Stick e04040 0.5 0.2 20|ticket Sheet f0c0a0 0.5 0.3 13|tin Cask a0a0a8 0.45 0.55 2|tool Stick c04a3a 0.6 0.2 21|" +
        "torch Torch 4a4a50 0.6 0.4 0|towel Drape 4ab0b0 0.8 0.5 7|towel2 Drape f0a0a0 0.6 0.4 8|trophy Cup d4a83a 0.5 0.8 1|tunnel Cord 8a6a40 0.7 0.5 7|twine Cord a08060 0.9 0.5 8|" +
        "twine2 Cord 9a8060 1.0 0.5 9|wave Wave 80e0ff 0.8 0.8 0|wave2 Wave a0ffc0 0.8 0.5 1|web Net d0d0d0 0.8 0.8 1|wheel Wheel 9aa6b5 0.6 0.6 0|wire Cord 3a3a3a 0.8 0.5 10|" +
        "wok Pan 2a2a2a 0.8 0.5 1|wrench Stick 9aa6b5 0.6 0.25 0|yarn Ball c04a7a 0.45 0.45 3|zz Zz e0e8ff 0.5 0.6 0|stamp2 Stick 8a3a3a 0.4 0.5 18";

    private static Dictionary<string, SGlyph>? _glyphs;

    private static Dictionary<string, SGlyph> SGlyphs
    {
        get
        {
            if (_glyphs != null) return _glyphs;
            var d = new Dictionary<string, SGlyph>();
            foreach (var row in GlyphTable.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var f = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 6 || !Enum.TryParse<GShape>(f[1], out var sh)) continue;
                d[f[0]] = new SGlyph(sh, new Color("#" + f[2]), float.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture), int.Parse(f[5]));
            }
            return _glyphs = d;
        }
    }

    /// <summary>표의 부품 하나를 그린다 (p = 바닥 가운데 · s = 크기 px · grow = 만든 정도 0~1).</summary>
    private void DrawGlyph(CanvasItem ci, string token, Vector2 p, float s, float alpha, bool close, int seed)
    {
        if (!SGlyphs.TryGetValue(token, out var g)) { ci.DrawCircle(p, s * 0.3f, new Color(0.6f, 0.6f, 0.6f, alpha)); return; }
        var c = g.C.WithAlpha(alpha);
        var dk = g.C.Darkened(0.4f).WithAlpha(alpha);
        var lt = g.C.Lightened(0.35f).WithAlpha(alpha);
        float w = g.W * s, h = g.H * s, t = _time + seed * 0.37f;
        int v = g.V;
        Vector2 V(float x, float y) => p + new Vector2(x * w, -y * h);
        void L(Vector2 a, Vector2 b, Color col, float wd = 1.2f) => ci.DrawLine(a, b, col, wd, true);
        void R(float x0, float y0, float x1, float y1, Color col) { var a = V(x0, y1); var b = V(x1, y0); ci.DrawRect(new Rect2(new Vector2(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y)), new Vector2(Mathf.Abs(b.X - a.X), Mathf.Abs(b.Y - a.Y))), col); }
        void E(float x, float y, float rx, float ry, Color col) => ci.DrawColoredPolygon(Ellipse(V(x, y), Mathf.Max(0.6f, rx * w), Mathf.Max(0.6f, ry * h), 12), col);
        void C(float x, float y, float r, Color col) => ci.DrawCircle(V(x, y), Mathf.Max(0.5f, r * s), col);
        void P(Color col, params Vector2[] pts) => ci.DrawColoredPolygon(pts, col);
        var white = new Color(1, 1, 1, 0.8f * alpha);
        var ink = new Color(0.1f, 0.1f, 0.12f, 0.85f * alpha);
        switch (g.S)
        {
            case GShape.Cask:
                if (v == 1) { R(-0.45f, 0f, 0.45f, 0.9f, c); R(-0.2f, 0.9f, 0.2f, 1f, dk); L(V(-0.35f, 0.2f), V(0.35f, 0.7f), dk); L(V(-0.35f, 0.7f), V(0.35f, 0.2f), dk); break; }
                if (v == 2) { R(-0.45f, 0f, 0.45f, 0.9f, c); for (int i = 1; i < 4; i++) L(V(-0.45f, i * 0.22f), V(0.45f, i * 0.22f), dk, 0.8f); E(0f, 0.9f, 0.45f, 0.1f, lt); break; }
                E(0f, 0.5f, 0.55f, 0.5f, c); R(-0.45f, 0.1f, 0.45f, 0.9f, c);
                L(V(-0.5f, 0.3f), V(0.5f, 0.3f), dk, 2f); L(V(-0.5f, 0.72f), V(0.5f, 0.72f), dk, 2f); E(0f, 0.92f, 0.45f, 0.1f, lt);
                if (close) { C(0.15f, 0.5f, 0.06f, ink); for (int i = -1; i <= 1; i++) L(V(i * 0.2f, 0.12f), V(i * 0.2f, 0.88f), dk.WithAlpha(0.4f * alpha), 0.7f); }
                break;
            case GShape.Jar:
                if (v == 2) { R(-0.3f, 0f, 0.3f, 0.75f, c); E(0f, 0.8f, 0.3f, 0.12f, lt); for (int i = -1; i <= 1; i++) C(i * 0.12f, 0.85f, 0.03f, ink); break; }
                if (v == 3) { E(0f, 0.4f, 0.5f, 0.4f, c); R(-0.25f, 0.75f, 0.25f, 0.95f, dk); R(-0.3f, 0.3f, 0.3f, 0.5f, white); break; }
                E(0f, 0.4f, 0.5f, 0.4f, c); R(-0.25f, 0.7f, 0.25f, 0.85f, c); R(-0.3f, 0.85f, 0.3f, 0.95f, dk);
                if (v == 1) ci.DrawArc(V(0.5f, 0.45f), 0.22f * w, -1.4f, 1.4f, 8, dk, 2f, true);
                if (close) L(V(-0.3f, 0.5f), V(-0.2f, 0.2f), lt, 1.5f);
                break;
            case GShape.Bottle:
                switch (v)
                {
                    case 2: C(0f, 0.3f, 0.32f * g.W, c); R(-0.12f, 0.5f, 0.12f, 1f, c); R(-0.14f, 0.95f, 0.14f, 1.05f, dk); break;
                    case 3: P(c, V(-0.4f, 0.1f), V(0.3f, 0.6f), V(0.4f, 0.45f), V(-0.3f, 0f)); R(0.3f, 0.5f, 0.45f, 0.7f, new Color(0.9f, 0.3f, 0.2f, alpha)); break;
                    case 4: E(0f, 0.25f, 0.5f, 0.25f, c); R(-0.2f, 0.4f, 0.2f, 0.6f, dk); L(V(0.1f, 0.5f), V(0.5f, 1.3f), lt, 1f); break;
                    case 5: C(0f, 0.8f, 0.18f * g.W * 2f, c); P(c, V(-0.2f, 0.65f), V(0.2f, 0.65f), V(0.45f, 0f), V(-0.45f, 0f)); R(-0.5f, 0f, 0.5f, 0.1f, dk); break;
                    case 6: R(-0.35f, 0f, 0.35f, 0.8f, c); R(-0.1f, 0.8f, 0.1f, 0.95f, dk); for (int i = 0; i < 4; i++) C(0.4f + i * 0.2f, 0.95f + Mathf.Sin(t * 3 + i) * 0.05f, 0.04f, c.WithAlpha(0.5f * alpha)); break;
                    default:
                        R(-0.35f, 0f, 0.35f, 0.6f, c); P(c, V(-0.35f, 0.6f), V(0.35f, 0.6f), V(0.15f, 0.75f), V(-0.15f, 0.75f)); R(-0.15f, 0.75f, 0.15f, 0.95f, c);
                        R(-0.12f, 0.95f, 0.12f, 1.05f, new Color(0.6f, 0.45f, 0.3f, alpha));
                        if (v == 1) { R(-0.05f, 1.05f, 0.05f, 1.3f, ink); }
                        if (close) R(-0.3f, 0.2f, 0.3f, 0.4f, white.WithAlpha(0.5f * alpha));
                        break;
                }
                break;
            case GShape.Box:
                switch (v)
                {
                    case 1: R(-0.5f, 0f, 0.5f, 0.8f, c); for (int i = 0; i < 3; i++) R(-0.4f + i * 0.28f, 0.2f, -0.2f + i * 0.28f, 0.55f, new Color(0.3f + 0.2f * i, 0.5f, 0.7f - 0.15f * i, alpha)); break;
                    case 3: R(-0.5f, 0f, 0.5f, 0.8f, c); R(-0.3f, 0.78f, 0.3f, 0.85f, ink); R(-0.12f, 0.8f, 0.12f, 1.05f, white); break;
                    case 4: P(c, V(-0.5f, 0.8f), V(0.5f, 0.8f), V(0.4f, 0f), V(-0.4f, 0f)); C(-0.15f, 0.85f, 0.1f, lt); C(0.15f, 0.88f, 0.08f, dk); break;
                    case 5: R(-0.5f, 0f, 0.5f, 0.8f, dk); R(-0.45f, 0.1f, 0.55f, 0.45f, c); R(-0.05f, 0.25f, 0.15f, 0.32f, lt); break;
                    case 6: R(-0.5f, 0f, 0.5f, 0.75f, c); R(-0.08f, 0f, 0.08f, 0.75f, new Color(1f, 0.85f, 0.3f, alpha)); R(-0.5f, 0.35f, 0.5f, 0.45f, new Color(1f, 0.85f, 0.3f, alpha)); C(-0.15f, 0.85f, 0.12f, new Color(1f, 0.85f, 0.3f, alpha)); C(0.15f, 0.85f, 0.12f, new Color(1f, 0.85f, 0.3f, alpha)); break;
                    case 7: R(-0.45f, 0f, 0.45f, 1f, c); for (int i = 0; i < 3; i++) L(V(-0.3f, 0.85f - i * 0.08f), V(0.3f, 0.85f - i * 0.08f), dk, 1f); R(0.25f, 0.4f, 0.35f, 0.55f, lt); break;
                    case 8: R(-0.5f, 0f, 0.5f, 0.85f, c); R(-0.25f, 0.65f, 0.25f, 0.7f, ink); break;
                    case 9: P(c, V(-0.5f, 0f), V(0.5f, 0f), V(0.5f, 0.4f), V(-0.4f, 0.7f)); L(V(-0.4f, 0.55f), V(0.45f, 0.3f), dk, 1f); break;
                    case 10: R(-0.5f, 0f, 0.5f, 0.75f, c); ci.DrawArc(V(0f, 0.75f), 0.18f * w, Mathf.Pi, Mathf.Tau, 8, dk, 2f, true); L(V(-0.25f, 0f), V(-0.25f, 0.75f), dk, 1.5f); L(V(0.25f, 0f), V(0.25f, 0.75f), dk, 1.5f); break;
                    default: R(-0.5f, 0f, 0.5f, 0.75f, c); R(-0.52f, 0.68f, 0.52f, 0.8f, dk); R(-0.08f, 0f, 0.08f, 0.8f, new Color(0.85f, 0.75f, 0.45f, alpha)); if (close) L(V(-0.45f, 0.1f), V(0.45f, 0.6f), dk.WithAlpha(0.4f * alpha), 0.7f); break;
                }
                break;
            case GShape.Crate:
                R(-0.5f, 0f, 0.5f, 0.85f, c); ci.DrawRect(new Rect2(V(-0.5f, 0.85f), new Vector2(w, 0.85f * h)), dk, false, 1.5f);
                L(V(-0.5f, 0f), V(0.5f, 0.85f), dk, 1.5f); if (v == 0) L(V(-0.5f, 0.85f), V(0.5f, 0f), dk, 1.5f);
                for (int i = 1; i < 3; i++) L(V(-0.5f, i * 0.28f), V(0.5f, i * 0.28f), dk.WithAlpha(0.6f * alpha), 1f);
                break;
            case GShape.Pot:
                P(c, V(-0.5f, 0.6f), V(0.5f, 0.6f), V(0.35f, 0f), V(-0.35f, 0f)); R(-0.55f, 0.55f, 0.55f, 0.7f, dk); E(0f, 0.68f, 0.45f, 0.06f, new Color(0.3f, 0.2f, 0.1f, alpha));
                break;
            case GShape.Leafy:
            {
                float sway = Mathf.Sin(t * 1.3f) * 0.05f;
                if (v == 2) { L(V(0f, 1f), V(0f, 0.75f), new Color(0.6f, 0.5f, 0.3f, alpha)); for (int i = 0; i < 5; i++) { var a = V(0f, 0.75f); var b = a + new Vector2(Mathf.Sin(-0.9f + i * 0.45f) * w * 0.5f, h * 0.5f); L(a, b, c, 2.2f); } break; }
                if (v == 3) { P(c, V(-0.5f, 0.2f), V(0f, 0.9f), V(0.5f, 0.3f), V(0f, 0f)); L(V(0f, 0f), V(0f, 0.85f), dk, 0.8f); break; }
                for (int i = 0; i < 5; i++)
                {
                    float ang = -1.1f + i * 0.55f + sway;
                    var tip = p + new Vector2(Mathf.Sin(ang) * w * 0.6f, -Mathf.Cos(ang) * h);
                    var mid = (p + tip) * 0.5f;
                    var n = (tip - p).Orthogonal().Normalized() * w * 0.12f;
                    P(i % 2 == 0 ? c : lt, p, mid + n, tip, mid - n);
                    if (close) L(p, tip, dk.WithAlpha(0.5f * alpha), 0.6f);
                }
                break;
            }
            case GShape.Flower:
                if (v == 1) { for (int i = 0; i < 6; i++) E(-0.4f + (i * 37 % 9) * 0.1f, 0.05f + (i * 13 % 5) * 0.05f, 0.12f, 0.06f, c); break; }
                L(V(0f, 0f), V(0f, 0.6f), new Color(0.3f, 0.6f, 0.3f, alpha), 1.5f);
                if (v == 2) { C(0f, 0.75f, 0.2f, c); ci.DrawArc(V(0f, 0.75f), 0.12f * s, 0f, 5f, 10, dk, 1.2f, true); P(new Color(0.3f, 0.6f, 0.3f, alpha), V(0f, 0.35f), V(0.3f, 0.45f), V(0.05f, 0.3f)); break; }
                for (int i = 0; i < 5; i++) { var d = Vector2.FromAngle(i * Mathf.Tau / 5 + t * 0.2f) * s * 0.2f; ci.DrawCircle(V(0f, 0.75f) + d, s * 0.13f, c); }
                C(0f, 0.75f, 0.09f, new Color(1f, 0.85f, 0.3f, alpha));
                break;
            case GShape.Mushroom:
                R(-0.15f, 0f, 0.15f, 0.45f, new Color(0.9f, 0.85f, 0.75f, alpha));
                P(c, V(-0.5f, 0.42f), V(-0.35f, 0.75f), V(0f, 0.88f), V(0.35f, 0.75f), V(0.5f, 0.42f));
                C(-0.2f, 0.62f, 0.05f, white); C(0.15f, 0.7f, 0.04f, white);
                break;
            case GShape.Coil:
                if (v == 1) { var pts = new Vector2[9]; for (int i = 0; i < 9; i++) pts[i] = V(i % 2 == 0 ? -0.4f : 0.4f, i / 8f); ci.DrawPolyline(pts, c, 1.6f, true); break; }
                for (int i = 0; i < 4; i++) ci.DrawArc(V(0f, 0.15f + i * 0.2f), 0.4f * w, 0f, Mathf.Pi, 10, i % 2 == 0 ? c : dk, 2f, true);
                L(V(0.4f, 0.75f), V(0.6f, 1f), c, 2f);
                break;
            case GShape.Drop:
            {
                if (v == 3) { C(0f, 1f, 0.15f, c); for (int i = -2; i <= 2; i++) { float yy = (t * 2f + i * 0.3f) % 1f; L(V(i * 0.1f, 0.9f - yy * 0.8f), V(i * 0.14f, 0.85f - yy * 0.8f), new Color(0.6f, 0.8f, 1f, 0.7f * alpha)); } break; }
                float fall = (t * 0.6f) % 1f;
                var dp = V(0f, 0.9f - fall * 0.8f);
                ci.DrawCircle(dp, s * 0.14f * g.W * 2f, c);
                P(c, dp + new Vector2(-s * 0.12f, 0), dp + new Vector2(s * 0.12f, 0), dp + new Vector2(0, -s * 0.3f));
                break;
            }
            case GShape.Flame:
            {
                float f = 1f + 0.15f * Mathf.Sin(t * 9f);
                P(c, V(-0.4f, 0f), V(0.4f, 0f), V(0.15f, 0.6f * f), V(0f, 1f * f), V(-0.2f, 0.55f));
                P(new Color(1f, 0.9f, 0.4f, alpha), V(-0.2f, 0f), V(0.2f, 0f), V(0f, 0.55f * f));
                break;
            }
            case GShape.Puff:
                if (v == 1) { for (int i = 0; i < 5; i++) C(-0.35f + i * 0.18f, 0.15f + (i % 2) * 0.12f, 0.16f, c); break; }
                for (int i = 0; i < 4; i++)
                {
                    float k = (t * 0.4f + i * 0.25f) % 1f;
                    var q = V(Mathf.Sin(k * 6f + i) * 0.25f, k);
                    if (v == 2) ci.DrawArc(q, s * 0.15f, 0f, Mathf.Pi, 6, c.WithAlpha((1f - k) * 0.7f * alpha), 1.2f, true);
                    else ci.DrawCircle(q, s * (0.12f + 0.15f * k), c.WithAlpha((1f - k) * 0.6f * alpha));
                }
                break;
            case GShape.Disc:
                switch (v)
                {
                    case 1: for (int i = 0; i < 4; i++) E(0f, 0.1f + i * 0.13f, 0.5f, 0.15f, i % 2 == 0 ? c : white); break;
                    case 5: for (int i = 0; i < 3; i++) E(0f, 0.1f + i * 0.12f, 0.5f, 0.15f, i % 2 == 0 ? c : dk); break;
                    case 6: E(0f, 0.15f, 0.5f, 0.2f, c); C(0f, 0.25f, 0.06f, dk); break;
                    case 7: E(0f, 0.2f, 0.5f, 0.25f, c); ci.DrawArc(V(0f, 0.2f), 0.3f * w, 0f, Mathf.Tau, 14, dk.WithAlpha(0.4f * alpha), 1f, true); break;
                    case 2: C(0f, 0.5f, 0.35f * g.W * 2f, c); C(0f, 0.5f, 0.2f * g.W * 2f, new Color(0.8f, 0.2f, 0.2f, alpha)); break;
                    default: C(0f, 0.5f, 0.4f * g.W * 2f, c); ci.DrawArc(V(0f, 0.5f), 0.3f * s * g.W * 2f, 0f, Mathf.Tau, 12, dk, 1f, true); break;
                }
                break;
            case GShape.Sheet:
                switch (v)
                {
                    case 2: R(-0.5f, 0f, 0.5f, 0.7f, c); ci.DrawPolyline(new[] { V(-0.5f, 0.7f), V(0f, 0.3f), V(0.5f, 0.7f) }, dk, 1f, true); if (g.C.R > 0.95f && g.C.G < 0.85f) C(0f, 0.35f, 0.08f, new Color(0.9f, 0.3f, 0.4f, alpha)); break;
                    case 4: R(-0.5f, 0f, 0.5f, 0.5f, c); L(V(-0.5f, 0.25f), V(0.5f, 0.25f), dk, 1f); break;
                    case 5: R(-0.5f, 0f, 0.5f, 1f, c); P(dk, V(0.5f, 0f), V(0.25f, 0f), V(0.5f, 0.25f)); L(V(-0.3f, 0.7f), V(0.3f, 0.7f), ink, 1f); L(V(-0.3f, 0.45f), V(0.2f, 0.45f), ink, 1f); break;
                    case 9: R(-0.4f, 0f, 0.4f, 0.8f, c); E(0f, 0.8f, 0.45f, 0.08f, dk); E(0f, 0f, 0.45f, 0.08f, dk); for (int i = 0; i < 3; i++) L(V(-0.3f, 0.2f + i * 0.2f), V(0.3f, 0.2f + i * 0.2f), ink, 0.8f); break;
                    case 10: R(-0.5f, 0f, 0.5f, 1f, c); for (int i = 0; i < 5; i++) L(V(-0.45f, 0.3f + i * 0.1f), V(0.45f, 0.3f + i * 0.1f), ink, 0.6f); C(-0.2f, 0.45f, 0.05f, ink); C(0.15f, 0.55f, 0.05f, ink); break;
                    case 11: R(-0.5f, 0.3f, 0.5f, 1f, c); L(V(-0.6f, 0.3f), V(0.6f, 0.3f), ink, 2f); break;
                    case 12: R(-0.5f, 0f, 0.5f, 0.8f, c); C(0f, 0.65f, 0.08f, dk); L(V(0f, 0.7f), V(0.3f, 1.3f), new Color(0.8f, 0.8f, 0.8f, alpha), 0.8f); break;
                    case 13: R(-0.5f, 0f, 0.5f, 1f, c); for (int i = 0; i < 3; i++) C(0.5f, 0.2f + i * 0.3f, 0.06f, new Color(0.1f, 0.1f, 0.1f, 0.6f * alpha)); break;
                    default:
                        R(-0.5f, 0f, 0.5f, 1f, c);
                        if (v == 1) R(-0.2f, 0.92f, 0.2f, 1.05f, new Color(0.7f, 0.7f, 0.75f, alpha));
                        if (v == 6) R(-0.45f, 0.05f, 0.55f, 1.05f, c.Darkened(0.1f));
                        if (v == 3) ci.DrawPolyline(new[] { V(-0.4f, 0.2f), V(-0.1f, 0.5f), V(0.1f, 0.35f), V(0.4f, 0.8f) }, new Color(0.9f, 0.3f, 0.3f, alpha), 1.4f, true);
                        else for (int i = 0; i < 4; i++)
                            {
                                float yy = 0.8f - i * 0.18f;
                                if (v == 8) ci.DrawPolyline(new[] { V(-0.4f, yy), V(-0.1f, yy + 0.04f), V(0.2f, yy - 0.03f), V(0.4f, yy) }, ink, 0.7f, true);
                                else L(V(-0.4f, yy), V(0.4f - (i == 3 ? 0.3f : 0f), yy), ink.WithAlpha(0.5f * alpha), 0.7f);
                            }
                        if (v == 7) P(new Color(0, 0, 0, 0.0f), V(0, 0), V(0.1f, 0), V(0, 0.1f));
                        break;
                }
                break;
            case GShape.Book:
                if (v >= 1) { R(-0.5f, 0f, 0f, 0.6f, c.Lightened(0.6f)); R(0f, 0f, 0.5f, 0.6f, c.Lightened(0.55f)); L(V(0f, 0f), V(0f, 0.6f), dk, 1.5f); for (int i = 0; i < 3; i++) { L(V(-0.4f, 0.15f + i * 0.15f), V(-0.1f, 0.15f + i * 0.15f), c, 0.8f); L(V(0.1f, 0.15f + i * 0.15f), V(0.4f, 0.15f + i * 0.15f), c, 0.8f); } if (v == 1) L(V(0.25f, 0f), V(0.25f, 0.6f), c, 0.6f); break; }
                R(-0.5f, 0f, 0.5f, 0.8f, c); R(-0.5f, 0f, -0.35f, 0.8f, dk); R(0.4f, 0.05f, 0.5f, 0.75f, white);
                break;
            case GShape.Frame:
            {
                float tilt = v == 3 ? 0.15f : 0f;
                var cc = V(0f, 0.5f);
                var hw = new Vector2(w * 0.5f, 0f).Rotated(tilt); var hh = new Vector2(0f, h * 0.5f).Rotated(tilt);
                P(c, cc - hw - hh, cc + hw - hh, cc + hw + hh, cc - hw + hh);
                var iw = hw * 0.75f; var ih = hh * 0.75f;
                var sky = v >= 4 ? new Color(0.5f, 0.7f, 0.9f, alpha) : new Color(0.75f, 0.85f, 0.95f, alpha);
                P(sky, cc - iw - ih, cc + iw - ih, cc + iw + ih, cc - iw + ih);
                if (v is 0 or 2 or 5) { ci.DrawCircle(cc - ih * 0.1f, s * 0.12f, new Color(0.85f, 0.65f, 0.5f, alpha)); P(new Color(0.3f, 0.35f, 0.5f, alpha), cc + ih * 0.75f - iw * 0.5f, cc + ih * 0.75f + iw * 0.5f, cc + ih * 0.15f); }
                else P(new Color(0.35f, 0.55f, 0.35f, alpha), cc + ih - iw, cc + ih + iw, cc + iw * 0.2f);
                break;
            }
            case GShape.Screen:
                if (v == 1) { L(V(-0.3f, 0f), V(0f, 0.5f), dk, 1.5f); L(V(0.3f, 0f), V(0f, 0.5f), dk, 1.5f); R(-0.5f, 0.45f, 0.5f, 1f, c); R(-0.4f, 0.52f, 0.4f, 0.92f, new Color(0.2f + 0.2f * Mathf.Sin(t), 0.3f, 0.5f, alpha)); break; }
                R(-0.05f, 0f, 0.05f, 0.25f, dk); R(-0.3f, 0f, 0.3f, 0.05f, dk); R(-0.5f, 0.25f, 0.5f, 0.95f, c);
                for (int i = 0; i < 3; i++) R(-0.4f, 0.75f - i * 0.15f - (t * 0.05f % 0.15f), 0.1f + (i * 0.17f % 0.3f), 0.8f - i * 0.15f - (t * 0.05f % 0.15f), new Color(0.4f, 1f, 0.6f, 0.7f * alpha));
                break;
            case GShape.Console:
                P(c, V(-0.5f, 0f), V(0.5f, 0f), V(0.4f, 0.8f), V(-0.4f, 0.8f));
                for (int i = 0; i < 6; i++) C(-0.3f + (i % 3) * 0.3f, 0.25f + (i / 3) * 0.3f, 0.05f, ((int)(t * 2f) + i) % 3 == 0 ? new Color(0.3f, 1f, 0.4f, alpha) : new Color(0.9f, 0.5f, 0.2f, 0.7f * alpha));
                break;
            case GShape.Antenna:
                L(V(0f, 0f), V(0f, 1f), c, 1.8f); for (int i = 1; i < 4; i++) L(V(-0.3f + i * 0.05f, i * 0.25f), V(0.3f - i * 0.05f, i * 0.25f), c, 1.2f);
                C(0f, 1f, 0.06f, ((int)(t * 2f)) % 2 == 0 ? new Color(1f, 0.3f, 0.3f, alpha) : dk);
                break;
            case GShape.Dish:
                L(V(0f, 0f), V(0f, 0.4f), dk, 2f); ci.DrawArc(V(0f, 0.75f), 0.45f * w, 0.3f, Mathf.Pi - 0.3f, 12, c, 3f, true); L(V(0f, 0.5f), V(0f, 0.9f), dk, 1f); C(0f, 0.9f, 0.05f, dk);
                break;
            case GShape.Speaker:
                if (v == 2) { P(c, V(-0.5f, 0.3f), V(0.3f, 0.5f), V(0.3f, 0.1f)); ci.DrawArc(V(0.4f, 0.3f), 0.15f * s, -1.5f, 1.5f, 6, dk, 1f, true); break; }
                R(-0.4f, 0f, 0.4f, 0.9f, c); C(0f, 0.3f, 0.2f, dk); C(0f, 0.3f, 0.08f, lt); C(0f, 0.7f, 0.1f, dk);
                if (v == 1) for (int i = 0; i < 3; i++) C(-0.25f + i * 0.25f, 0.82f, 0.04f, lt);
                break;
            case GShape.Mic:
                if (v == 1) { ci.DrawArc(V(0f, 0.3f), 0.5f * w, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 8, c, 3f, true); C(-0.45f, 0.35f, 0.08f, c); C(0.45f, 0.35f, 0.08f, c); break; }
                if (v == 2) { L(V(0f, 0f), V(0f, 0.85f), dk, 1.5f); L(V(-0.3f, 0f), V(0.3f, 0f), dk, 2f); E(0f, 0.92f, 0.12f, 0.08f, c); break; }
                L(V(0f, 0f), V(0f, 0.65f), c, 2.5f); C(0f, 0.8f, 0.12f, dk); if (close) ci.DrawArc(V(0f, 0.8f), 0.1f * s, 0f, Mathf.Tau, 8, lt, 0.6f, true);
                break;
            case GShape.Note:
            {
                var o = new Vector2(0f, -Mathf.Sin(t * 2f) * 2f);
                ci.DrawColoredPolygon(Ellipse(V(-0.2f, 0.15f) + o, 0.2f * w * 1.4f, 0.15f * h, 8), c);
                L(V(0f, 0.15f) + o, V(0f, 0.9f) + o, c, 1.5f); L(V(0f, 0.9f) + o, V(0.35f, 0.7f) + o, c, 1.5f);
                if (v == 1) { ci.DrawColoredPolygon(Ellipse(V(0.4f, 0.1f) + o, 0.2f * w * 1.4f, 0.15f * h, 8), c); L(V(0.6f, 0.1f) + o, V(0.6f, 0.85f) + o, c, 1.5f); L(V(0f, 0.9f) + o, V(0.6f, 0.85f) + o, c, 2f); }
                break;
            }
            case GShape.Star:
            {
                var cc = V(0f, 0.5f);
                if (v == 1) { float k = 0.7f + 0.3f * Mathf.Sin(t * 4f); L(cc + new Vector2(-s * 0.4f * k, 0), cc + new Vector2(s * 0.4f * k, 0), c, 1.5f); L(cc + new Vector2(0, -s * 0.4f * k), cc + new Vector2(0, s * 0.4f * k), c, 1.5f); break; }
                if (v == 2) { for (int i = 0; i < 3; i++) StarPoly(ci, V(-0.4f + i * 0.4f, 0.3f + (i % 2) * 0.4f), s * 0.18f, c); break; }
                if (v == 5) { var pts = new[] { V(-0.4f, 0.2f), V(-0.1f, 0.7f), V(0.2f, 0.4f), V(0.45f, 0.9f) }; ci.DrawPolyline(pts, c.WithAlpha(0.6f * alpha), 0.8f, true); foreach (var q in pts) ci.DrawCircle(q, 1.6f, c); break; }
                if (v == 6) { ci.DrawCircle(cc, s * 0.35f, c.WithAlpha(0.25f * alpha)); StarPoly(ci, cc, s * 0.18f, c); break; }
                if (v == 4) { var pts = StarPts(cc, s * 0.4f); ci.DrawPolyline(new List<Vector2>(pts) { pts[0] }.ToArray(), c, 1.2f, true); break; }
                StarPoly(ci, cc, s * 0.42f * g.W * 2f, c);
                if (v == 3) ci.DrawCircle(cc, s * 0.08f, dk);
                break;
            }
            case GShape.Heart:
                C(-0.22f, 0.65f, 0.2f, c); C(0.22f, 0.65f, 0.2f, c); P(c, V(-0.48f, 0.6f), V(0.48f, 0.6f), V(0f, 0.05f));
                break;
            case GShape.Ring:
                ci.DrawArc(V(0f, 0.4f), 0.3f * s, 0f, Mathf.Tau, 16, c, 2f, true); P(new Color(0.7f, 0.9f, 1f, alpha), V(-0.12f, 0.75f), V(0.12f, 0.75f), V(0f, 0.95f));
                break;
            case GShape.Arch:
                ci.DrawArc(V(0f, 0f), 0.5f * w, Mathf.Pi, Mathf.Tau, 18, c, 3f, true);
                for (int i = 0; i < 7; i++) { var q = V(0f, 0f) + Vector2.FromAngle(Mathf.Pi + i * Mathf.Pi / 6f) * 0.5f * w; ci.DrawCircle(q, 2.2f, i % 2 == 0 ? new Color(1f, 0.6f, 0.75f, alpha) : white); }
                break;
            case GShape.Lamp:
                switch (v)
                {
                    case 0: R(-0.5f, 0.85f, 0.5f, 1f, c.Darkened(0.5f)); P(c.WithAlpha(0.25f * alpha), V(-0.5f, 0.85f), V(0.5f, 0.85f), V(0.7f, 0f), V(-0.7f, 0f)); break;
                    case 1: R(-0.3f, 0f, 0.3f, 0.08f, dk); L(V(0f, 0.05f), V(0.2f, 0.6f), dk, 1.5f); P(c, V(0.05f, 0.6f), V(0.45f, 0.7f), V(0.4f, 0.45f)); ci.DrawCircle(V(0.35f, 0.45f), s * 0.3f, c.WithAlpha(0.15f * alpha)); break;
                    case 2: L(V(0f, 1f), V(0f, 0.6f), dk, 1f); C(0f, 0.5f, 0.12f, c); ci.DrawCircle(V(0f, 0.5f), s * 0.4f, c.WithAlpha(0.15f * alpha)); break;
                    case 3: E(0f, 0.2f, 0.4f, 0.2f, c); var dir = Vector2.FromAngle(t * 3f); P(c.WithAlpha(0.2f * alpha), V(0f, 0.3f), V(0f, 0.3f) + dir.Rotated(-0.25f) * s * 1.5f, V(0f, 0.3f) + dir.Rotated(0.25f) * s * 1.5f); break;
                    case 4: L(V(0f, 0f), V(0f, 0.8f), dk, 1.5f); P(c, V(-0.4f, 0.75f), V(0.4f, 0.75f), V(0.25f, 1f), V(-0.25f, 1f)); ci.DrawCircle(V(0f, 0.7f), s * 0.4f, c.WithAlpha(0.12f * alpha)); break;
                    case 5: E(0f, 0.5f, 0.5f, 0.45f, c); for (int i = -1; i <= 1; i++) L(V(i * 0.25f, 0.1f), V(i * 0.25f, 0.9f), dk.WithAlpha(0.5f * alpha), 0.7f); ci.DrawCircle(V(0f, 0.5f), s * 0.5f, c.WithAlpha(0.15f * alpha)); break;
                    default: P(c.WithAlpha(0.2f * alpha), V(-0.15f, 1f), V(0.15f, 1f), V(0.6f, 0f), V(-0.6f, 0f)); R(-0.15f, 0.95f, 0.15f, 1.1f, dk); break;
                }
                break;
            case GShape.Candle:
            {
                float fl = 1f + 0.2f * Mathf.Sin(t * 10f);
                void Wick(float x, float top) { P(new Color(1f, 0.75f, 0.2f, alpha), V(x - 0.06f, top), V(x + 0.06f, top), V(x, top + 0.18f * fl)); }
                switch (v)
                {
                    case 1: for (int i = -1; i <= 1; i++) { R(i * 0.3f - 0.05f, 0f, i * 0.3f + 0.05f, 0.5f, i == 0 ? c : lt); Wick(i * 0.3f, 0.5f); } break;
                    case 2: R(-0.4f, 0f, 0.4f, 0.08f, c); R(-0.06f, 0.08f, 0.06f, 0.35f, c); R(-0.12f, 0.35f, 0.12f, 0.8f, new Color(0.95f, 0.92f, 0.85f, alpha)); Wick(0f, 0.8f); break;
                    case 3: E(0f, 0.15f, 0.5f, 0.2f, c); Wick(0f, 0.3f); break;
                    case 4: L(V(0f, 0f), V(0f, 0.7f), c, 1.2f); for (int i = 0; i < 3; i++) { float k = (t * 0.3f + i * 0.33f) % 1f; ci.DrawCircle(V(Mathf.Sin(k * 7f) * 0.3f, 0.7f + k * 0.6f), 1.3f, new Color(0.8f, 0.8f, 0.85f, (1f - k) * 0.6f * alpha)); } break;
                    default: R(-0.15f, 0f, 0.15f, 0.8f, c); Wick(0f, 0.8f); break;
                }
                break;
            }
            case GShape.Critter:
            {
                float bob = Mathf.Sin(t * 5f) * 0.03f;
                switch (v)
                {
                    case 1: C(0f, 0.3f, 0.15f, c); for (int i = 0; i < 4; i++) { L(V(0f, 0.3f), V(-0.5f, 0.1f + i * 0.15f), c, 1f); L(V(0f, 0.3f), V(0.5f, 0.1f + i * 0.15f), c, 1f); } break;
                    case 2: for (int i = 0; i < 4; i++) { float k = (t * 0.2f + i * 0.25f) % 1f; ci.DrawCircle(V(-0.5f + k, 0.1f + Mathf.Sin(k * 9f) * 0.1f), 1.4f, c); } break;
                    case 3: E(0f, 0.25f, 0.45f, 0.25f, c); C(0.25f, 0.6f, 0.15f, c); P(new Color(1f, 0.55f, 0.1f, alpha), V(0.4f, 0.6f), V(0.6f, 0.55f), V(0.4f, 0.5f)); C(0.28f, 0.65f, 0.03f, ink); break;
                    case 4: E(0f, 0.35f, 0.35f, 0.3f, new Color(0.9f, 0.85f, 0.7f, alpha)); C(0.3f, 0.7f, 0.12f, new Color(0.9f, 0.85f, 0.7f, alpha)); P(c, V(0.2f, 0.85f), V(0.35f, 1f), V(0.45f, 0.85f)); P(c, V(-0.35f, 0.4f), V(-0.6f, 0.8f), V(-0.5f, 0.3f)); break;
                    case 5: E(0f, 0.12f, 0.5f, 0.12f, new Color(0.7f, 0.65f, 0.5f, alpha)); C(-0.1f, 0.4f, 0.28f, c); ci.DrawArc(V(-0.1f, 0.4f), 0.15f * s, 0f, 5f, 10, dk, 1f, true); break;
                    default:
                        E(0f, 0.3f + bob, 0.45f, 0.3f, c); C(0.4f, 0.45f + bob, 0.1f, c); C(0.35f, 0.62f + bob, 0.07f, lt); C(0.5f, 0.5f + bob, 0.03f, ink);
                        ci.DrawPolyline(new[] { V(-0.45f, 0.3f), V(-0.7f, 0.5f), V(-0.85f, 0.35f) }, c.Darkened(0.2f), 1f, true);
                        break;
                }
                break;
            }
            case GShape.Robot:
            {
                float bob = Mathf.Sin(t * 4f) * 0.04f;
                R(-0.4f, 0.15f + bob, 0.4f, 0.6f + bob, c); R(-0.3f, 0.6f + bob, 0.3f, 0.95f + bob, lt);
                C(-0.12f, 0.78f + bob, 0.06f, new Color(0.4f, 1f, 1f, alpha)); C(0.12f, 0.78f + bob, 0.06f, new Color(0.4f, 1f, 1f, alpha));
                L(V(0f, 0.95f + bob), V(0.1f, 1.15f + bob), dk, 1f); C(0.1f, 1.15f + bob, 0.04f, new Color(1f, 0.4f, 0.4f, alpha));
                C(-0.3f, 0.1f, 0.1f, dk); C(0.3f, 0.1f, 0.1f, dk);
                break;
            }
            case GShape.Wheel:
            {
                var cc = V(0f, 0.5f);
                ci.DrawArc(cc, 0.45f * s, 0f, Mathf.Tau, 16, c, 2f, true);
                for (int i = 0; i < (v == 1 ? 3 : 6); i++) { var d = Vector2.FromAngle(t * (v == 1 ? 0.5f : 3f) + i * Mathf.Tau / (v == 1 ? 3 : 6)); if (v == 1) ci.DrawCircle(cc + d * s * 0.22f, s * 0.1f, dk); else L(cc, cc + d * s * 0.45f, c, 0.8f); }
                if (v == 0) L(V(0f, 0f), cc, dk, 1.5f);
                break;
            }
            case GShape.Gear:
            {
                var cc = V(0f, 0.5f);
                var pts = new Vector2[16];
                for (int i = 0; i < 16; i++) pts[i] = cc + Vector2.FromAngle(i * Mathf.Tau / 16 + t * 0.3f) * s * (i % 2 == 0 ? 0.45f : 0.33f) * g.W * 2f;
                ci.DrawColoredPolygon(pts, c); ci.DrawCircle(cc, s * 0.12f, dk);
                if (v == 2) R(0.3f, 0f, 0.5f, 0.3f, dk);
                break;
            }
            case GShape.Bolt:
                P(c, V(0.1f, 1f), V(-0.35f, 0.45f), V(0f, 0.45f), V(-0.15f, 0f), V(0.35f, 0.6f), V(0.02f, 0.6f));
                break;
            case GShape.Stick:
                DrawTool(ci, v, V(-0.5f, 0.2f), V(0.5f, 0.2f), s, c, dk, alpha, t);
                break;
            case GShape.Cards:
                for (int i = 0; i < 3; i++)
                {
                    var cc = V(-0.2f + i * 0.2f, 0.35f);
                    var a = (-0.4f + i * 0.4f);
                    var ux = new Vector2(s * 0.22f, 0).Rotated(a); var uy = new Vector2(0, -s * 0.32f).Rotated(a);
                    P(white, cc - ux - uy, cc + ux - uy, cc + ux + uy, cc - ux + uy);
                    ci.DrawCircle(cc, 1.6f, i == 1 ? new Color(0.85f, 0.15f, 0.15f, alpha) : ink);
                }
                break;
            case GShape.Dice:
                for (int k = 0; k < 2; k++)
                {
                    float x0 = -0.5f + k * 0.55f;
                    R(x0, 0f, x0 + 0.42f, 0.6f, c); ci.DrawRect(new Rect2(V(x0, 0.6f), new Vector2(0.42f * w, 0.6f * h)), dk, false, 1f);
                    int n = k == 0 ? 3 : 5;
                    for (int i = 0; i < n; i++) C(x0 + 0.1f + (i * 0.11f) % 0.3f, 0.12f + (i * 0.17f) % 0.4f, 0.04f, ink);
                }
                break;
            case GShape.Ball:
                switch (v)
                {
                    case 1: E(0f, 0.75f, 0.45f, 0.35f, c); L(V(0f, 0.4f), V(Mathf.Sin(t) * 0.1f, 0f), new Color(0.8f, 0.8f, 0.8f, alpha), 0.8f); C(-0.15f, 0.85f, 0.06f, white); break;
                    case 2: C(0f, 0.6f, 0.45f, c); for (int i = 0; i < 6; i++) { var d = Vector2.FromAngle(t * 2f + i); if (((int)(t * 3f) + i) % 3 == 0) ci.DrawCircle(V(0f, 0.6f) + d * s * 0.25f, 1.5f, white); } L(V(0f, 0.95f), V(0f, 1.2f), dk, 0.8f); break;
                    case 3: C(0f, 0.45f, 0.45f, c); ci.DrawArc(V(0f, 0.45f), 0.3f * s, 0.5f, 2.5f, 8, dk, 1f, true); ci.DrawArc(V(0f, 0.45f), 0.2f * s, 3f, 5f, 8, dk, 1f, true); L(V(0.4f, 0.3f), V(0.8f, 0f), c, 1f); break;
                    default: C(0f, 0.45f, 0.45f, c); var pts = StarPts(V(0f, 0.45f), s * 0.15f); ci.DrawColoredPolygon(new[] { pts[0], pts[2], pts[4], pts[6], pts[8] }, ink); break;
                }
                break;
            case GShape.Net:
                if (v == 1) { var cc = V(0f, 0.5f); for (int i = 0; i < 8; i++) L(cc, cc + Vector2.FromAngle(i * Mathf.Tau / 8) * s * 0.5f, c, 0.6f); for (int r = 1; r <= 3; r++) ci.DrawArc(cc, s * 0.15f * r, 0f, Mathf.Tau, 8, c, 0.6f, true); break; }
                L(V(-0.5f, 0f), V(-0.5f, 0.9f), dk, 2f); L(V(0.5f, 0f), V(0.5f, 0.9f), dk, 2f);
                for (int i = 0; i < 4; i++) L(V(-0.5f, 0.3f + i * 0.2f), V(0.5f, 0.3f + i * 0.2f), c, 0.6f);
                for (int i = 0; i < 6; i++) L(V(-0.5f + i * 0.2f, 0.3f), V(-0.5f + i * 0.2f, 0.9f), c, 0.6f);
                break;
            case GShape.Cup:
                switch (v)
                {
                    case 2: L(V(-0.2f, 1f), V(0f, 0.55f), new Color(0.2f, 0.4f, 0.8f, alpha), 2f); L(V(0.2f, 1f), V(0f, 0.55f), new Color(0.8f, 0.2f, 0.2f, alpha), 2f); C(0f, 0.4f, 0.18f, c); break;
                    case 3: P(c, V(-0.4f, 0.1f), V(0.4f, 0.1f), V(0.25f, 0.7f), V(-0.25f, 0.7f)); C(0f, 0.75f, 0.1f, c); C(Mathf.Sin(t * 6f) * 0.1f, 0.05f, 0.07f, dk); break;
                    default: P(c, V(-0.4f, 1f), V(0.4f, 1f), V(0.15f, 0.45f), V(-0.15f, 0.45f)); R(-0.06f, 0.15f, 0.06f, 0.45f, c); R(-0.3f, 0f, 0.3f, 0.15f, dk); ci.DrawArc(V(-0.4f, 0.8f), 0.12f * s, 1.5f, 4.7f, 6, c, 1.5f, true); ci.DrawArc(V(0.4f, 0.8f), 0.12f * s, -1.5f, 1.5f, 6, c, 1.5f, true); break;
                }
                break;
            case GShape.Mat:
                switch (v)
                {
                    case 3: for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) R(-0.5f + i * 0.25f, j * 0.2f, -0.25f + i * 0.25f, 0.2f + j * 0.2f, (i + j) % 2 == 0 ? c : new Color(0.25f, 0.2f, 0.15f, alpha)); break;
                    case 4: R(-0.5f, 0f, 0.5f, 0.5f, c); for (int i = 0; i < 3; i++) L(V(-0.4f + i * 0.35f, 0.25f), V(-0.25f + i * 0.35f, 0.25f), white, 1.5f); break;
                    case 5: for (int i = 0; i < 6; i++) R(-0.5f + (i % 3) * 0.34f, (i / 3) * 0.35f, -0.2f + (i % 3) * 0.34f, 0.3f + (i / 3) * 0.35f, i % 2 == 0 ? c : c.Darkened(0.2f)); break;
                    case 6: R(-0.5f, 0f, 0.5f, 0.35f, c); R(-0.5f, 0.3f, 0.5f, 0.4f, c.Lightened(0.2f)); break;
                    case 2: E(0f, 0.3f, 0.5f, 0.3f, c); ci.DrawArc(V(0f, 0.3f), 0.35f * w, 0f, Mathf.Tau, 14, lt, 1.2f, true); break;
                    case 1: R(-0.5f, 0f, 0.5f, 0.7f, c); ci.DrawRect(new Rect2(V(-0.4f, 0.6f), new Vector2(0.8f * w, 0.5f * h)), new Color(0.95f, 0.85f, 0.5f, alpha), false, 1.2f); for (int i = 0; i < 6; i++) L(V(-0.5f + i * 0.2f, 0f), V(-0.5f + i * 0.2f, -0.08f), lt, 0.8f); break;
                    default: R(-0.5f, 0f, 0.5f, 0.3f, c); L(V(-0.5f, 0.15f), V(0.5f, 0.15f), lt, 1f); break;
                }
                break;
            case GShape.Cushion:
                if (v == 2) { for (int i = 0; i < 5; i++) E(-0.4f + i * 0.2f, 0.2f + (i % 2) * 0.3f, 0.15f, 0.15f, i % 2 == 0 ? c : lt); L(V(0f, 0.6f), V(0f, 1f), dk, 1f); P(new Color(0.9f, 0.3f, 0.3f, alpha), V(0f, 1f), V(0.25f, 0.9f), V(0f, 0.82f)); break; }
                E(0f, 0.3f, 0.5f, 0.3f, c);
                if (v == 0) P(dk, V(0.45f, 0.3f), V(0.7f, 0.4f), V(0.7f, 0.2f));
                if (v == 1) { L(V(-0.4f, 0.3f), V(0.4f, 0.3f), lt, 0.8f); }
                break;
            case GShape.Seat:
                switch (v)
                {
                    case 1: R(-0.5f, 0.35f, 0.5f, 0.5f, c); L(V(-0.4f, 0f), V(-0.4f, 0.35f), dk, 1.5f); L(V(0.4f, 0f), V(0.4f, 0.35f), dk, 1.5f); break;
                    case 2: for (int i = 0; i < 3; i++) { float x = -0.4f + i * 0.4f; R(x - 0.12f, 0.3f, x + 0.12f, 0.4f, c); R(x - 0.12f, 0.4f, x - 0.08f, 0.8f, c); } break;
                    case 3: P(c, V(-0.5f, 0.3f), V(0.2f, 0.3f), V(0.5f, 0.6f), V(0.45f, 0.7f), V(0.1f, 0.42f), V(-0.5f, 0.42f)); L(V(-0.3f, 0f), V(-0.3f, 0.3f), dk, 1.5f); L(V(0.1f, 0f), V(0.1f, 0.3f), dk, 1.5f); break;
                    case 4: E(0f, 0.6f, 0.4f, 0.1f, c); L(V(-0.3f, 0f), V(-0.1f, 0.6f), dk, 1.2f); L(V(0.3f, 0f), V(0.1f, 0.6f), dk, 1.2f); L(V(0f, 0f), V(0f, 0.6f), dk, 1.2f); break;
                    case 5: R(-0.5f, 0.45f, 0.5f, 0.55f, c); L(V(-0.4f, 0f), V(-0.4f, 0.45f), dk, 1.5f); L(V(0.4f, 0f), V(0.4f, 0.45f), dk, 1.5f); break;
                    default: R(-0.4f, 0.35f, 0.4f, 0.45f, c); R(-0.4f, 0.45f, -0.3f, 1f, c); L(V(-0.35f, 0f), V(-0.35f, 0.35f), dk, 1.5f); L(V(0.35f, 0f), V(0.35f, 0.35f), dk, 1.5f); break;
                }
                break;
            case GShape.Bunting:
            {
                var a = V(-0.5f, 0.9f); var b = V(0.5f, 0.9f);
                if (v == 1) { L(V(-0.4f, 0f), V(-0.4f, 1f), dk, 1.2f); P(c, V(-0.4f, 1f), V(-0.4f, 0.65f), V(0.4f, 0.82f)); break; }
                var mid = (a + b) * 0.5f + new Vector2(0, h * 0.15f);
                ci.DrawPolyline(new[] { a, mid, b }, new Color(0.85f, 0.85f, 0.8f, alpha), 0.8f, true);
                for (int i = 0; i < 6; i++)
                {
                    float k = (i + 0.5f) / 6f;
                    var q = k < 0.5f ? a.Lerp(mid, k * 2f) : mid.Lerp(b, k * 2f - 1f);
                    if (v == 2) ci.DrawCircle(q, 1.8f, ((int)(t * 2f) + i) % 2 == 0 ? c : c.Darkened(0.5f));
                    else P(i % 2 == 0 ? c : lt, q + new Vector2(-s * 0.08f, 0), q + new Vector2(s * 0.08f, 0), q + new Vector2(0, s * 0.18f));
                }
                break;
            }
            case GShape.Clock:
            {
                var cc = V(0f, 0.5f);
                switch (v)
                {
                    case 1: R(-0.5f, 0.2f, 0.5f, 0.8f, c); ci.DrawCircle(V(-0.22f, 0.5f), s * 0.15f, white); ci.DrawCircle(V(0.22f, 0.5f), s * 0.15f, white); L(V(-0.22f, 0.5f), V(-0.22f, 0.5f) + Vector2.FromAngle(t) * s * 0.12f, ink, 1f); R(-0.3f, 0.8f, -0.15f, 0.9f, dk); break;
                    case 2: P(c, V(-0.35f, 1f), V(0.35f, 1f), V(0f, 0.5f)); P(c, V(-0.35f, 0f), V(0.35f, 0f), V(0f, 0.5f)); P(new Color(0.9f, 0.8f, 0.5f, alpha), V(-0.2f, 0f), V(0.2f, 0f), V(0f, 0.3f)); break;
                    case 3: ci.DrawCircle(cc, s * 0.4f, c); P(new Color(0.85f, 0.2f, 0.2f, alpha), cc + new Vector2(-s * 0.06f, 0), cc + new Vector2(s * 0.06f, 0), cc + Vector2.FromAngle(-Mathf.Pi / 2 + Mathf.Sin(t) * 0.3f) * s * 0.35f); break;
                    case 4: ci.DrawArc(V(0f, 0.2f), 0.5f * w, Mathf.Pi, Mathf.Tau, 10, c, 2f, true); L(V(0f, 0.2f), V(0f, 0.2f) + Vector2.FromAngle(Mathf.Pi + 1.2f + Mathf.Sin(t * 2f) * 0.5f) * s * 0.4f, new Color(0.9f, 0.2f, 0.2f, alpha), 1.4f); break;
                    case 5: ci.DrawCircle(cc, s * 0.4f, c); ci.DrawArc(cc, s * 0.4f, 0f, Mathf.Tau, 14, dk, 1f, true); R(-0.06f, 0.92f, 0.06f, 1.02f, dk); L(cc, cc + Vector2.FromAngle(t * 2f) * s * 0.32f, new Color(0.9f, 0.2f, 0.2f, alpha), 1f); break;
                    default: ci.DrawCircle(cc, s * 0.42f, c); ci.DrawArc(cc, s * 0.42f, 0f, Mathf.Tau, 16, dk, 1.5f, true); L(cc, cc + Vector2.FromAngle(t * 0.1f) * s * 0.25f, ink, 1.5f); L(cc, cc + Vector2.FromAngle(t * 1.2f) * s * 0.35f, ink, 1f); break;
                }
                break;
            }
            case GShape.Drape:
            {
                switch (v)
                {
                    case 2: for (int i = 0; i < 5; i++) R(-0.5f + i * 0.2f, 0f, -0.32f + i * 0.2f, 1f, i % 2 == 0 ? c : dk); R(-0.55f, 0.95f, 0.55f, 1.05f, new Color(0.85f, 0.7f, 0.3f, alpha)); break;
                    case 3: P(c.WithAlpha(0.6f * alpha), V(-0.5f, 1f), V(0.5f, 1f), V(0.45f, 0f), V(-0.45f, 0f)); for (int i = 0; i < 4; i++) L(V(-0.35f + i * 0.23f, 1f), V(-0.33f + i * 0.23f, 0f), dk.WithAlpha(0.5f * alpha), 0.7f); break;
                    case 4: P(c, V(-0.5f, 0.6f), V(0.5f, 0.6f), V(0.4f, 0f), V(-0.4f, 0f)); L(V(-0.4f, 0.45f), V(0.4f, 0.45f), dk, 1f); break;
                    case 5: R(-0.5f, 0.1f, 0.5f, 0.4f, c); for (int i = 0; i < 5; i++) R(-0.5f + i * 0.2f, 0.1f, -0.42f + i * 0.2f, 0.4f, lt); L(V(0.5f, 0.1f), V(0.6f, -0.2f), c, 2f); break;
                    case 6: R(-0.5f, 0f, 0.5f, 0.6f, c); L(V(-0.5f, 0.3f), V(0.5f, 0.3f), dk.WithAlpha(0.5f * alpha), 1f); P(dk.WithAlpha(0.3f * alpha), V(0.2f, 0.6f), V(0.5f, 0.6f), V(0.5f, 0.3f)); break;
                    case 7: R(-0.5f, 0f, 0.5f, 0.5f, c); L(V(-0.5f, 0.1f), V(0.5f, 0.1f), white, 1.5f); L(V(-0.5f, 0.4f), V(0.5f, 0.4f), white, 1.5f); break;
                    case 8: E(0f, 0.25f, 0.5f, 0.25f, c); ci.DrawArc(V(0f, 0.25f), 0.15f * s, 0f, 5f, 8, dk, 1f, true); break;
                    default:
                    {
                        var pts = new List<Vector2> { V(-0.5f, 0f) };
                        for (int i = 0; i <= 6; i++) pts.Add(V(-0.5f + i / 6f, 0.55f + 0.08f * Mathf.Sin(i * 1.7f + (v == 1 ? 0f : t * 0.5f))));
                        pts.Add(V(0.5f, 0f));
                        ci.DrawColoredPolygon(pts.ToArray(), c);
                        L(V(-0.3f, 0.1f), V(-0.2f, 0.5f), dk.WithAlpha(0.4f * alpha), 0.8f); L(V(0.2f, 0.1f), V(0.25f, 0.5f), dk.WithAlpha(0.4f * alpha), 0.8f);
                        break;
                    }
                }
                break;
            }
            case GShape.Rack:
                switch (v)
                {
                    case 1: ci.DrawRect(new Rect2(V(-0.5f, 1f), new Vector2(w, h)), c, false, 2f); for (int j = 0; j < 3; j++) { L(V(-0.5f, 0.33f * j), V(0.5f, 0.33f * j), c, 1.5f); for (int i = 0; i < 5; i++) R(-0.45f + i * 0.18f, 0.33f * j + 0.02f, -0.33f + i * 0.18f, 0.33f * j + 0.25f, new Color(0.3f + 0.12f * ((i + j) % 4), 0.3f, 0.45f + 0.1f * (i % 3), alpha)); } break;
                    case 2: P(c, V(-0.4f, 1f), V(-0.25f, 1f), V(-0.25f, 0.15f), V(0.5f, 0.15f), V(0.5f, 0f), V(-0.4f, 0f)); break;
                    case 3: R(-0.5f, 0f, 0.5f, 0.8f, c); for (int i = 0; i < 5; i++) L(V(-0.45f, 0.1f + i * 0.15f), V(0.45f, 0.1f + i * 0.15f), dk, 1.2f); break;
                    case 4: L(V(-0.4f, 0f), V(-0.3f, 1f), c, 2f); L(V(0.4f, 0f), V(0.3f, 1f), c, 2f); for (int i = 1; i < 5; i++) L(V(-0.38f + i * 0.02f, i * 0.2f), V(0.38f - i * 0.02f, i * 0.2f), c, 1.5f); break;
                    case 5: R(-0.5f, 0.6f, 0.5f, 0.7f, c); P(dk, V(-0.4f, 0.6f), V(-0.25f, 0.6f), V(-0.4f, 0.35f)); P(dk, V(0.4f, 0.6f), V(0.25f, 0.6f), V(0.4f, 0.35f)); break;
                    case 6: R(-0.5f, 0f, 0.5f, 0.25f, c); R(-0.35f, 0.25f, 0.35f, 0.5f, c.Lightened(0.1f)); R(-0.2f, 0.5f, 0.2f, 0.75f, c.Lightened(0.2f)); break;
                    default: L(V(-0.5f, 0f), V(-0.5f, 1f), c, 1.5f); L(V(0.5f, 0f), V(0.5f, 1f), c, 1.5f); for (int i = 1; i < 4; i++) { L(V(-0.5f, i * 0.25f), V(0.5f, i * 0.25f), c, 1f); for (int j = 0; j < 3; j++) L(V(-0.3f + j * 0.3f, i * 0.25f), V(-0.3f + j * 0.3f, i * 0.25f - 0.15f), new Color(0.45f, 0.65f, 0.35f, alpha), 2f); } break;
                }
                break;
            case GShape.Capsule:
                switch (v)
                {
                    case 1: L(V(0f, 0f), V(0f, 0.3f), dk, 1.5f); P(c, V(-0.5f, 0.4f), V(0.3f, 0.4f), V(0.5f, 0.5f), V(0.3f, 0.6f), V(-0.5f, 0.6f)); P(dk, V(-0.5f, 0.6f), V(-0.35f, 0.8f), V(-0.25f, 0.6f)); P(dk, V(-0.5f, 0.4f), V(-0.35f, 0.25f), V(-0.25f, 0.4f)); break;
                    case 2: E(0f, 0.5f, 0.5f, 0.45f, c); C(0.15f, 0.6f, 0.15f, new Color(0.4f, 0.7f, 0.9f, alpha)); ci.DrawCircle(V(0f, 0.5f), s * 0.6f, new Color(1f, 0.6f, 0.2f, 0.1f * alpha * (1f + Mathf.Sin(t * 3f)))); break;
                    case 3: P(c, V(-0.5f, 0.2f), V(0.5f, 0.4f), V(-0.5f, 0.6f)); P(dk, V(-0.5f, 0.2f), V(-0.3f, 0.05f), V(-0.2f, 0.3f)); C(0.1f, 0.4f, 0.05f, new Color(0.4f, 0.8f, 1f, alpha)); break;
                    default: E(0f, 0.3f, 0.5f, 0.3f, c); R(-0.08f, 0f, 0.08f, 0.6f, dk); break;
                }
                break;
            case GShape.Sign:
                switch (v)
                {
                    case 2: L(V(-0.4f, 0f), V(-0.2f, 0.9f), dk, 1.5f); L(V(0.4f, 0f), V(0.2f, 0.9f), dk, 1.5f); R(-0.5f, 0.35f, 0.5f, 1f, c); L(V(-0.35f, 0.75f), V(0.2f, 0.75f), white, 1f); L(V(-0.35f, 0.55f), V(0.3f, 0.55f), white, 1f); break;
                    case 3: R(-0.5f, 0.1f, 0.5f, 1f, c); for (int i = 0; i < 3; i++) { R(-0.4f + i * 0.3f, 0.4f + (i % 2) * 0.25f, -0.18f + i * 0.3f, 0.65f + (i % 2) * 0.25f, i == 1 ? new Color(1f, 0.9f, 0.4f, alpha) : white); C(-0.29f + i * 0.3f, 0.65f + (i % 2) * 0.25f, 0.03f, new Color(0.9f, 0.2f, 0.2f, alpha)); } break;
                    case 6: P(c, V(-0.5f, 0.1f), V(0.5f, 0.1f), V(0f, 0.95f)); R(-0.04f, 0.35f, 0.04f, 0.65f, ink); C(0f, 0.25f, 0.04f, ink); break;
                    default:
                        if (v == 0) L(V(0f, 0f), V(0f, 0.5f), new Color(0.55f, 0.4f, 0.25f, alpha), 2f);
                        R(-0.5f, 0.4f, 0.5f, 1f, c);
                        if (v == 0) ci.DrawPolyline(new[] { V(-0.35f, 0.8f), V(-0.1f, 0.6f), V(0.1f, 0.8f), V(0.35f, 0.6f) }, new Color(0.8f, 0.15f, 0.15f, alpha), 1.5f, true);
                        else { ci.DrawCircle(V(0f, 0.75f), s * 0.12f, v == 4 ? new Color(0.3f, 0.5f, 0.8f, alpha) : new Color(0.85f, 0.6f, 0.45f, alpha)); L(V(-0.3f, 0.5f), V(0.3f, 0.5f), ink, 1f); }
                        break;
                }
                break;
            case GShape.Hand:
                switch (v)
                {
                    case 1: L(V(0f, 0f), V(0f, 0.5f), c.Darkened(0.2f), 3f); R(-0.25f, 0.5f, 0.25f, 0.9f, c); for (int i = 0; i < 3; i++) L(V(-0.15f + i * 0.15f, 0.9f), V(-0.15f + i * 0.15f, 0.7f), dk, 0.6f); break;
                    case 2: E(0f, 0.35f, 0.4f, 0.3f, c); for (int i = 0; i < 4; i++) E(-0.3f + i * 0.2f, 0.75f + (i == 1 || i == 2 ? 0.1f : 0f), 0.08f, 0.2f, c); E(0.45f, 0.4f, 0.1f, 0.15f, c); break;
                    case 3: E(-0.18f, 0.4f, 0.25f, 0.35f, c); E(0.18f, 0.4f, 0.25f, 0.35f, c.Darkened(0.15f)); break;
                    default: E(0f, 0.35f, 0.3f, 0.3f, c); for (int i = 0; i < 4; i++) L(V(-0.2f + i * 0.13f, 0.55f), V(-0.25f + i * 0.17f, 0.95f), c, 2.4f); L(V(0.3f, 0.35f), V(0.5f, 0.6f), c, 2.4f); break;
                }
                break;
            case GShape.Eye:
                if (v == 2) { for (int k = -1; k <= 1; k += 2) { ci.DrawCircle(V(k * 0.25f, 0.5f), s * 0.14f, c.WithAlpha((0.6f + 0.4f * Mathf.Sin(t * 3f)) * alpha)); } break; }
                P(c, V(-0.5f, 0.5f), V(0f, 0.85f), V(0.5f, 0.5f), V(0f, 0.15f)); ci.DrawCircle(V(0f, 0.5f), s * 0.16f, v == 1 ? new Color(0.9f, 0.1f, 0.1f, alpha) : new Color(0.3f, 0.45f, 0.7f, alpha)); ci.DrawCircle(V(0f, 0.5f), s * 0.07f, ink);
                break;
            case GShape.Splat:
                switch (v)
                {
                    case 1: E(0f, 0.2f, 0.5f, 0.25f, c.WithAlpha(0.45f * alpha)); ci.DrawArc(V(0f, 0.2f), 0.45f * w, 0f, Mathf.Tau, 14, c.Darkened(0.3f).WithAlpha(0.5f * alpha), 1f, true); break;
                    case 2: ci.DrawPolyline(new[] { V(-0.5f, 0.2f), V(-0.35f, 0.7f), V(-0.2f, 0.2f), V(-0.05f, 0.65f), V(0.1f, 0.25f), V(0.3f, 0.7f), V(0.45f, 0.3f) }, c, 2f, true); break;
                    default: var cols = new[] { c, new Color(0.3f, 0.6f, 0.9f, alpha), new Color(0.95f, 0.8f, 0.3f, alpha), new Color(0.4f, 0.75f, 0.45f, alpha) }; for (int i = 0; i < 5; i++) E(-0.4f + i * 0.2f, 0.3f + (i % 2) * 0.35f, 0.15f, 0.15f, cols[i % 4]); break;
                }
                break;
            case GShape.Sparkle:
                switch (v)
                {
                    case 1: for (int i = 0; i < 10; i++) { float k = (t * 0.2f + i * 0.1f) % 1f; var col = new Color(0.5f + 0.5f * Mathf.Sin(i), 0.5f + 0.5f * Mathf.Cos(i * 1.7f), 0.6f, alpha); ci.DrawRect(new Rect2(V(-0.5f + (i * 0.37f) % 1f, 1f - k), new Vector2(2.2f, 2.2f)), col); } break;
                    case 2: for (int i = 0; i < 7; i++) ci.DrawCircle(V(-0.5f + (i * 0.29f) % 1f, (i * 0.13f) % 0.4f), 1.1f, c); break;
                    case 3: var cc = V(0f, 0.5f); for (int i = 0; i < 8; i++) L(cc + Vector2.FromAngle(i * Mathf.Tau / 8) * s * 0.15f, cc + Vector2.FromAngle(i * Mathf.Tau / 8) * s * 0.45f * (0.8f + 0.2f * Mathf.Sin(t * 8f)), c, 1.2f); break;
                    default: for (int i = 0; i < 4; i++) { var q = V(-0.4f + i * 0.27f, 0.2f + (i % 2) * 0.5f); float k = Mathf.Abs(Mathf.Sin(t * 3f + i)); L(q - new Vector2(s * 0.12f * k, 0), q + new Vector2(s * 0.12f * k, 0), c, 1.2f); L(q - new Vector2(0, s * 0.12f * k), q + new Vector2(0, s * 0.12f * k), c, 1.2f); } break;
                }
                break;
            case GShape.Wave:
                if (v == 1) { var pts = new Vector2[12]; for (int i = 0; i < 12; i++) pts[i] = V(-0.5f + i / 11f, 0.5f + 0.25f * Mathf.Sin(i * 0.9f + t * 4f)); ci.DrawPolyline(pts, c, 1.4f, true); break; }
                for (int i = 0; i < 3; i++) { float k = (t * 0.7f + i / 3f) % 1f; ci.DrawArc(V(0f, 0.2f), s * (0.2f + 0.5f * k), -2.3f, -0.8f, 8, c.WithAlpha((1f - k) * alpha), 1.4f, true); }
                break;
            case GShape.Cross:
                L(V(-0.4f, 0.1f), V(0.4f, 0.9f), c, 3f); L(V(0.4f, 0.1f), V(-0.4f, 0.9f), c, 3f);
                break;
            case GShape.Arrow:
                if (v == 1) { ci.DrawArc(V(0f, 0.5f), 0.35f * s, 0.3f, 2.8f, 10, c, 1.8f, true); ci.DrawArc(V(0f, 0.5f), 0.35f * s, 3.4f, 5.9f, 10, c, 1.8f, true); P(c, V(-0.4f, 0.65f), V(-0.2f, 0.55f), V(-0.45f, 0.4f)); P(c, V(0.4f, 0.35f), V(0.2f, 0.45f), V(0.45f, 0.6f)); break; }
                L(V(-0.5f, 0.5f), V(0.35f, 0.5f), c, 2f); P(c, V(0.5f, 0.5f), V(0.3f, 0.7f), V(0.3f, 0.3f));
                if (v == 2) P(c, V(-0.5f, 0.5f), V(-0.3f, 0.7f), V(-0.3f, 0.3f));
                break;
            case GShape.Globe:
                ci.DrawCircle(V(0f, 0.55f), s * 0.42f, c); ci.DrawArc(V(0f, 0.55f), s * 0.42f, 0f, Mathf.Tau, 16, dk, 1f, true);
                L(V(-0.42f * s / w, 0.55f), V(0.42f * s / w, 0.55f), lt, 0.8f); ci.DrawColoredPolygon(Ellipse(V(0f, 0.55f), s * 0.18f, s * 0.42f, 10), new Color(0.35f, 0.65f, 0.4f, 0.6f * alpha));
                L(V(0f, 0f), V(0f, 0.13f), dk, 1.5f);
                break;
            case GShape.Cake:
                R(-0.5f, 0f, 0.5f, 0.4f, c); R(-0.35f, 0.4f, 0.35f, 0.7f, c.Lightened(0.15f));
                for (int i = 0; i < 5; i++) C(-0.4f + i * 0.2f, 0.4f, 0.06f, white);
                C(0f, 0.75f, 0.06f, new Color(0.9f, 0.2f, 0.3f, alpha));
                break;
            case GShape.Mug:
                switch (v)
                {
                    case 1: for (int k = -1; k <= 1; k += 2) { R(k * 0.25f - 0.15f, 0f, k * 0.25f + 0.15f, 0.4f, c); ci.DrawArc(V(k * 0.25f + 0.15f, 0.2f), 0.08f * s, -1.5f, 1.5f, 6, c, 1.2f, true); } break;
                    case 2: P(c, V(-0.5f, 0.4f), V(0.5f, 0.4f), V(0.3f, 0f), V(-0.3f, 0f)); E(0f, 0.4f, 0.5f, 0.08f, lt); break;
                    case 4: P(c, V(-0.4f, 0.8f), V(0.4f, 0.8f), V(0.3f, 0f), V(-0.3f, 0f)); for (int i = 0; i < 3; i++) L(V(-0.2f + i * 0.2f, 0f), V(-0.27f + i * 0.27f, 0.8f), white, 2f); for (int i = 0; i < 5; i++) C(-0.3f + i * 0.15f, 0.85f + (i % 2) * 0.08f, 0.07f, new Color(1f, 0.95f, 0.75f, alpha)); break;
                    default: R(-0.3f, 0f, 0.3f, 0.6f, c); ci.DrawArc(V(0.3f, 0.3f), 0.15f * s, -1.5f, 1.5f, 8, c, 1.5f, true); for (int i = 0; i < 2; i++) { float k = (t * 0.5f + i * 0.5f) % 1f; ci.DrawArc(V(-0.1f + i * 0.2f, 0.7f + k * 0.4f), 2f, 0f, Mathf.Pi, 4, white.WithAlpha((1f - k) * 0.6f * alpha), 0.8f, true); } break;
                }
                break;
            case GShape.Kettle:
                E(0f, 0.35f, 0.4f, 0.35f, c); R(-0.15f, 0.65f, 0.15f, 0.75f, dk); P(c, V(0.35f, 0.4f), V(0.7f, 0.7f), V(0.65f, 0.75f), V(0.3f, 0.5f));
                ci.DrawArc(V(-0.4f, 0.4f), 0.15f * s, 1.5f, 4.7f, 6, dk, 1.5f, true);
                if (v == 2) for (int i = 0; i < 3; i++) C(-0.2f + i * 0.2f, 0.35f, 0.05f, white);
                if (v == 1) ci.DrawArc(V(0f, 0.75f), 0.25f * w, Mathf.Pi, Mathf.Tau, 8, dk, 1.5f, true);
                break;
            case GShape.Pan:
                if (v == 1) { ci.DrawArc(V(0f, 0.6f), 0.5f * w, 0.2f, Mathf.Pi - 0.2f, 12, c, 3f, true); L(V(0.5f, 0.55f), V(0.9f, 0.7f), dk, 2f); break; }
                E(-0.1f, 0.25f, 0.4f, 0.25f, c); L(V(0.3f, 0.25f), V(0.8f, 0.3f), dk, 2.5f); for (int i = 0; i < 4; i++) E(-0.25f + i * 0.1f, 0.28f, 0.05f, 0.05f, new Color(0.45f, 0.28f, 0.15f, alpha));
                break;
            case GShape.Dome:
                if (v == 2) { ci.DrawArc(V(0f, 0.3f), 0.45f * w, Mathf.Pi, Mathf.Tau, 10, c, 2.5f, true); E(-0.45f, 0.25f, 0.12f, 0.2f, c); E(0.45f, 0.25f, 0.12f, 0.2f, c); break; }
                P(c, V(-0.5f, 0f), V(-0.5f, 0.4f), V(-0.3f, 0.8f), V(0.3f, 0.8f), V(0.5f, 0.4f), V(0.5f, 0f));
                P(new Color(0.3f, 0.5f, 0.7f, 0.8f * alpha), V(-0.3f, 0.2f), V(-0.3f, 0.5f), V(0.3f, 0.5f), V(0.3f, 0.2f));
                break;
            case GShape.Suit:
                C(0f, 0.85f, 0.18f, c); R(-0.25f, 0.25f, 0.25f, 0.7f, c); L(V(-0.25f, 0.65f), V(-0.45f, 0.35f), c, 3f); L(V(0.25f, 0.65f), V(0.45f, 0.35f), c, 3f);
                L(V(-0.12f, 0.25f), V(-0.15f, 0f), c, 3f); L(V(0.12f, 0.25f), V(0.15f, 0f), c, 3f); C(0f, 0.85f, 0.1f, new Color(0.3f, 0.5f, 0.7f, alpha));
                break;
            case GShape.Boot:
                switch (v)
                {
                    case 1: for (int i = 0; i < 4; i++) E(-0.4f + i * 0.27f, 0.2f + (i % 2) * 0.35f, 0.08f, 0.14f, c.WithAlpha(0.6f * alpha)); break;
                    case 2: for (int i = 0; i < 5; i++) E(-0.45f + i * 0.22f, 0.2f + (i % 2) * 0.2f, 0.06f, 0.1f, c.WithAlpha(0.5f * alpha)); break;
                    case 3: P(c, V(-0.5f, 0f), V(0.5f, 0f), V(0.45f, 0.25f), V(-0.1f, 0.35f), V(-0.4f, 0.7f), V(-0.5f, 0.7f)); L(V(-0.45f, 0.05f), V(0.45f, 0.05f), new Color(0.9f, 0.3f, 0.3f, alpha), 1.5f); break;
                    default: for (int k = 0; k < 2; k++) { float x = -0.45f + k * 0.5f; P(c, V(x, 0f), V(x + 0.4f, 0f), V(x + 0.4f, 0.2f), V(x + 0.15f, 0.25f), V(x + 0.15f, 0.8f), V(x, 0.8f)); } break;
                }
                break;
            case GShape.Hat:
                if (v == 1) { E(0f, 0.3f, 0.45f, 0.25f, c); P(ink, V(-0.1f, 0.15f), V(0.6f, 0.05f), V(0.5f, 0.2f)); C(0f, 0.38f, 0.07f, new Color(0.9f, 0.75f, 0.3f, alpha)); break; }
                R(-0.5f, 0f, 0.5f, 0.1f, c); R(-0.3f, 0.1f, 0.3f, 0.9f, c); R(-0.3f, 0.2f, 0.3f, 0.3f, new Color(0.7f, 0.2f, 0.2f, alpha));
                break;
            case GShape.Face:
            {
                var cc = V(0f, 0.5f);
                ci.DrawCircle(cc, s * 0.4f * g.W * 2f, c);
                if (v == 1) { ci.DrawArc(cc + new Vector2(-s * 0.13f, -s * 0.08f), s * 0.06f, Mathf.Pi, Mathf.Tau, 4, ink, 1f, true); ci.DrawArc(cc + new Vector2(s * 0.13f, -s * 0.08f), s * 0.06f, Mathf.Pi, Mathf.Tau, 4, ink, 1f, true); ci.DrawColoredPolygon(Ellipse(cc + new Vector2(0, s * 0.12f), s * 0.15f, s * 0.1f + s * 0.03f * Mathf.Abs(Mathf.Sin(t * 8f)), 8), ink); }
                else { ci.DrawCircle(cc + new Vector2(-s * 0.13f, -s * 0.08f), 1.4f, ink); ci.DrawCircle(cc + new Vector2(s * 0.13f, -s * 0.08f), 1.4f, ink); ci.DrawArc(cc + new Vector2(0, s * 0.02f), s * 0.18f, 0.4f, Mathf.Pi - 0.4f, 8, ink, 1.2f, true); }
                break;
            }
            case GShape.Ghost:
            {
                float fl = Mathf.Sin(t * 2f) * 0.06f;
                var pts = new List<Vector2> { V(-0.45f, 0.1f + fl) };
                for (int i = 0; i <= 8; i++) { float a = Mathf.Pi + i * Mathf.Pi / 8f; pts.Add(V(0f, 0.65f + fl) + new Vector2(Mathf.Cos(a) * 0.45f * w, Mathf.Sin(a) * 0.35f * h)); }
                pts.Add(V(0.45f, 0.1f + fl)); pts.Add(V(0.22f, 0.2f + fl)); pts.Add(V(0f, 0.08f + fl)); pts.Add(V(-0.22f, 0.2f + fl));
                ci.DrawColoredPolygon(pts.ToArray(), c.WithAlpha(0.85f * alpha));
                C(-0.15f, 0.65f + fl, 0.06f, ink); C(0.15f, 0.65f + fl, 0.06f, ink);
                break;
            }
            case GShape.Glass:
                if (v == 1) { E(0f, 0.05f, 0.5f, 0.06f, dk); ci.DrawArc(V(0f, 0.05f), 0.48f * w, Mathf.Pi, Mathf.Tau, 12, c, 1.2f, true); for (int i = -2; i <= 2; i++) L(V(i * 0.18f, 0.05f), V(i * 0.1f, 0.05f + Mathf.Sqrt(Mathf.Max(0f, 1f - i * i * 0.04f)) * 0.85f), c, 0.8f); break; }
                R(-0.5f, 0f, 0.5f, 0.9f, c.WithAlpha(0.35f * alpha)); R(-0.48f, 0.02f, 0.48f, 0.6f, new Color(0.75f, 0.6f, 0.4f, alpha));
                ci.DrawRect(new Rect2(V(-0.5f, 0.9f), new Vector2(w, 0.9f * h)), dk, false, 1.2f);
                break;
            case GShape.Map:
                R(-0.5f, 0f, 0.5f, 0.8f, c); L(V(-0.17f, 0f), V(-0.17f, 0.8f), dk.WithAlpha(0.5f * alpha), 0.7f); L(V(0.17f, 0f), V(0.17f, 0.8f), dk.WithAlpha(0.5f * alpha), 0.7f);
                if (v == 1) { for (int i = 0; i < 7; i++) ci.DrawCircle(V(-0.4f + (i * 0.31f) % 0.8f, 0.1f + (i * 0.17f) % 0.6f), 1.2f, new Color(1f, 1f, 0.8f, alpha)); }
                else { ci.DrawPolyline(new[] { V(-0.4f, 0.15f), V(-0.1f, 0.4f), V(0.15f, 0.3f), V(0.35f, 0.6f) }, new Color(0.8f, 0.2f, 0.2f, alpha), 1f, true); L(V(0.3f, 0.55f), V(0.4f, 0.65f), new Color(0.8f, 0.2f, 0.2f, alpha), 1.5f); L(V(0.4f, 0.55f), V(0.3f, 0.65f), new Color(0.8f, 0.2f, 0.2f, alpha), 1.5f); }
                break;
            case GShape.Lock:
                ci.DrawArc(V(0f, 0.6f), 0.28f * w, Mathf.Pi, Mathf.Tau, 10, new Color(0.7f, 0.7f, 0.75f, alpha), 2.2f, true); R(-0.45f, 0f, 0.45f, 0.6f, c); C(0f, 0.32f, 0.07f, ink);
                break;
            case GShape.Drum:
                if (v == 1) { var cc = V(0f, 0.5f); for (int i = 0; i < 4; i++) { var a = Vector2.FromAngle(t * 2f + i * Mathf.Pi / 4f) * s * 0.4f; L(cc - a, cc + a, c, 1f); } ci.DrawArc(cc, s * 0.4f, 0f, Mathf.Tau, 12, c, 1.2f, true); L(cc, cc + new Vector2(s * 0.6f, 0), dk, 1.5f); for (int i = 0; i < 3; i++) ci.DrawCircle(cc + Vector2.FromAngle(t * 2f + i * 2f) * s * 0.2f, 1.6f, new Color(1f, 0.8f, 0.3f, alpha)); break; }
                R(-0.45f, 0f, 0.45f, 0.6f, c); E(0f, 0.6f, 0.45f, 0.12f, new Color(0.95f, 0.92f, 0.85f, alpha)); L(V(-0.45f, 0f), V(0.45f, 0.6f), lt, 0.8f); L(V(0.45f, 0f), V(-0.45f, 0.6f), lt, 0.8f);
                L(V(0.1f, 0.7f), V(0.5f, 1f), new Color(0.8f, 0.7f, 0.5f, alpha), 1.5f); L(V(-0.1f, 0.7f), V(-0.4f, 1.05f), new Color(0.8f, 0.7f, 0.5f, alpha), 1.5f);
                break;
            case GShape.Guitar:
                C(0f, 0.2f, 0.25f * g.W * 2f, c); C(0f, 0.45f, 0.18f * g.W * 2f, c); C(0f, 0.3f, 0.07f, ink); R(-0.06f, 0.5f, 0.06f, 1f, dk);
                L(V(-0.03f, 0.15f), V(-0.03f, 1f), white.WithAlpha(0.5f * alpha), 0.5f); L(V(0.03f, 0.15f), V(0.03f, 1f), white.WithAlpha(0.5f * alpha), 0.5f);
                break;
            case GShape.Scope:
                L(V(0f, 0.45f), V(-0.35f, 0f), dk, 1.2f); L(V(0f, 0.45f), V(0.35f, 0f), dk, 1.2f); L(V(0f, 0.45f), V(0f, 0f), dk, 1.2f);
                P(c, V(-0.5f, 0.45f), V(0.45f, 0.85f), V(0.5f, 0.75f), V(-0.45f, 0.35f)); C(0.48f, 0.8f, 0.06f, new Color(0.6f, 0.8f, 1f, alpha));
                break;
            case GShape.Camera:
                R(-0.5f, 0f, 0.5f, 0.6f, c); R(-0.2f, 0.6f, 0.15f, 0.75f, c); C(0f, 0.3f, 0.2f, dk); C(0f, 0.3f, 0.1f, new Color(0.4f, 0.6f, 0.9f, alpha)); R(0.25f, 0.45f, 0.42f, 0.55f, white);
                break;
            case GShape.Torch:
            {
                R(-0.5f, 0.3f, 0.1f, 0.5f, c); R(0.1f, 0.25f, 0.25f, 0.55f, dk);
                float fl = 0.85f + 0.15f * Mathf.Sin(t * 13f);
                P(new Color(1f, 0.95f, 0.7f, 0.25f * alpha * fl), V(0.25f, 0.3f), V(0.25f, 0.5f), V(1.2f, 0.95f), V(1.2f, -0.15f));
                break;
            }
            case GShape.Rocks:
                switch (v)
                {
                    case 1: P(c, V(-0.5f, 0f), V(0.5f, 0f), V(0.1f, 0.25f), V(-0.2f, 0.2f)); for (int i = 0; i < 3; i++) C(-0.2f + i * 0.2f, 0.1f, 0.03f, new Color(1f, 0.5f, 0.2f, 0.5f * alpha * (0.5f + 0.5f * Mathf.Sin(t * 4f + i)))); break;
                    case 2: P(new Color(0.6f, 0.6f, 0.6f, alpha), V(-0.5f, 0.4f), V(0.5f, 0.4f), V(0.3f, 0f), V(-0.3f, 0f)); E(0f, 0.4f, 0.45f, 0.12f, c); break;
                    case 3: for (int i = 0; i < 5; i++) E(-0.35f + (i % 3) * 0.35f, 0.15f + (i / 3) * 0.25f, 0.18f, 0.14f, i % 2 == 0 ? c : lt); for (int i = 0; i < 3; i++) { float k = (t * 0.6f + i * 0.33f) % 1f; ci.DrawArc(V(-0.2f + i * 0.2f, 0.5f + k * 0.5f), 2f, 0f, Mathf.Pi, 4, new Color(1f, 0.7f, 0.5f, (1f - k) * 0.5f * alpha), 0.8f, true); } break;
                    default: P(c, V(-0.5f, 0f), V(0.5f, 0f), V(0.35f, 0.5f), V(-0.1f, 0.65f), V(-0.45f, 0.4f)); break;
                }
                break;
            case GShape.Log:
                R(-0.5f, 0f, 0.4f, 0.5f, c); E(0.4f, 0.25f, 0.12f, 0.25f, lt); ci.DrawArc(V(0.4f, 0.25f), 0.08f * s, 0f, Mathf.Tau, 8, dk, 0.7f, true);
                for (int i = 0; i < 3; i++) C(-0.3f + i * 0.25f, 0.55f, 0.07f, new Color(0.8f, 0.7f, 0.6f, alpha));
                break;
            case GShape.Fruit:
                switch (v)
                {
                    case 1: C(0f, 0.4f, 0.4f, c); ci.DrawArc(V(0f, 0.4f), 0.28f * s, 0.3f, 2.8f, 8, c.Darkened(0.2f), 1f, true); ci.DrawArc(V(0f, 0.4f), 0.16f * s, 3.4f, 6f, 8, c.Darkened(0.25f), 1f, true); break;
                    case 2: P(c, V(-0.25f, 0.85f), V(0.25f, 0.85f), V(0f, 0f)); for (int i = -1; i <= 1; i++) L(V(i * 0.08f, 0.85f), V(i * 0.2f, 1.1f), new Color(0.3f, 0.65f, 0.3f, alpha), 2f); for (int i = 1; i < 4; i++) L(V(-0.15f + i * 0.02f, i * 0.2f), V(0.05f, i * 0.2f + 0.03f), dk, 0.6f); break;
                    case 3: for (int i = 0; i < 6; i++) E(-0.4f + (i * 0.17f) % 0.8f, 0.08f + (i % 3) * 0.12f, 0.1f, 0.07f, c); break;
                    default: for (int i = 0; i < 3; i++) { var q = V(-0.25f + i * 0.25f, 0.25f + (i % 2) * 0.15f); P(c, q + new Vector2(-s * 0.13f, -s * 0.05f), q + new Vector2(s * 0.13f, -s * 0.05f), q + new Vector2(0, s * 0.18f)); ci.DrawCircle(q + new Vector2(0, -s * 0.05f), s * 0.12f, c); L(q + new Vector2(-s * 0.08f, -s * 0.15f), q + new Vector2(s * 0.08f, -s * 0.15f), new Color(0.3f, 0.65f, 0.3f, alpha), 1.5f); } break;
                }
                break;
            case GShape.Cord:
                switch (v)
                {
                    case 1: ci.DrawArc(V(-0.22f, 0.5f), 0.25f * w, 0.2f, 2.8f, 8, c, 2.5f, true); ci.DrawArc(V(0.22f, 0.5f), 0.25f * w, 0.35f, 2.95f, 8, c, 2.5f, true); break;
                    case 2: P(c, V(0f, 0.5f), V(-0.45f, 0.75f), V(-0.45f, 0.25f)); P(c, V(0f, 0.5f), V(0.45f, 0.75f), V(0.45f, 0.25f)); L(V(0f, 0.5f), V(-0.2f, 0f), c, 1.5f); L(V(0f, 0.5f), V(0.2f, 0f), c, 1.5f); break;
                    case 3: { var pts = new Vector2[10]; for (int i = 0; i < 10; i++) pts[i] = V(-0.5f + i / 9f, 0.4f + 0.2f * Mathf.Sin(i * 2.1f)); ci.DrawPolyline(pts, c, 1f, true); break; }
                    case 4: for (int i = -1; i <= 1; i++) ci.DrawPolyline(new[] { V(i * 0.2f, 0f), V(i * 0.25f + 0.05f, 0.5f), V(i * 0.2f, 1f) }, c, 0.7f, true); break;
                    case 5: L(V(-0.5f, 0.2f), V(0.5f, 0.8f), c, 4f); L(V(-0.5f, 0.8f), V(0.5f, 0.2f), c, 4f); for (int i = 0; i < 4; i++) { var q = V(-0.35f + i * 0.23f, 0.25f + i * 0.17f); L(q, q + new Vector2(2f, 0), ink, 2f); } break;
                    case 6: for (int i = 0; i < 3; i++) ci.DrawArc(V(0f, 0.35f), s * (0.15f + i * 0.1f), 0f, Mathf.Tau, 12, c, 1.2f, true); L(V(0.35f, 0.35f), V(0.6f, 0.9f), c, 1.2f); break;
                    case 7: ci.DrawPolyline(new[] { V(-0.45f, 0.1f), V(-0.2f, 0.3f), V(-0.35f, 0.5f), V(0f, 0.6f), V(0.2f, 0.4f), V(0.45f, 0.55f) }, c, 2f, true); break;
                    case 9: L(V(-0.5f, 0.8f), V(0.5f, 0.8f), c, 1f); for (int i = 0; i < 3; i++) { R(-0.35f + i * 0.3f, 0.45f, -0.25f + i * 0.3f, 0.8f, new Color(0.95f, 0.9f, 0.8f, alpha)); R(-0.32f + i * 0.3f, 0.75f, -0.28f + i * 0.3f, 0.88f, new Color(0.7f, 0.5f, 0.3f, alpha)); } break;
                    case 10: { var pts = new Vector2[8]; for (int i = 0; i < 8; i++) pts[i] = V(-0.5f + i / 7f, 0.3f + 0.15f * Mathf.Sin(i * 1.3f)); ci.DrawPolyline(pts, c, 1.8f, true); R(0.45f, 0.2f, 0.6f, 0.4f, dk); break; }
                    case 8: L(V(-0.5f, 0.8f), V(0.5f, 0.8f), c, 1f); for (int i = 0; i < 4; i++) L(V(-0.35f + i * 0.23f, 0.8f), V(-0.35f + i * 0.23f, 0.55f), c, 0.7f); break;
                    default: for (int i = 0; i < 3; i++) ci.DrawArc(V(0f, 0.25f), s * (0.1f + i * 0.08f), 0f, Mathf.Tau, 10, c, 1.6f, true); break;
                }
                break;
            case GShape.Bars:
                switch (v)
                {
                    case 2: R(-0.45f, 0f, 0.45f, 0.6f, c); for (int i = 0; i < 4; i++) { L(V(-0.45f, 0.1f + i * 0.13f), V(-0.6f, 0.1f + i * 0.13f), new Color(0.8f, 0.8f, 0.8f, alpha), 1f); L(V(0.45f, 0.1f + i * 0.13f), V(0.6f, 0.1f + i * 0.13f), new Color(0.8f, 0.8f, 0.8f, alpha), 1f); } break;
                    case 3: L(V(-0.35f, 0.3f), V(0.35f, 0.3f), c, 2f); R(-0.5f, 0.05f, -0.35f, 0.55f, c); R(0.35f, 0.05f, 0.5f, 0.55f, c); break;
                    case 4: P(c, V(-0.5f, 0.15f), V(-0.4f, 0f), V(0.4f, 0f), V(0.5f, 0.15f), V(0.4f, 0.4f), V(-0.4f, 0.4f)); L(V(-0.2f, 0.2f), V(0.2f, 0.2f), white, 1.5f); break;
                    default: for (int i = 0; i < 3; i++) { R(-0.5f + i * 0.05f, i * 0.2f, 0.4f + i * 0.05f, i * 0.2f + 0.18f, i % 2 == 0 ? c : lt); } if (v == 1) for (int i = 0; i < 3; i++) R(-0.1f + i * 0.05f, i * 0.2f + 0.04f, 0.1f + i * 0.05f, i * 0.2f + 0.14f, white.WithAlpha(0.6f * alpha)); break;
                }
                break;
            case GShape.Pill:
                for (int k = 0; k < 2; k++) { var q = V(-0.2f + k * 0.35f, 0.2f + k * 0.1f); var d = new Vector2(s * 0.15f, -s * 0.06f); ci.DrawLine(q - d, q, c, s * 0.14f, true); ci.DrawLine(q, q + d, new Color(0.9f, 0.3f, 0.3f, alpha), s * 0.14f, true); }
                break;
            case GShape.Crystal:
                P(c, V(-0.4f, 0f), V(-0.25f, 0.7f), V(-0.05f, 0f)); P(c.Lightened(0.2f), V(-0.1f, 0f), V(0.1f, 1f), V(0.3f, 0f)); P(c.Darkened(0.2f), V(0.2f, 0f), V(0.4f, 0.55f), V(0.5f, 0f));
                if (((int)(t * 2f)) % 3 == 0) ci.DrawCircle(V(0.1f, 0.8f), 1.5f, white);
                break;
            case GShape.Zz:
                for (int i = 0; i < 3; i++) { float k = (t * 0.4f + i / 3f) % 1f; var q = V(-0.2f + k * 0.5f, k); float z = s * (0.1f + 0.08f * k); ci.DrawPolyline(new[] { q, q + new Vector2(z, 0), q + new Vector2(0, z), q + new Vector2(z, z) }, c.WithAlpha((1f - k) * alpha), 1f, true); }
                break;
        }
    }

    private static Vector2[] StarPts(Vector2 c, float r)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++) { float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5; pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? r : r * 0.42f); }
        return pts;
    }

    private static void StarPoly(CanvasItem ci, Vector2 c, float r, Color col) => ci.DrawColoredPolygon(StarPts(c, r), col);

    /// <summary>손에 드는 도구들 (긴 막대 + 끝 모양이 저마다 다르다).</summary>
    private static void DrawTool(CanvasItem ci, int v, Vector2 a, Vector2 b, float s, Color c, Color dk, float alpha, float t)
    {
        var mid = (a + b) * 0.5f;
        var dir = (b - a).Normalized();
        var n = dir.Orthogonal();
        void L(Vector2 x, Vector2 y, Color col, float wd) => ci.DrawLine(x, y, col, wd, true);
        switch (v)
        {
            case 0: L(a, b, c, 2.5f); ci.DrawArc(b, s * 0.15f, -1f, 4f, 8, c, 2.5f, true); break;
            case 1: ci.DrawArc(a, s * 0.12f, 0f, Mathf.Tau, 8, c, 2f, true); L(a + dir * s * 0.12f, b, c, 2f); L(b, b + n * s * 0.1f, c, 2f); L(b - dir * s * 0.15f, b - dir * s * 0.15f + n * s * 0.08f, c, 2f); break;
            case 2: L(a, b, c, 0.8f); ci.DrawArc(a + dir * s * 0.05f, 1.2f, 0f, Mathf.Tau, 6, c, 0.6f, true); break;
            case 3: L(mid + n * s * 0.4f, mid - n * s * 0.1f, dk, 1.5f); L(a, b, c, 1.5f); ci.DrawArc(a + n * -s * 0.1f, s * 0.15f, 0f, Mathf.Pi, 6, c, 1.2f, true); ci.DrawArc(b + n * -s * 0.1f, s * 0.15f, 0f, Mathf.Pi, 6, c, 1.2f, true); break;
            case 4: L(a, b - dir * s * 0.12f, c, 2.2f); L(b - dir * s * 0.12f, b, new Color(0.85f, 0.85f, 0.85f, alpha), 1f); break;
            case 5: L(a, mid, new Color(0.6f, 0.4f, 0.25f, alpha), 2f); ci.DrawColoredPolygon(new[] { mid + n * s * 0.08f, mid - n * s * 0.08f, b - n * s * 0.12f, b + n * s * 0.12f }, c); break;
            case 6: L(a, b - dir * s * 0.15f, c, 1.5f); ci.DrawCircle(b, s * 0.12f, c); break;
            case 7: L(a, b, c, 1.5f); ci.DrawArc(b + n * s * 0.1f, s * 0.15f, 0f, Mathf.Pi, 8, c, 2.5f, true); break;
            case 8: ci.DrawCircle(a, s * 0.1f, dk); L(a, b, c, 3f); break;
            case 10: L(a, b, c, 2.2f); for (int i = 1; i < 5; i++) { var q = a.Lerp(b, i / 5f); L(q - n * s * 0.06f, q + n * s * 0.06f, dk, 0.8f); } ci.DrawCircle(a, s * 0.1f, c); break;
            case 11: L(mid - dir * s * 0.15f, mid + dir * s * 0.15f, c, 3f); break;
            case 12: L(a, b - dir * s * 0.1f, c, 2f); L(b - dir * s * 0.1f, b, new Color(1f, 0.5f + 0.3f * Mathf.Sin(t * 6f), 0.1f, alpha), 2f); ci.DrawCircle(b + n * s * 0.2f + new Vector2(0, -s * 0.2f * ((t * 0.5f) % 1f)), 1.5f, new Color(0.7f, 0.7f, 0.7f, 0.5f * alpha)); break;
            case 13: L(a, b, c, 2f); for (int i = 0; i < 8; i++) { var q = a.Lerp(b, i / 8f); L(q, q + n * s * 0.12f, c, 0.6f); } break;
            case 14: L(a + n * s * 0.3f, b - n * s * 0.3f, c, 1.5f); L(a - n * s * 0.3f, b + n * s * 0.3f, c, 1.5f); ci.DrawCircle(mid, s * 0.18f, new Color(0.75f, 0.3f, 0.5f, alpha)); break;
            case 16: L(a, b, c, 2.5f); L(a, a + dir * s * 0.1f, new Color(1f, 0.6f, 0.65f, alpha), 2.5f); L(b - dir * s * 0.08f, b, new Color(0.3f, 0.3f, 0.3f, alpha), 1f); break;
            case 17: ci.DrawCircle(mid + n * -s * 0.1f, s * 0.12f, c); L(mid, mid + n * s * 0.15f, new Color(0.8f, 0.8f, 0.8f, alpha), 0.8f); break;
            case 18: L(mid - n * s * 0.4f, mid, new Color(0.55f, 0.4f, 0.25f, alpha), 3f); ci.DrawRect(new Rect2(mid - new Vector2(s * 0.2f, 0), new Vector2(s * 0.4f, s * 0.12f)), c); break;
            case 20: L(a, b, new Color(0.9f, 0.9f, 0.95f, alpha), 2f); ci.DrawCircle(a, s * 0.08f, c); L(a, a.Lerp(b, 0.6f), c, 1f); break;
            case 21: L(a, mid, c, 3f); L(mid, b, new Color(0.75f, 0.75f, 0.8f, alpha), 1.2f); break;
            default: L(a, b, c, 1.6f); break;
        }
    }
}
