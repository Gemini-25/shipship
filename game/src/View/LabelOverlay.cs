using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 방 이름·보기 모드 수치·승무원 라벨·말풍선을 화면 좌표로 그린다.
/// 월드에 직접 그리면 확대할 때 글자가 뭉개지므로, 카메라 변환만 빌려서 늘 선명한 크기로 그린다.
/// </summary>
public partial class LabelOverlay : Node2D
{
    private Main _main = null!;
    private World _world = null!;
    private float _time;

    public void Init(Main main, World world)
    {
        _main = main;
        _world = world;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        QueueRedraw();
    }

    public override void _Draw() { long fp = FrameProbe.Now; DrawLabels(); FrameProbe.Add("Labels", fp); } // v17.7 프레임 시간 재기

    private void DrawLabels()
    {
        var xf = GetViewport().GetCanvasTransform();
        float zoom = xf.X.Length();
        _zoom = zoom;
        const float T = ShipView.T;
        var mode = _main.ViewMode;

        bool crisis = Severity.Crisis(_world);
        if (ZoomDetail.Shows(zoom, Detail.RoomIcon)) PaintRoomIcons(xf, zoom); // v16.24 멀리: 방 가운데 큰 아이콘 (이름 대신)
        if (zoom >= 0.35f)
        {
            foreach (var room in _world.Ship.Rooms)
            {
                if (room.Detached) continue; // 떠다니는 조각은 따로 (PaintFragmentLabels)
                var severity = Severity.Of(_world, room);
                var accent = Palette.Room(room.Kind);
                bool focus = room == _main.HoveredRoom || room == _main.SelectedRoom;
                int size = zoom >= 0.9f ? 12 : 11;
                Vector2 p;
                if (room.Type == RoomType.Corridor)
                {
                    // 통로는 이름표 없이, 보기 모드 수치나 사고 배지만
                    if (mode == ViewMode.Normal && RoomAlert(room).text.Length == 0) continue;
                    p = xf * (ShipView.ToPx(room.Center) + new Vector2(-T * 3f, -T * 0.6f));
                }
                else if (!ZoomDetail.Shows(zoom, Detail.RoomLabel) && !focus) { } // v16.24 멀리선 이름 대신 아이콘 (고르거나 올리면 이름)
                else
                {
                    p = xf * _main.ShipView.RoomLabelAnchor(room);
                    this.Circle(p + new Vector2(4f, 0f), 2.5f, accent.WithAlpha(focus ? 1f : 0.75f), true, -1f, true);
                    Gfx.Text(this, Fonts.Bold, p + new Vector2(11f, Gfx.CenterOffset(Fonts.Bold, size)), room.Name, size,
                        accent.WithAlpha(focus ? 1f : 0.7f));
                    // 용도가 바뀐 방: 원래 이름 옆에 지금 쓰임새
                    if (room.Purpose is string purpose)
                    {
                        float nw = Gfx.Width(Fonts.Bold, room.Name, size);
                        Gfx.Text(this, Fonts.Body, p + new Vector2(17f + nw, Gfx.CenterOffset(Fonts.Body, size - 1)), "→ " + purpose, size - 1,
                            new Color("#e0b64a").WithAlpha(purpose.Contains("비어") ? 0.5f : 0.9f));
                    }
                }

                // 방 가운데 위쪽에 수치 배지
                var anchor = new Vector2((room.MinX + room.MaxX + 1) * 0.5f * T, (room.MinY + 0.55f) * T);
                if (room.Type == RoomType.Corridor) anchor = ShipView.ToPx(room.Center);
                int fs = zoom < 0.7f ? 10 : 11;
                var at = xf * anchor;

                // 치명: 방 한가운데 하나의 큰 상태만 (세부 수치·배지는 겹쳐 봐야 소음)
                if (severity == RoomSeverity.Critical)
                {
                    var (title, sub) = Severity.CriticalLabel(_world, room);
                    bool fire = title == "화재";
                    var col = fire ? new Color("#ff9a3c") : Palette.Danger;
                    var mid = xf * ShipView.ToPx(room.Center);
                    int big = zoom < 0.7f ? 16 : 20;
                    float pulse = 0.75f + 0.25f * Mathf.Sin(_time * 4f);
                    float tw = Gfx.Width(Fonts.Bold, title, big);
                    Gfx.RoundRect(this, new Rect2(mid.X - tw * 0.5f - 12f, mid.Y - big * 0.9f, tw + 24f, big * 1.25f + 16f),
                        new Color(0.06f, 0.02f, 0.03f, 0.78f), 8, col.WithAlpha(0.55f));
                    Gfx.TextCentered(this, Fonts.Bold, mid + new Vector2(0f, -4f), title, big, col.WithAlpha(pulse));
                    Gfx.TextCentered(this, Fonts.Body, mid + new Vector2(0f, big * 0.55f + 4f), sub, 10, Palette.TextDim);
                    continue;
                }
                if (severity == RoomSeverity.Abandoned)
                {
                    var mid = xf * ShipView.ToPx(room.Center);
                    Gfx.TextCentered(this, Fonts.Bold, mid + new Vector2(0f, -6f), "폐쇄 구역", zoom < 0.7f ? 12 : 14, new Color(0.75f, 0.77f, 0.82f, 0.8f));
                    if (room.AbandonReason is string why && zoom >= 0.6f)
                        Gfx.TextCentered(this, Fonts.Body, mid + new Vector2(0f, 12f), why, 10, Palette.TextMuted);
                    continue;
                }

                if (mode != ViewMode.Normal)
                {
                    var (text, color) = Readout(room, mode);
                    if (text.Length > 0)
                    {
                        Gfx.Pill(this, Fonts.Bold, at, text, fs, color, new Color(0.03f, 0.04f, 0.06f, 0.88f), color.WithAlpha(0.45f), 6f, 3f);
                        at += new Vector2(0f, 20f);
                    }
                }

                // 사고 배지: 어떤 보기 모드에서든 보인다. 방 이름표 바로 옆(벽 위)에 붙여 바닥의 사람을 가리지 않게.
                var (alert, alertColor) = RoomAlert(room);
                if (alert.Length > 0)
                {
                    float pulse = 0.75f + 0.25f * Mathf.Sin(_time * 5f);
                    // 방 아래쪽 가운데 (위쪽은 이름표와 보기 모드 수치 자리)
                    var where = room.Type == RoomType.Corridor ? at
                        : xf * new Vector2((room.MinX + room.MaxX + 1) * 0.5f * T, (room.MaxY + 0.5f) * T);
                    Gfx.Pill(this, Fonts.Bold, where, alert, fs, alertColor.WithAlpha(pulse), new Color(0.09f, 0.03f, 0.04f, 0.92f), alertColor.WithAlpha(0.6f), 6f, 3f);
                }
            }
        }

        PaintJumperLabels(xf, zoom, mode);
        PaintFragmentLabels(xf, zoom);
        PaintDroneLabels(xf, zoom, mode);

        foreach (var c in _world.Crew)
        {
            if (c.Away && !c.Dead) continue; // 배에 없는 사람 (원정 · 하선) — 배 밖 빈 자리에 이름표를 띄우지 않는다
            var col = Palette.Crew(c.Id);
            bool selected = c == _main.SelectedCrew;
            bool hovered = c == _main.HoveredCrew;
            float r = _main.ShipView.CrewRadius;
            var head = xf * (_main.ShipView.CrewPx(c) + new Vector2(0f, -r - 4f));

            // 경보를 막 들었으면 느낌표
            if (_world.Tick - c.AlertedTick < SimTime.Minutes(3))
            {
                var ap = head + new Vector2(-14f, -8f);
                this.Circle(ap, 7f, Palette.Danger, true, -1f, true);
                Gfx.TextCentered(this, Fonts.Bold, ap, "!", 11, Colors.White);
            }

            if (c.Pose == Pose.Sleeping)
            {
                for (int k = 0; k < 3; k++)
                {
                    float phase = Mathf.PosMod(_time * 0.45f + k / 3f, 1f);
                    var zp = head + new Vector2(8f + phase * 10f, -phase * 20f * Mathf.Max(zoom, 0.6f));
                    float alpha = Mathf.Sin(phase * Mathf.Pi) * 0.8f;
                    Gfx.Text(this, Fonts.Bold, zp, "z", 10 + k * 2, new Color(0.8f, 0.85f, 0.95f, alpha));
                }
                if (!selected && !hovered) continue;
            }

            // 대화 중이면 말풍선
            if (c.TalkingTo != null && c.Job?.Activity is ChatActivity)
            {
                var bp = head + new Vector2(16f, -10f);
                var bubble = new Rect2(bp.X - 13f, bp.Y - 8f, 26f, 16f);
                Gfx.RoundRect(this, bubble, new Color(0.92f, 0.94f, 0.98f, 0.92f), 8);
                this.Poly(new[] { bp + new Vector2(-8f, 6f), bp + new Vector2(-2f, 7f), bp + new Vector2(-12f, 12f) }, new Color(0.92f, 0.94f, 0.98f, 0.92f));
                for (int k = 0; k < 3; k++)
                {
                    float up = Mathf.Max(0f, Mathf.Sin(_time * 5f - k * 0.8f)) * 2f;
                    this.Circle(bp + new Vector2(-6f + k * 6f, -up), 1.8f, new Color("#2a3140"), true, -1f, true);
                }
            }

            // v12.7 말다툼 (붉은 번개 말풍선) · 슬픔 (푸른 물방울)
            if (!c.Dead && _world.Tick - c.Quarrel < SimTime.Minutes(40))
            {
                var bp = head + new Vector2(-16f, -12f);
                float shake = Mathf.Sin(_time * 30f) * 1.2f;
                var bubble = new Rect2(bp.X - 11f + shake, bp.Y - 8f, 22f, 16f);
                Gfx.RoundRect(this, bubble, new Color("#3a1416").WithAlpha(0.95f), 6, Palette.Danger, 1);
                Gfx.TextCentered(this, Fonts.Bold, bubble.GetCenter() + new Vector2(0, 1), "#!", 11, new Color("#ff8a80"));
            }
            else if (!c.Dead && c.GriefUntil > _world.Tick && c.Pose != Pose.Sleeping)
            {
                float t = Mathf.PosMod(_time * 0.6f + c.Id * 0.3f, 1f);
                this.Circle(head + new Vector2(6f, 2f + t * 8f), 1.8f, new Color("#9fc4ff").WithAlpha(1f - t), true, -1f, true);
            }

            if (ZoomDetail.Shows(zoom, Detail.NameTag) && !c.Dead && c.CarriedBy == null) PaintNameTag(c, xf, col, selected || hovered); // v16.24 가까이: 이름표
            if (!ZoomDetail.Shows(zoom, Detail.StatusIcon) && !selected && !hovered && !c.Down) continue; // v16.24 멀리선 점만 (상태 아이콘은 중간부터)
            // 위기 중에는 사고에 얽힌 사람만 라벨을 남긴다 (나머지는 가까이 보거나 고르면 보인다)
            if (crisis && !selected && !hovered && zoom < 1.1f && !Severity.Notable(c)) continue;

            var center = head + new Vector2(0f, -9f);
            if (c.CarriedBy != null && !selected && !hovered) continue; // 업은 사람의 라벨("구조")로 충분
            if (c.Dead || c.Down)
            {
                string state = c.Dead ? "사망" : c.CarriedBy != null ? $"업혀 감 · {c.CarriedBy.Name}" : c.CareBed != null ? "의식 없음 · 치료 중" : "쓰러짐";
                var sc = c.Dead ? Palette.TextMuted : Palette.Danger;
                Gfx.Pill(this, Fonts.Bold, center, selected || hovered ? $"{c.Name} · {state}" : state, zoom < 0.7f ? 10 : 11,
                    sc, new Color(0.08f, 0.03f, 0.04f, 0.9f), sc.WithAlpha(0.6f));
                continue;
            }
            // v17.1 글자 딱지 → [행동 아이콘 | 감정 그림] (글자는 가까이 확대했을 때만)
            bool emergency = Severity.Notable(c) && crisis;
            HeadBadge.Draw(this, _world, c, center, zoom, selected, hovered, emergency, col, _time);
        }
    }

    /// <summary>v16.24 멀리: 방마다 가운데에 그 방 종류 아이콘 (방 색 · 크기는 방에 맞춰). 사고가 난 방은 그 뜻 색.</summary>
    private void PaintRoomIcons(Transform2D xf, float zoom)
    {
        const float T = ShipView.T;
        foreach (var room in _world.Ship.Rooms)
        {
            if (room.Detached || room.Type == RoomType.Corridor) continue;
            var c = xf * new Vector2((room.MinX + room.MaxX + 1) * 0.5f * T, (room.MinY + room.MaxY + 1) * 0.5f * T);
            float w = (room.MaxX - room.MinX + 1) * T * zoom, h = (room.MaxY - room.MinY + 1) * T * zoom;
            float size = Mathf.Clamp(Mathf.Min(w, h) * 0.45f, 10f, 30f);
            var sev = Severity.Of(_world, room);
            var col = sev == RoomSeverity.Critical ? Palette.Danger : Palette.Room(room.Kind).Lightened(0.15f);
            this.Circle(c, size * 0.72f, new Color(0.02f, 0.03f, 0.05f, 0.55f), true, -1f, true);
            Icons.Draw(this, Icons.Room(room.Kind), c, size, col.WithAlpha(0.9f));
        }
    }

    /// <summary>v16.24 가까이: 발밑 이름표 (사람 색 점 · 이름). 고르거나 올리면 밝게.</summary>
    private void PaintNameTag(CrewMember c, Transform2D xf, Color col, bool focus)
    {
        float r = _main.ShipView.CrewRadius;
        var at = xf * (_main.ShipView.CrewPx(c) + new Vector2(0f, r + 7f));
        string name = c.Name;
        float w = Gfx.Width(Fonts.Body, name, Ui.TextTiny) + 14f;
        var tag = new Rect2(at.X - w * 0.5f, at.Y - 7f, w, 13f);
        Gfx.RoundRect(this, tag, new Color(0.03f, 0.04f, 0.06f, focus ? 0.92f : 0.7f), 4f, col.WithAlpha(focus ? 0.8f : 0.35f));
        this.Circle(new Vector2(tag.Position.X + 5f, at.Y - 0.5f), 2f, col, true, -1f, true);
        Gfx.Text(this, Fonts.Body, new Vector2(tag.Position.X + 9f, at.Y + Gfx.CenterOffset(Fonts.Body, Ui.TextTiny) - 0.5f), name, Ui.TextTiny,
            focus ? Palette.Text : Palette.TextDim);
    }

    /// <summary>방에서 벌어지는 사고를 한 줄로.</summary>
    private float _zoom = 1f;

    private (string text, Color color) RoomAlert(Room room)
    {
        var parts = new System.Collections.Generic.List<string>();
        var color = Palette.Warning;
        int fires = _world.Fire.CountIn(room);
        if (fires > 0 && _world.Fire.IsKnown(room)) { parts.Add($"화재 {fires}칸"); color = Palette.Danger; }
        if (room.Leaking) { parts.Add($"감압 {room.Air.Pressure:0}kPa"); color = Palette.Danger; }
        else if (room.Lockdown) parts.Add(room.Air.Pressure < 90f ? $"격벽 폐쇄 · {room.Air.Pressure:0}kPa" : "격벽 폐쇄");
        if (room.Air.Smoke > 0.25f && fires == 0) parts.Add("연기");
        if (room.Air.Toxin > 0.1f) { parts.Add($"유독 가스 {room.Air.Toxin * 100:0}%"); if (room.Air.Toxin > 0.2f) color = Palette.Danger; } // v11.2
        // v8 구조: 사출 준비, 하중이 몰린 방
        if (room.Jettison is JettisonPlan jp) { parts.Add($"사출 준비 · {JettisonPlan.StageName(jp.Stage)}"); color = Palette.Danger; }
        else if (room.DesignJoints > 0)
        {
            float stress = StructureSystem.StressOf(StructureSystem.KnownCapacity(room), room.DesignJoints, StructureSystem.FrameLost(_world, room));
            if (stress > 1f) parts.Add($"하중 {stress * 100:0}%");
            else if (room.Joints.Any(j => j.KnownBroken && !j.Released)) parts.Add($"연결부 {room.Joints.Count(j => !j.KnownBroken)}/{room.Joints.Count}");
        }
        return (string.Join(" · ", parts), color);
    }

    private (string text, Color color) Readout(Room room, ViewMode mode)
    {
        var air = room.Air;
        switch (mode)
        {
            case ViewMode.Ambience: // v12.6 소음·진동·냄새·방사선 · 잠의 질
            {
                var parts = new System.Collections.Generic.List<string>();
                if (room.Noise > 0.06f) parts.Add($"소음 {room.Noise * 100:0}");
                if (room.Vibration > 0.06f) parts.Add($"진동 {room.Vibration * 100:0}");
                if (room.Smell > 0.06f) parts.Add($"냄새 {room.Smell * 100:0}");
                if (room.Radiation > 0.08f) parts.Add($"방사선 {room.Radiation * 100:0}");
                if ((RoomCatalog.Tags(room.Kind) & RoomTag.Sleep) != 0) parts.Add($"잠 {AmbienceSystem.SleepFactor(room) * 100:0}%");
                if (room.Compartment >= 0 && room.Type == RoomType.Corridor) parts.Add($"{room.Compartment + 1}구획");
                float top = Mathf.Max(Mathf.Max(room.Noise, room.Vibration), Mathf.Max(room.Smell, room.Radiation * 1.5f));
                return (string.Join(" · ", parts), room.Radiation > 0.2f ? new Color("#b58cff") : top > 0.35f ? Palette.Warning : top > 0.1f ? new Color("#f5d547") : Palette.Good);
            }
            case ViewMode.Power:
                return room.Powered
                    ? ($"{PowerGrid.CircuitName(room.Circuit)} 정상", Palette.Good)
                    : ($"{PowerGrid.CircuitName(room.Circuit)} 정전", Palette.Danger);
            case ViewMode.Air:
            {
                float bad = Mathf.Clamp((20.5f - air.O2) / 5f, 0f, 1f);
                var col = air.CO2 > 1f || bad > 0.05f ? Palette.Severity(0.4f + bad * 0.6f) : Palette.Accent;
                return air.Pressure < 95f
                    ? ($"O2 {air.O2:0.0} · {air.Pressure:0}kPa", Palette.Danger)
                    : ($"O2 {air.O2:0.0} · CO2 {air.CO2:0.00}", col);
            }
            case ViewMode.Temperature:
            {
                var col = air.Temperature < 15f ? new Color("#6cb8ff") : air.Temperature > 28f ? new Color("#ff7a5c") : Palette.TextDim;
                return ($"{air.Temperature:0.0}°C", col);
            }
            case ViewMode.Trace:
            {
                // 고른 사람이 있으면 그 사람의 공포, 아니면 이 방이 겪은 것
                if (_main.SelectedCrew is CrewMember who)
                {
                    float fear = who.Memory.FearOf(room);
                    return fear >= 0.08f ? ($"{who.Name} 공포 {fear * 100:0}%", ShipView.FearColor.Lightened(0.3f)) : ("", Palette.TextMuted);
                }
                // 멀리서 볼 때는 짧게 (좁은 방에서 이웃 방 표시와 겹치지 않게)
                bool compact = _zoom < 0.8f || (room.MaxX - room.MinX + 1) < 7;
                var parts = new System.Collections.Generic.List<string>();
                if (room.Breaches > 0) parts.Add(compact ? $"뚫림{room.Breaches}" : $"뚫림 {room.Breaches}");
                if (room.Fires > 0) parts.Add(compact ? $"불{room.Fires}" : $"화재 {room.Fires}");
                if (room.TimesAbandoned > 0) parts.Add(compact ? $"포기{room.TimesAbandoned}" : $"포기 {room.TimesAbandoned}");
                if (room.Collapses > 0) parts.Add(compact ? $"쓰러짐{room.Collapses}" : $"쓰러짐 {room.Collapses}");
                if (room.Deaths > 0) parts.Add(compact ? $"사망{room.Deaths}" : $"사망 {room.Deaths}");
                int plates = _world.Ship.Walls.Count(kv => kv.Value.Reinforced && Hull.InsideRoom(_world.Ship, kv.Key) == room);
                if (plates > 0) parts.Add(compact ? $"보강{plates}" : $"보강 {plates}칸");
                if (room.Suppression) parts.Add(compact ? "소화" : "소화 장치");
                if (room.FormerPurposes.Count > 0 && !compact) parts.Add($"예전: {room.FormerPurposes[^1]}");
                bool hurt = room.Deaths > 0 || room.Collapses > 0 || room.TimesAbandoned > 0;
                return (string.Join(compact ? " " : " · ", parts), hurt ? Palette.Warning : parts.Count > 0 ? Palette.TextDim : Palette.TextMuted);
            }
            case ViewMode.Structure:
            {
                if (room.DesignJoints <= 0) return ("용골", Palette.TextMuted);
                int frames = StructureSystem.FrameLost(_world, room);
                float stress = StructureSystem.StressOf(StructureSystem.KnownCapacity(room), room.DesignJoints, frames);
                string joints = string.Join(" ", room.Joints.Select(j => j.Released ? "풀림" : j.KnownBroken ? "✕" : $"{j.Known * 100:0}"));
                bool unseen = _world.Structure.Unseen.ContainsKey(room.Id);
                float ttf = StructureSystem.HoursToFailure(room, true, frames);
                string eta = ttf < 72f ? $" · {ttf:0}시간" : "";
                var sev = Palette.Severity(Mathf.Clamp((stress - 0.5f) / 0.8f, 0f, 1f));
                // 좁은 방은 하중만 (연결부 숫자는 벽의 표식에 붙어 있다). 멀쩡한 방은 짧게
                bool narrow = _zoom < 0.8f || (room.MaxX - room.MinX + 1) < 9;
                bool fine = stress <= 0.6f && !unseen && room.Joints.All(j => j.Known >= 0.95f);
                if (fine) return (narrow ? $"{stress * 100:0}%" : $"하중 {stress * 100:0}%", Palette.TextMuted);
                if (narrow) return ($"{stress * 100:0}%{(unseen ? " ?" : "")}{(ttf < 72f ? $" {ttf:0}h" : "")}", sev);
                return ($"하중 {stress * 100:0}% · {joints}{(unseen ? " · 미확인" : "")}{eta}", sev);
            }
            case ViewMode.Pipes:
            {
                var segs = _world.Piping.Segments.Where(x => x.ValveRoom == room).ToList();
                if (segs.Count == 0)
                    return room.Type == RoomType.Hydroponics && !_world.Piping.WaterTo(room) ? ("물이 끊겼다", Palette.Warning) : ("", Palette.TextMuted);
                bool narrow = _zoom < 0.8f || (room.MaxX - room.MinX + 1) < 7;
                string Short(PipeSegment x) => x.Role switch
                {
                    PipeRole.HotLeg => "고온관", PipeRole.ColdLeg => "귀환관", PipeRole.Branch => $"{x.Branch + 1}번 루프",
                    PipeRole.WaterMain => "급수", _ => "보충관",
                };
                var bad = segs.Where(x => !x.Sound || x.Closed || x.Leaking || x.Bypass > 0f).ToList();
                if (bad.Count == 0) return (narrow ? "정상" : string.Join(" · ", segs.Select(Short)) + " 정상", Palette.TextMuted);
                string text = string.Join(narrow ? " " : " · ", bad.Select(x => $"{Short(x)} {x.StateText}" + (x.Leaking && !narrow ? $" {x.LeakRate:0}L" : "")));
                return (text, bad.Any(x => x.Leaking || x.Severed) ? Palette.Danger : Palette.Warning);
            }
            case ViewMode.Condition:
            {
                var machines = room.Furniture.Where(f => f.Machine != null).Select(f => f.Machine!).ToList();
                if (machines.Count == 0) return ("", Palette.TextMuted);
                int faults = machines.Sum(m => m.Faults.Count);
                float wear = machines.Max(m => m.Wear);
                return faults > 0
                    ? ($"고장 {faults}", Palette.Danger)
                    : ($"마모 {wear * 100:0}%", Palette.Severity(wear));
            }
        }
        return ("", Palette.TextDim);
    }

    /// <summary>떠다니는 조각 이름표: "떨어져 나간 함교 · 12칸 · 표류".</summary>
    private void PaintFragmentLabels(Transform2D xf, float zoom)
    {
        foreach (var f in _world.Structure.Fragments)
        {
            if (f.State == FragmentState.Lost) continue;
            var at = xf * (ShipView.ToPx(f.Center) + new Vector2(0f, ShipView.T * 1.2f));
            string state = f.State switch
            {
                FragmentState.Towed => $"견인 중 · {f.Distance:0}칸",
                FragmentState.Moored => "계류 · 임시 도킹 대기",
                _ => f.Velocity.LengthSquared() < 0.001f ? $"붙잡아 둠 · {f.Distance:0}칸" : $"표류 · {f.Distance:0}칸",
            };
            string title = (f.Jettisoned ? "사출한 " : "떨어져 나간 ") + f.Room.Name;
            var col = f.Jettisoned ? new Color("#b8c2d0") : Palette.Warning;
            int fs = zoom < 0.7f ? 11 : 13;
            Gfx.Pill(this, Fonts.Bold, at, title, fs, col, new Color(0.06f, 0.04f, 0.04f, 0.9f), col.WithAlpha(0.6f), 7f, 3f);
            Gfx.TextCentered(this, Fonts.Body, at + new Vector2(0f, fs + 6f), state + (f.RetrieveApproved ? " · 되찾기로 함" : ""), 10, Palette.TextDim);
        }
    }

    /// <summary>밖에 나간 드론 이름표: 하는 일 (구조 보기이거나 가까이 볼 때).</summary>
    private void PaintDroneLabels(Transform2D xf, float zoom, ViewMode mode)
    {
        if (zoom < 0.7f && mode != ViewMode.Structure) return;
        foreach (var d in _world.Drones.Drones)
        {
            if (d.State is DroneState.Docked or DroneState.Lost) continue;
            var at = xf * (ShipView.ToPx(d.Position) + new Vector2(0f, -14f)) + new Vector2(0f, -8f);
            string text = d.State == DroneState.Adrift ? $"{d.Name} · 표류 ({d.Doing})" : $"{d.Name} · {d.Doing}";
            var col = ShipView.DroneColor(d.Kind);
            Gfx.Pill(this, Fonts.Body, at, text, 10, col.Lightened(0.2f), new Color(0.03f, 0.05f, 0.07f, 0.85f), col.WithAlpha(0.45f), 5f, 2f);
        }
    }

    /// <summary>임시 배선 이름표: 쓰는 중이면 늘, 탔거나 흔적만 남은 건 전력 보기에서만.</summary>
    private void PaintJumperLabels(Transform2D xf, float zoom, ViewMode mode)
    {
        if (zoom < 0.5f) return;
        var panel = _world.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault();
        if (panel == null) return;
        var jumpers = _world.Power.Jumpers;
        for (int i = 0; i < jumpers.Count; i++)
        {
            var j = jumpers[i];
            if (!j.Active && mode is not (ViewMode.Power or ViewMode.Trace) && _main.SelectedFurniture != panel) continue;
            string name = $"{PowerGrid.CircuitName(j.From)}→{PowerGrid.CircuitName(j.To)}";
            string text = j.Permanent
                ? (j.Burnt ? $"탄 예비 배선 {name}" : j.Active ? $"예비 배선 {name} · {j.Load:0}/{j.Capacity:0}kW" : $"예비 배선 {name} · 대기")
                : j.Burnt ? $"탄 배선 {name}" : j.Active ? $"임시 배선 {name} · {j.Load:0}/{j.Capacity:0}kW" : $"배선 흔적 {name}";
            var col = j.Burnt ? Palette.TextMuted : j.Active && j.Load > j.Capacity ? Palette.Danger : j.Permanent ? ShipView.FeederColor.Lightened(0.2f) : ShipView.JumperColor;
            // 배전반 오른쪽에 차례로 (방 가운데의 큰 상태 표시와 겹치지 않게)
            int fsz = zoom < 0.8f ? 10 : 11;
            var pr = ShipView.FurnitureRect(panel);
            var at = xf * new Vector2(pr.End.X + 4f, pr.Position.Y + 6f) + new Vector2(Gfx.Width(Fonts.Bold, text, fsz) * 0.5f + 8f, i * 20f);
            Gfx.Pill(this, Fonts.Bold, at, text, fsz, col, new Color(0.08f, 0.05f, 0.02f, 0.9f), col.WithAlpha(0.5f), 6f, 3f);
        }
    }
}
