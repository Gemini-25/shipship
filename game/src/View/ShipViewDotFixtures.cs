using System;
using Godot;
using ShipSim.Art;
using ShipSim.Core;
using FixState = ShipSim.Art.FixState;

namespace ShipSim.View;

// 도트 설비 · 가구: 몸체(그림자 포함)는 구워 두는 층에 한 번, 움직이는 부분(화면 · 불빛 · 팬 · 노심 빛 · 작물)은 설비 층(30Hz)에
// 캐시된 그림으로 겹친다. 꺼짐 · 고장은 몸체 모양 그대로 어둡게 덮는다. 고장 불꽃 · 연기 같은 효과는 예전 것을 그대로 쓴다.
public partial class ShipView
{
    /// <summary>몸체 그림의 열쇠와 재료 (주인 담요 색 · 등급 · 낡음 단계 · 설비마다 다른 씨앗).</summary>
    private static (ulong Key, int Blanket, int Grade, int Wear) DotBodyArgs(Furniture f)
    {
        int blanket = -1;
        if (f.Owner != null)
        {
            var pc = Palette.Crew(f.Owner.Id);
            blanket = Ramps.Derive((byte)(pc.R * 255), (byte)(pc.G * 255), (byte)(pc.B * 255));
        }
        var m = f.Machine;
        int grade = m == null ? 1 : (int)m.Grade;
        int wear = m == null ? 0 : Math.Min(3, FixtureArt.WearStep(m));
        return (DotTex.Key(5, (int)f.Type, f.Width, f.Height, blanket, grade, wear, f.Id), blanket, grade, wear);
    }

    /// <summary>몸체 + 오른쪽 아래로 지는 그림자 (바닥에 붙은 판은 그림자 없이).</summary>
    private static Img DotBodyImg(Furniture f, int blanket, int grade, int wear)
    {
        var body = DotMachines.Body(f.Type, f.Width, f.Height, blanket, grade, wear, f.Id);
        if (FurnitureTypes.Walkable(f.Type) || DotMachines.RiseOf(f.Type) <= 0) return body;
        var o = new Img(body.W + 3, body.H + 2);
        o.DrawShadowOf(body, 0, 0, 0.3f);
        o.Draw(body, 0, 0);
        return o;
    }

    private Vector2 DotFixtureAt(Furniture f) => new(f.MinX * T, f.MinY * T - DotMachines.RiseOf(f.Type));

    /// <summary>구워 두는 층: 도트 몸체. 그렸으면 true.</summary>
    private bool PaintDotFixtureBody(CanvasItem ci, Furniture f)
    {
        var (key, blanket, grade, wear) = DotBodyArgs(f);
        var e = Dots.Get(key, () => DotBodyImg(f, blanket, grade, wear), force: true);
        if (e == null || e.W == 0) return e != null;
        ci.DrawTextureRect(Zoom >= 0.999f ? e.Crisp : e.Tex, new Rect2(DotFixtureAt(f), e.W, e.H), false);
        return true;
    }

    /// <summary>움직이는 부분의 상태: 돌아감 · 꺼짐(전기 없음 · 세워 둠) · 고장.</summary>
    private static FixState DotFixState(FixtureArt.State s) => s switch
    {
        FixtureArt.State.Fault or FixtureArt.State.Broken or FixtureArt.State.Wrecked => FixState.Broken,
        FixtureArt.State.Off or FixtureArt.State.Parked => FixState.Off,
        _ => FixState.On,
    };

    /// <summary>종류마다 다른 정도 (노심 출력 · 엔진 연소 · 작물 자람 · 전지 충전) 0~1.</summary>
    private float DotFixLevel(Furniture f, Machine? m)
    {
        switch (f.Type)
        {
            case FurnitureType.ReactorCore:
                return Mathf.Clamp(_world.Power.ReactorOutput / Mathf.Max(1f, _world.Power.ReactorRated), 0.05f, 1f);
            case FurnitureType.EngineCore:
                return _world.Propulsion.Burning || _world.Propulsion.CourseBurnVisible ? 1f : 0.2f;
            case FurnitureType.Battery:
                return _world.Power.BatteryPercent;
            default:
                return m?.Crop is CropState crop ? crop.Growth : 1f;
        }
    }

    /// <summary>설비 층: 도트 움직임 + 꺼짐/고장 어둠 + 예전 상태 효과(불꽃 · 연기 · 아지랑이 · 가스 · 분말 · 병충해 · 상함).</summary>
    private void PaintDotFixtureLife(CanvasItem ci, Furniture f)
    {
        var r = FurnitureRect(f);
        if (!_fixView.Intersects(r.Grow(T * 4f))) return; // 화면 밖은 건너뛴다
        var m = f.Machine;
        float t = _time + f.Id * 0.73f;
        var s = FixtureArt.StateOf(f, m);
        var st = DotFixState(s);
        bool crisp = Zoom >= 0.999f;
        var at = DotFixtureAt(f);
        // 꺼짐 · 세워 둠 · 고장: 몸체 모양 그대로 어둡게
        float dark = s switch { FixtureArt.State.Off => 0.34f, FixtureArt.State.Parked => 0.22f, FixtureArt.State.Broken or FixtureArt.State.Wrecked => 0.2f, _ => 0f };
        if (dark > 0f && Dots.Peek(DotBodyArgs(f).Key) is { W: > 0 } be)
            ci.DrawTextureRect(crisp ? be.Crisp : be.Tex, new Rect2(at, be.W, be.H), false, new Color(0f, 0.01f, 0.03f, dark));
        int frame = (int)(t * 6f) & 7;
        int lv = (int)MathF.Round(Math.Clamp(DotFixLevel(f, m), 0f, 1f) * 8f);
        var e = Dots.Get(DotTex.Key(6, (int)f.Type, f.Width, f.Height, (int)st, frame, lv),
            () => DotMachines.Life(f.Type, f.Width, f.Height, st, frame, lv / 8f));
        if (e is { W: > 0 }) ci.DrawTextureRect(crisp ? e.Crisp : e.Tex, new Rect2(at, e.W, e.H), false);
        if (f.Type == FurnitureType.GrowBed && m?.Crop is CropState cs) PaintBlight(ci, f, cs);
        if (f.Type is FurnitureType.Fridge or FurnitureType.MealDispenser) PaintTaint(ci, f);
        FixtureArt.PaintState(ci, f, _world, t, _fixZoom, dot: true);
    }
}
