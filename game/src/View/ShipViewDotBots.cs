using System;
using Godot;
using ShipSim.Art;
using ShipSim.Core;

namespace ShipSim.View;

// 도트 로봇 · 드론: 시뮬레이션의 종류 · 색 · 상태 · 짐 · 방향을 도트 겉모습으로 옮겨 그린다 (읽기만).
public partial class ShipView
{
    private readonly BotLook _botLook = new();

    private static int BotBadge(RobotKind k) => k switch
    {
        RobotKind.Courier => 1, RobotKind.Tanker => 2, RobotKind.Stocker => 3, RobotKind.Lineman => 4, RobotKind.Assistant => 5, RobotKind.Overhauler => 6,
        RobotKind.Harvester => 7, RobotKind.Tender => 8, RobotKind.Sentry => 9, RobotKind.Firefighter => 10, RobotKind.Utility => 11, _ => 0,
    };

    private static int DroneBadge(DroneKind k) => k switch
    {
        DroneKind.Scout => 9, DroneKind.Surveyor => 6, DroneKind.Welder => 10, DroneKind.Radiator => 2, DroneKind.Tug => 3, DroneKind.Rigger => 4, _ => 0,
    };

    private static int RampOf(Color c) => Ramps.Derive((byte)(c.R * 255), (byte)(c.G * 255), (byte)(c.B * 255));

    /// <summary>로봇 몸 (발이 p 아래 9도트). 그렸으면 true — 상태 불빛 · 배터리 막대 · 고장 표시는 그리는 쪽이 덧그린다.</summary>
    private bool PaintDotRobot(CanvasItem ci, Robot r, Vector2 p, bool working, bool moving)
    {
        if (!DotCrew || ZoomDetail.Shows(Zoom, Detail.CrewDot)) return false;
        var l = _botLook;
        var kind = r.Kind;
        l.Shape = kind switch
        {
            RobotKind.Nurse => DotBotKind.Nurse,
            RobotKind.Stretcher => DotBotKind.Stretcher,
            _ => RobotsV15.Base(kind) switch { RobotKind.Hauler => DotBotKind.Hauler, RobotKind.Gardener => DotBotKind.Gardener, RobotKind.Safety => DotBotKind.Safety, _ => DotBotKind.Maintainer },
        };
        l.Paint = l.Shape is DotBotKind.Nurse or DotBotKind.Stretcher ? Ramps.ClothWhite : RampOf(RobotColor(kind));
        l.Badge = BotBadge(kind);
        bool dead = r.State is RobotState.Stalled or RobotState.Towed || r.Disabled;
        l.State = r.Fault != null && !RobotSystem.CanSelfRepair(r) ? DotBotState.Broken : dead ? DotBotState.Off : working ? DotBotState.Working : DotBotState.Ok;
        l.Cargo = r.Cargo is ItemStack st ? RampOf(Palette.Item(st.Kind)) : r.Order?.Kind == WorkKind.CarryWater ? Ramps.Glass : -1;
        l.Seed = r.Id & 3;
        var fv = r.Facing;
        var f = MathF.Abs(fv.X) > MathF.Abs(fv.Y) * 1.15f ? (fv.X > 0 ? DotFacing.E : DotFacing.W) : fv.Y < 0 ? DotFacing.N : DotFacing.S;
        int frame = moving ? (int)(_time * 8f + r.Id) & 3 : working ? (int)(_time * 10f + r.Id) & 3 : ((int)(_time * 2f + r.Id) & 1);
        var key = DotTex.Key(2, (long)l.Shape, l.Paint, l.Badge, (long)l.State, l.Cargo, l.Seed, (long)f, frame);
        var look = l;
        var e = Dots.Get(key, () => DotBots.Robot(look, f, frame));
        if (e == null) return false;
        var o = new Vector2(Mathf.Round(p.X) - DotBots.Cx, Mathf.Round(p.Y) + 9f - DotBots.Foot);
        ci.DrawTextureRect(Zoom >= 0.999f ? e.Crisp : e.Tex, new Rect2(o, e.W, e.H), false);
        return true;
    }

    /// <summary>드론 몸 (가운데가 p). 그렸으면 true.</summary>
    private bool PaintDotDrone(CanvasItem ci, Drone d, Vector2 p, Color col, bool docked)
    {
        if (!DotCrew || ZoomDetail.Shows(Zoom, Detail.CrewDot)) return false;
        var kind = RobotsV15.Base(d.Kind) switch { DroneKind.Inspect => DotDroneKind.Inspect, DroneKind.Repair => DotDroneKind.Repair, DroneKind.Tow => DotDroneKind.Tow, _ => DotDroneKind.Build };
        bool adrift = d.State == DroneState.Adrift;
        var st = d.Wrecked ? DotBotState.Broken : docked || adrift ? DotBotState.Off : d.State == DroneState.Working ? DotBotState.Working : DotBotState.Ok;
        int body = RampOf(col);
        bool rotor = d.Hurt.Thruster < 0.6f;
        int badge = DroneBadge(d.Kind);
        int frame = st == DotBotState.Off ? 0 : (int)(_time * 12f + d.Id) % 6;
        var key = DotTex.Key(3, (long)kind, (long)st, body, rotor ? 1 : 0, badge, frame);
        var e = Dots.Get(key, () => DotBots.Drone(kind, st, frame, body, rotor, badge));
        if (e == null) return false;
        Texture2D tex = Zoom >= 0.999f ? e.Crisp : e.Tex;
        if (adrift)
        {
            // 떠다니며 구른다 (도트가 비스듬해도 잔해처럼 보이면 된다)
            float ang = Mathf.DegToRad(d.Hurt.Spin) + _time * (d.Hurt.Tumbling ? 3f : 0.6f);
            ci.DrawSetTransform(p, ang, Vector2.One);
            ci.DrawTextureRect(tex, new Rect2(-e.W / 2f, -e.H / 2f, e.W, e.H), false);
            ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            return true;
        }
        float bob = docked ? 0f : Mathf.Round(Mathf.Sin(_time * 3f + d.Id) * 1.2f);
        var o = new Vector2(Mathf.Round(p.X) - 13f, Mathf.Round(p.Y) - 12f + bob);
        ci.DrawTextureRect(tex, new Rect2(o, e.W, e.H), false);
        return true;
    }
}
