using System;
using ShipSim.Core;

namespace ShipSim.Art;

/// <summary>
/// 도트 사고 흔적 30가지 (24 × 24, 칸 가운데에 놓는다): 사고마다 남는 모양이 다르다.
/// 흐려지는 정도는 그릴 때 투명도로 준다 (그림 하나로 충분).
/// </summary>
public static class DotTraces
{
    public const int S = 24;

    public static Img Trace(HazardKind k, int seed)
    {
        var d = new Dot(S, S);
        int c = S / 2;
        float F(int i) => DotHash.F(seed, i, 1900);
        void Blob(int x, int y, float r, int ramp, int shade) { d.Oval(x, y, r, r * 0.7f, ramp, shade); }
        void Spot(int x, int y, int ramp, int shade) => d.Mark(x, y, ramp, shade, true);
        switch (k)
        {
            case HazardKind.CatScratch:
                for (int i = 0; i < 3; i++) d.Line(c - 4 + i * 3, c - 5, c - 2 + i * 3, c + 4, Ramps.ClothRed, 2, mark: true);
                d.Part = 2; Blob(c + 7, c + 5, 2.2f, Ramps.ClothOrange, 3);
                break;
            case HazardKind.StaticZap:
                d.Part = 1; Blob(c, c, 2.4f, Ramps.Char, 1);
                d.Line(c - 5, c - 6, c - 1, c - 2, Ramps.Cyan, 3, mark: true); d.Line(c - 1, c - 2, c - 3, c, Ramps.Cyan, 3, mark: true); d.Line(c - 3, c, c, c, Ramps.Cyan, 4, mark: true);
                break;
            case HazardKind.HeatExhaustion:
                d.Part = 1; d.Rect(c - 7, c - 2, 4, 5, Ramps.ClothOrange, 2); d.Rect(c - 7, c - 3, 2, 2, Ramps.ClothOrange, 2);
                d.Part = 2; d.Line(c + 2, c + 3, c + 8, c, Ramps.Glass, 4); d.Line(c + 2, c + 4, c + 8, c + 1, Ramps.Glass, 3); d.Mark(c + 8, c, Ramps.ClothBlue, 3);
                break;
            case HazardKind.SpaceSick:
                d.Part = 1;
                for (int i = 0; i < 6; i++) Blob(c - 5 + (int)(F(i) * 10), c - 3 + (int)(F(i + 5) * 8), 1.3f + F(i + 9) * 1.4f, SickRamp, 2 + (i & 1));
                d.Part = 2; d.Rect(c + 5, c - 6, 4, 4, Ramps.Bandage, 3); d.Mark(c + 6, c - 5, Ramps.Bandage, 1);
                break;
            case HazardKind.HatchFall:
                d.Part = 1; d.Rect(c - 5, c - 5, 10, 10, Ramps.Panel, 0);
                d.MarkRect(c - 5, c - 5, 10, 1, Ramps.Hazard, 3); d.MarkRect(c - 5, c + 4, 10, 1, Ramps.Hazard, 3);
                d.Part = 2; d.Line(c + 6, c + 6, c + 10, c + 3, Ramps.ClothOrange, 3); d.Line(c + 6, c + 7, c + 10, c + 4, Ramps.ClothOrange, 4); d.Mark(c + 8, c + 5, Ramps.Bandage, 3);
                break;
            case HazardKind.ScaldSpill:
                d.Part = 1; Blob(c, c + 1, 6, Ramps.ClothOrange, 2); Blob(c + 4, c + 3, 3, Ramps.ClothOrange, 2);
                d.Mark(c - 2, c, Ramps.ClothOrange, 3); d.Mark(c + 1, c + 2, Ramps.Hazard, 3);
                break;
            case HazardKind.PotFire:
                d.Part = 1;
                for (int i = 0; i < 4; i++) Blob(c - 2 + i * 2, c - 3 - i, 3 + i, Ramps.Char, 0);
                d.Part = 2; d.Oval(c, c + 3, 5, 3, Ramps.Rubber, 1); d.Oval(c, c + 2.5f, 3.5f, 2, Ramps.Char, 0);
                break;
            case HazardKind.FermentBurst:
                d.Part = 1; Blob(c, c + 2, 6, Ramps.ClothRed, 1);
                d.Part = 2; for (int i = 0; i < 5; i++) { int x = c - 6 + (int)(F(i) * 12), y = c - 4 + (int)(F(i + 7) * 10); d.Rect(x, y, 2, 2, OnggiShard, 2 + (i & 1)); }
                break;
            case HazardKind.HeaterOverload:
                d.Part = 1; d.Rect(c - 5, c - 4, 8, 8, Ramps.Char, 1); d.MarkRect(c - 4, c - 2, 6, 1, Ramps.Fire, 2);
                d.Part = 2; d.Line(c + 3, c + 2, c + 8, c + 5, Ramps.Rubber, 2); d.Line(c + 8, c + 5, c + 9, c + 8, Ramps.Rubber, 2); d.Mark(c + 6, c + 4, Ramps.Fire, 3);
                break;
            case HazardKind.PumpShock:
            case HazardKind.GreywaterJam:
                d.Part = 1; d.Oval(c - 1, c + 1, 8, 4, Ramps.Glass, 2); d.Oval(c + 4, c - 2, 4, 2, Ramps.Glass, 2);
                d.Mark(c - 4, c, Ramps.Glass, 4); d.Mark(c + 3, c - 3, Ramps.Glass, 4);
                if (k == HazardKind.PumpShock) { d.Line(c - 6, c + 3, c + 2, c, Ramps.Char, 0, mark: true); d.Mark(c + 2, c, Ramps.Cyan, 4); }
                break;
            case HazardKind.DrainBackflow:
                d.Part = 1; d.Oval(c, c, 7, 5, Ramps.Soil, 2); d.Oval(c, c, 4.5f, 3, Ramps.Soil, 1);
                d.Part = 2; d.Oval(c, c, 2.5f, 2, Ramps.Steel, 2); d.Mark(c, c, Ramps.Panel, 0);
                break;
            case HazardKind.WeevilSwarm:
                for (int i = 0; i < 9; i++) Spot(c - 8 + i * 2, c + (int)(MathF.Sin(i * 0.9f) * 2), Ramps.Ink, 1);
                for (int i = 0; i < 4; i++) Spot(c - 4 + (int)(F(i) * 10), c - 4 + (int)(F(i + 3) * 3), Ramps.Wood, 3);
                break;
            case HazardKind.PlantTopple:
                d.Part = 1; for (int i = 0; i < 7; i++) Blob(c - 2 + (int)(F(i) * 10), c - 2 + (int)(F(i + 4) * 8), 2f, Ramps.Soil, 1 + (i & 1));
                d.Part = 2; d.Rect(c - 7, c - 2, 5, 4, Ramps.Wood, 2); d.Rect(c - 3, c + 2, 3, 2, Ramps.Wood, 3);
                d.Part = 3; d.Line(c - 5, c - 3, c - 9, c - 8, Ramps.Leaf, 3); d.Line(c - 4, c - 3, c - 4, c - 8, Ramps.Leaf, 2);
                break;
            case HazardKind.MoonshineFire:
                d.Part = 1; for (int i = 0; i < 3; i++) Blob(c + i * 2, c - i * 2, 5 - i, Ramps.Char, 0);
                d.Part = 2; d.Rect(c - 6, c - 1, 6, 7, Ramps.Wood, 2); d.MarkRect(c - 6, c + 1, 6, 1, Ramps.Steel, 3); d.Line(c - 3, c - 1, c - 2, c - 4, Ramps.Char, 0, mark: true);
                d.Part = 3; d.Line(c + 1, c - 4, c + 8, c - 7, Ramps.SteelWarm, 2);
                break;
            case HazardKind.PartitionFall:
                d.Part = 1; d.Rect(c - 9, c - 2, 18, 5, Ramps.Steel, 3); d.MarkRect(c - 9, c, 18, 1, Ramps.Steel, 2);
                d.Part = 2; d.Rect(c + 5, c + 6, 2, 2, Ramps.Steel, 4); d.Rect(c - 6, c + 7, 2, 1, Ramps.Steel, 4);
                break;
            case HazardKind.LockedIn:
                for (int i = 0; i < 3; i++) d.Line(c - 3 + i * 3, c - 6, c - 2 + i * 3, c + 6, Ramps.Steel, 4, mark: true);
                Spot(c - 4, c - 7, Ramps.Steel, 5); Spot(c + 4, c + 7, Ramps.Steel, 5);
                for (int i = 0; i < 3; i++) d.Line(c - 3 + i * 3, c - 6, c - 2 + i * 3, c + 6, Ramps.Steel, 1, mark: false);
                break;
            case HazardKind.BearingWhine:
                d.Part = 1; for (int i = 0; i < 4; i++) Blob(c - 6 + i * 4, c + (int)(F(i) * 4), 1.6f + F(i + 1), OilRamp, 2);
                d.Mark(c - 6, c - 1, OilRamp, 4); d.Mark(c + 2, c, OilRamp, 4);
                break;
            case HazardKind.MeetingBrawl:
                d.Part = 1; d.Rect(c - 8, c - 2, 8, 3, Ramps.Steel, 2); d.Rect(c - 8, c + 1, 2, 4, Ramps.Steel, 2); d.Rect(c - 2, c + 1, 2, 4, Ramps.Steel, 2);
                d.Part = 2; d.Rect(c + 2, c - 5, 5, 6, Ramps.Bandage, 3); d.Rect(c + 5, c + 2, 5, 6, Ramps.Bandage, 2);
                d.MarkRect(c + 3, c - 3, 3, 1, Ramps.Ink, 2); d.MarkRect(c + 6, c + 4, 3, 1, Ramps.Ink, 2);
                break;
            case HazardKind.GravityHiccup:
                d.Part = 1; for (int i = 0; i < 6; i++) d.Rect(c - 8 + (int)(F(i) * 16), c - 6 + (int)(F(i + 6) * 12), 2, 2, Ramps.Accent[i % Ramps.Accent.Length], 3);
                for (int i = 0; i < 4; i++) Spot(c + 7, c - 5 + i * 3, Ramps.Steel, 1);
                break;
            case HazardKind.ManeuverJolt:
                for (int x = c - 9; x < c + 9; x++) { Spot(x, c - 2 + (x - c) / 6, Ramps.Rubber, 2); Spot(x, c + 3 + (x - c) / 6, Ramps.Rubber, 2); }
                break;
            case HazardKind.RebootGlitch:
                d.Part = 1; d.Rect(c - 7, c - 5, 14, 10, Ramps.Panel, 1);
                for (int y = c - 4; y < c + 4; y += 2) d.MarkRect(c - 6, y, 4 + (int)(F(y) * 8), 1, Ramps.Led, 2);
                d.Mark(c + 4, c + 3, Ramps.Led, 4);
                break;
            case HazardKind.BlackboxGap:
                d.Part = 1; d.Rect(c - 9, c - 2, 18, 4, Ramps.Hazard, 2);
                d.MarkRect(c - 3, c - 2, 6, 4, Ramps.Ink, 0);
                break;
            case HazardKind.CatLost:
                d.Part = 1; d.Oval(c - 3, c + 3, 4, 2, Ramps.ClothBlue, 3); d.Oval(c - 3, c + 2.5f, 2.5f, 1.2f, Ramps.Wood, 2);
                d.Part = 2; d.Rect(c + 3, c - 5, 6, 5, Ramps.Bandage, 3); d.MarkRect(c + 4, c - 4, 4, 1, Ramps.Ink, 2); d.MarkRect(c + 4, c - 2, 3, 1, Ramps.Ink, 2);
                break;
            case HazardKind.GravityFailure:
                for (int i = 0; i < 4; i++)
                {
                    int x = c - 7 + (int)(F(i) * 14), y = c - 7 + (int)(F(i + 4) * 14);
                    Spot(x, y, Ramps.Steel, 1); Spot(x - 1, y, Ramps.Steel, 2); Spot(x + 1, y, Ramps.Steel, 2); Spot(x, y - 1, Ramps.Steel, 2); Spot(x, y + 1, Ramps.Steel, 2);
                }
                break;
            case HazardKind.DockSealFail:
                d.Part = 1; d.Oval(c, c, 8, 6, Ramps.Cloud, 4); d.Oval(c, c, 5.5f, 4, Ramps.Panel, 1);
                for (int i = 0; i < 6; i++) Spot(c - 7 + (int)(F(i) * 14), c - 5 + (int)(F(i + 3) * 2), Ramps.Bandage, 3);
                break;
            case HazardKind.WreckDrift:
                d.Part = 1; d.Rect(c - 7, c - 5, 14, 10, Ramps.Panel, 1);
                d.Line(c - 6, c + 3, c + 5, c - 4, Ramps.Hazard, 3, mark: true);
                Spot(c + 5, c - 4, Ramps.Alarm, 4); Spot(c - 2, c, Ramps.Hazard, 4);
                break;
            case HazardKind.GammaFlash:
                d.Line(c - 9, c - 3, c + 9, c - 1, Ramps.Bandage, 3, mark: false); d.Line(c - 9, c + 1, c + 9, c + 3, Ramps.Bandage, 2, mark: false);
                break;
            case HazardKind.TidalPull:
            {
                int x = c - 10, y = c;
                for (int i = 0; i < 20; i++) { Spot(x, y, Ramps.Ink, 1); Spot(x, y + 1, Ramps.Steel, 4); x++; if ((i + seed) % 4 == 0) y += F(i) < 0.5f ? 1 : -1; }
                break;
            }
            case HazardKind.MagnetarPulse:
                d.Part = 1;
                d.Line(c - 4, c - 4, c + 2, c + 3, Ramps.Steel, 4); d.Line(c + 3, c - 4, c - 2, c + 3, Ramps.Steel, 3);
                d.Rect(c - 1, c - 1, 4, 2, Ramps.Alarm, 3); d.Rect(c - 5, c + 2, 3, 2, Ramps.Hazard, 3);
                break;
            default: // 예전 사고 70가지는 흔적을 따로 남기지 않는다 (그을음 · 물기는 바닥 층이 맡는다)
                break;
        }
        d.Bevel();
        return d.ToImg(outline: false);
    }

    private static readonly int SickRamp = Ramps.Add("#3a3c14", "#6b7024", "#a8b048", "#cdd47a");
    private static readonly int OilRamp = Ramps.Add("#060608", "#121218", "#1f1f2a", "#2f3040", "#5a5f7a");
    private static readonly int OnggiShard = Ramps.Add("#1d0f08", "#3e2214", "#5e3620", "#7f4d2f");
}
