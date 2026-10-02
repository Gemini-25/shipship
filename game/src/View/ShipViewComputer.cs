using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.6 주컴퓨터 보이게 2차 (배 화면 — 읽기만 한다):
///  · 서버 랙: 모듈마다 칼날 한 장(갈래 색 · 모듈마다 다른 깜빡임 무늬, 가까이 보면 모듈 아이콘) · 부하 막대 · 팬(부하만큼 빨리, 과열이면 붉게 떨림) ·
///    재부팅(훑는 빛줄 · 진행 막대) · 업데이트 버그(지지직 어긋남) · 멎음(어둠 · 붉은 X) · 등급 눈금.
///  · 조작 빛 흐름: 컴퓨터가 손을 쓰면 서버실에서 대상 방까지 데이터선을 따라 빛이 흐르고, 닿으면 대상 방에 조치 그림이 깜빡인다 (선이 끊겼으면 끊긴 자리에서 꺼진다).
///  · 선내 방송: 방마다 벽 스피커 — 울리면 소리 고리 · 말풍선, 고장 난 스피커는 금 간 나팔 · 늘어진 전선 · 불꽃, 못 들은 방은 "…" · 들은 사람 머리 위 소리 표시, 못 들은 사람은 "?".
///  · 방 콘솔: 컴퓨터가 그 방에 대해 아는 것을 화면에 띄운다 (화재 · 대피 · 승인 기다림 · 확인 요청 · 재부팅 · 멎음 · 버그 · 링크 끊김).
///  · 홀로그램 정보판: 함교 홀로그램 옆에 뜬 판 넷 (부하 · 온도 / 30일 물 곡선 / 제안 · 오늘의 결정 / 하루 보고).
///  · "컴퓨터가 보는 배": 실제 위에 어둠을 덮고 컴퓨터가 믿는 사람 수 · 불 · 기압만 선 그림으로 — 감지기가 틀어진 방은 빈 방으로 보인다. 낡은 값은 빗금과 "n분 전".
/// </summary>
public partial class ShipView
{
    private static readonly Color CompCyan = new("#5fd8ff");
    private static readonly Color CompAmber = new("#f2c66d");
    private static readonly Color CompViolet = new("#b58cff");

    public static Color ActColor(ActKind k) => k switch
    {
        ActKind.Alarm => new Color("#ffd166"),
        ActKind.Damper => new Color("#8fcfc4"),
        ActKind.Bulkhead => new Color("#ff9a6b"),
        ActKind.Valve => new Color("#4f9fdc"),
        ActKind.Breaker or ActKind.Shed => new Color("#e8b84a"),
        ActKind.Suppress => new Color("#ff5c6c"),
        ActKind.Module => new Color("#c9a0ff"),
        ActKind.Zone => new Color("#6ee7b7"),
        ActKind.Proposal => CompAmber,
        ActKind.Broadcast => CompViolet,
        ActKind.Reboot => new Color("#e6eaf2"),
        ActKind.Door => new Color("#5fd0c8"),
        ActKind.Forecast => new Color("#9fd8ff"),
        _ => new Color("#7cc4ff"),
    };

    // ───────────────────── 프레임마다 한 번 ─────────────────────

    private Furniture? _compBody;
    private Room? _compRoom;

    /// <summary>사람 밑 층 (설비 · 방 위): 랙 · 빛 흐름 · 스피커 · 콘솔 · 홀로그램 정보판 · 제안 · 확인.</summary>
    private void PaintComputerWorld(CanvasItem ci)
    {
        var a = _world.Automation;
        _compBody = a.ComputerBody;
        _compRoom = _compBody?.Room is Room cr && !cr.Detached ? cr : null;
        foreach (var f in _world.Ship.FurnitureOf(FurnitureType.MainComputer)) PaintRack(ci, f, f == _compBody);
        PaintConsoleScreens(ci);
        PaintSpeakers(ci);
        PaintRoomAsks(ci);
        PaintBrainAsks(ci); // v16.16 부탁 · 쉼 · 의심하는 계기 · 회의 안건
        PaintDataFlows(ci);
        PaintHoloBoard(ci);
        if (a.ShelterCall) PaintShelterCall(ci);
    }

    /// <summary>대피 방송이 살아 있는 동안: 대피소에 보랏빛 방패와 모이는 고리 (들은 사람이 여기로 온다).</summary>
    private void PaintShelterCall(CanvasItem ci)
    {
        var (shelter, _) = Facilities.Best(_world.Ship, "shelter", r => !r.Detached && !r.OffLimits && !r.Leaking);
        if (shelter == null) return;
        var c = ToPx(shelter.Center);
        for (int k = 0; k < 3; k++)
        {
            float q = Mathf.PosMod(_time * 0.6f + k / 3f, 1f);
            ci.DrawArc(c, T * (2.2f - 1.8f * q), 0f, Mathf.Tau, 36, CompViolet.WithAlpha(0.5f * q), 1.4f, true); // 바깥에서 안으로 모인다
        }
        float pulse = 0.75f + 0.25f * Mathf.Sin(_time * 3f);
        var shield = new[] { c + new Vector2(0, -11), c + new Vector2(8, -7), c + new Vector2(7, 3), c + new Vector2(0, 11), c + new Vector2(-7, 3), c + new Vector2(-8, -7) };
        ci.DrawColoredPolygon(shield, new Color(0.12f, 0.08f, 0.2f, 0.85f));
        ci.DrawPolyline(new[] { shield[0], shield[1], shield[2], shield[3], shield[4], shield[5], shield[0] }, CompViolet.WithAlpha(pulse), 1.6f, true);
        ci.DrawLine(c + new Vector2(0, -6), c + new Vector2(0, 6), CompViolet.WithAlpha(pulse), 1.2f);
        ci.DrawLine(c + new Vector2(-4, -1), c + new Vector2(4, -1), CompViolet.WithAlpha(pulse), 1.2f);
        if (Zoom > 0.8f) Gfx.TextCentered(ci, Fonts.Bold, c + new Vector2(0, 20f), "방송 — 대피소", 9, CompViolet);
    }

    /// <summary>사람 위 층: 방송 말풍선 · 들은/못 들은 표시 · "컴퓨터가 보는 배".</summary>
    private void PaintComputerTop(CanvasItem ci, ViewMode mode)
    {
        PaintBroadcastBubbles(ci);
        if (mode == ViewMode.Belief || _main.SecondaryView == ViewMode.Belief) PaintBeliefOverlay(ci, mode == ViewMode.Belief);
    }

    // ───────────────────── 서버 랙 ─────────────────────

    private static readonly ComputerModule[] AllModules = Enum.GetValues<ComputerModule>();

    private void PaintRack(CanvasItem ci, Furniture f, bool main)
    {
        var w = _world;
        var a = w.Automation;
        var m = f.Machine;
        var r = FurnitureRect(f);
        float t = _time + f.Id * 0.37f;
        bool online = main && a.MainOnline;
        bool rebooting = main && a.Rebooting;
        bool wrecked = m != null && (m.Has(FaultKind.Wrecked) || m.Has(FaultKind.Stripped));
        float temp = f.Room.Air.Temperature;
        float heat = Mathf.Clamp((temp - 30f) / (AutomationSystem.OverheatC - 30f), 0f, 1.3f);
        float load = main ? a.Load : 0f;
        bool close = Zoom > 1.35f;

        // 칼날 자리: 랙 두 개 × 3열 × 5줄 = 30 (모듈 수와 같다)
        float half = r.Size.X * 0.5f;
        int idx = 0;
        foreach (var mod in AllModules)
        {
            int k = idx / 15, slot = idx % 15, col = slot % 3, row = slot / 3;
            idx++;
            var rack = new Rect2(r.Position.X + 6 + k * half, r.Position.Y + 6, half - 9, r.Size.Y - 12);
            float bw = (rack.Size.X - 4f) / 3f, bh = (rack.Size.Y - 4f) / 5f;
            var blade = new Rect2(rack.Position.X + 2 + col * bw, rack.Position.Y + 2 + row * bh, bw - 1.2f, bh - 1.2f);
            bool has = main && a.Has(mod);
            if (!has) { ci.DrawRect(blade, new Color("#0e1219")); continue; } // 빈 자리: 가림판
            var st = ComputerIcons.StateOf(w, mod);
            var tint = ComputerIcons.Tint(mod);
            var face = st switch
            {
                ComputerIcons.State.Suspended => new Color("#2a2f38"),
                ComputerIcons.State.Bug => tint.Lerp(Palette.Danger, 0.5f + 0.5f * Mathf.Sin(t * 21f)).Darkened(0.55f),
                _ => tint.Darkened(online ? 0.62f : 0.82f),
            };
            if (st == ComputerIcons.State.Bug && Mathf.Sin(t * 37f + idx) > 0.55f) blade.Position += new Vector2(1.2f, 0f); // 지지직 어긋남
            ci.DrawRect(blade, face);
            ci.DrawRect(blade, tint.WithAlpha(online ? 0.55f : 0.2f), false, 0.8f);
            if (close && blade.Size.X > 5f)
                ComputerIcons.Draw(ci, mod, blade.GetCenter(), Mathf.Min(blade.Size.X, blade.Size.Y) * 0.32f, st, t);
            else if (online && st == ComputerIcons.State.On)
            {
                // 모듈마다 다른 깜빡임 무늬 (빠르기 · 자리 · 개수)
                float h = Hash(f.Id, (int)mod, 501);
                int leds = 1 + (int)mod % 3;
                for (int l = 0; l < leds; l++)
                {
                    bool on = Mathf.Sin(t * (2f + 7f * h) + l * 1.9f + h * 11f) > -0.1f + 0.5f * load;
                    if (on) ci.DrawCircle(new Vector2(blade.Position.X + 1.5f + l * (blade.Size.X - 3f) / Mathf.Max(1, leds - 1 + 0.001f), blade.Position.Y + blade.Size.Y * 0.5f), 0.9f, tint.Lightened(0.3f), true, -1f, true);
                }
            }
            else if (st == ComputerIcons.State.Suspended)
            {
                var c0 = blade.GetCenter();
                ci.DrawLine(c0 + new Vector2(-1f, -1.3f), c0 + new Vector2(-1f, 1.3f), new Color("#8a93a3"), 0.8f);
                ci.DrawLine(c0 + new Vector2(1f, -1.3f), c0 + new Vector2(1f, 1.3f), new Color("#8a93a3"), 0.8f);
            }
        }

        // 부하 막대 (오른쪽 끝 세로)
        var lb = new Rect2(r.End.X - 5f, r.Position.Y + 6f, 2.5f, r.Size.Y - 12f);
        ci.DrawRect(lb, new Color("#0a0d12"));
        if (main && a.Present)
        {
            float fill = Mathf.Clamp(load, 0f, 1.2f) / 1.2f;
            var lc = load > 0.9f ? Palette.Danger : load > 0.7f ? Palette.Warning : Palette.Good;
            ci.DrawRect(new Rect2(lb.Position.X, lb.End.Y - lb.Size.Y * fill, lb.Size.X, lb.Size.Y * fill), lc.WithAlpha(0.85f));
            ci.DrawLine(new Vector2(lb.Position.X - 1f, lb.End.Y - lb.Size.Y * 0.75f), new Vector2(lb.End.X + 1f, lb.End.Y - lb.Size.Y * 0.75f), Colors.White.WithAlpha(0.4f), 0.6f); // 90% 선
        }

        // 팬 (아래 가운데): 부하만큼 빨리 · 과열이면 붉게 떤다 · 멎으면 선다
        var fc = new Vector2(r.GetCenter().X, r.End.Y - 4.5f);
        float fr = Mathf.Min(5f, r.Size.Y * 0.14f);
        ci.DrawCircle(fc, fr + 1f, new Color("#0b0f15"), true, -1f, true);
        ci.DrawArc(fc, fr + 1f, 0f, Mathf.Tau, 16, new Color("#3a4558"), 0.8f, true);
        float spin = online || rebooting ? t * (4f + 22f * Mathf.Clamp(load, 0f, 1.3f) + 10f * heat) : 0f;
        var bladeCol = heat > 0.85f ? new Color("#ff8a4a") : new Color("#7d8aa0");
        if (heat > 0.85f && online) fc += new Vector2(Mathf.Sin(t * 53f) * 0.5f, 0f);
        for (int b = 0; b < 4; b++)
        {
            float ang = spin + b * Mathf.Tau / 4f;
            var tip = fc + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * fr;
            var side = fc + new Vector2(Mathf.Cos(ang + 0.6f), Mathf.Sin(ang + 0.6f)) * fr * 0.7f;
            ci.DrawColoredPolygon(new[] { fc, tip, side }, bladeCol.WithAlpha(online ? 0.8f : 0.45f));
        }
        ci.DrawCircle(fc, 1.2f, new Color("#c8d0dc"), true, -1f, true);
        // 팬 소리: 부하가 크면 옆으로 퍼지는 작은 호
        if (online && load > 0.65f)
            for (int s = 0; s < 2; s++)
            {
                float ph = Mathf.PosMod(t * 1.6f + s * 0.5f, 1f);
                ci.DrawArc(fc, fr + 3f + 7f * ph, -0.5f, 0.5f, 6, Colors.White.WithAlpha(0.35f * (1f - ph) * load), 0.8f, true);
                ci.DrawArc(fc, fr + 3f + 7f * ph, Mathf.Pi - 0.5f, Mathf.Pi + 0.5f, 6, Colors.White.WithAlpha(0.35f * (1f - ph) * load), 0.8f, true);
            }

        // 등급 눈금 (위 가운데): 등급마다 하나 · 등급이 오르면 금빛
        if (main)
        {
            int lv = Mathf.Clamp(a.Level, 0, 5);
            for (int k = 0; k < 5; k++)
            {
                var p = new Vector2(r.GetCenter().X - 10f + k * 5f, r.Position.Y + 3.2f);
                ci.DrawRect(new Rect2(p - new Vector2(1.6f, 1f), new Vector2(3.2f, 2f)), k < lv ? (lv >= 4 ? new Color("#ffd166") : CompCyan).WithAlpha(online ? 0.9f : 0.35f) : new Color("#1a2130"));
            }
        }

        // 재부팅: 위에서 아래로 훑는 빛줄 + 진행 막대
        if (rebooting)
        {
            float span = Mathf.Max(1f, a.RebootUntil - a.RebootStarted);
            float prog = Mathf.Clamp(1f - (a.RebootUntil - w.Tick) / span, 0f, 1f);
            float sy = r.Position.Y + 6f + Mathf.PosMod(t * 1.3f, 1f) * (r.Size.Y - 12f);
            ci.DrawRect(new Rect2(r.Position.X + 5f, sy - 1.5f, r.Size.X - 12f, 3f), new Color(0.8f, 0.95f, 1f, 0.45f));
            var pb = new Rect2(r.Position.X + 8f, r.GetCenter().Y - 3f, r.Size.X - 18f, 6f);
            ci.DrawRect(pb, new Color("#05070a").WithAlpha(0.9f));
            ci.DrawRect(new Rect2(pb.Position, new Vector2(pb.Size.X * prog, pb.Size.Y)), CompCyan.WithAlpha(0.85f));
            ci.DrawRect(pb, CompCyan.WithAlpha(0.6f), false, 0.8f);
        }
        // 업데이트 버그: 랙 위를 가로지르는 붉은 잡음 줄
        if (main && a.BugUntil > w.Tick && Mathf.Sin(t * 13f) > 0.2f)
            for (int k = 0; k < 3; k++)
            {
                float y = r.Position.Y + 6f + Hash((int)(t * 9f), k, f.Id) * (r.Size.Y - 12f);
                ci.DrawLine(new Vector2(r.Position.X + 4f, y), new Vector2(r.End.X - 6f, y), Palette.Danger.WithAlpha(0.55f), 1f);
            }
        // 멎음 · 부서짐: 어둡게 덮고 붉은 X
        if (!online && !rebooting || wrecked)
        {
            ci.DrawRect(r.Grow(-3f), new Color(0f, 0f, 0f, 0.45f));
            if (main || wrecked)
            {
                var c0 = r.GetCenter();
                float s = Mathf.Min(r.Size.X, r.Size.Y) * 0.22f;
                float blink = Mathf.Sin(t * 3f) > 0f ? 0.9f : 0.4f;
                ci.DrawLine(c0 + new Vector2(-s, -s), c0 + new Vector2(s, s), Palette.Danger.WithAlpha(blink), 2f, true);
                ci.DrawLine(c0 + new Vector2(-s, s), c0 + new Vector2(s, -s), Palette.Danger.WithAlpha(blink), 2f, true);
            }
        }
        // 과열: 붉은 테 숨쉬기 + 위로 오르는 아지랑이 줄
        if (heat > 0.6f && main)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 5f);
            Gfx.RoundRect(ci, r.Grow(-1f), new Color(1f, 0.3f, 0.1f, 0f), 6, new Color(1f, 0.35f, 0.15f, 0.25f + 0.5f * pulse * Mathf.Min(1f, heat)), 2);
            for (int k = 0; k < 3; k++)
            {
                float ph = Mathf.PosMod(t * 0.9f + k * 0.33f, 1f);
                float x = r.Position.X + r.Size.X * (0.25f + 0.25f * k) + Mathf.Sin(t * 4f + k) * 2f;
                ci.DrawLine(new Vector2(x, r.Position.Y - 2f - 10f * ph), new Vector2(x + 2f, r.Position.Y - 6f - 10f * ph), new Color("#ff8a4a").WithAlpha(0.5f * (1f - ph)), 1f, true);
            }
        }
        if (main) PaintCoreBody(ci, f, r, t); // v16.20 비상 전지 · 예비 연산기 · 느리게 돎 · 절전
    }

    // ───────────────────── 조작 빛 흐름 ─────────────────────

    private readonly Dictionary<int, Vector2[]?> _dataPaths = new();
    private long _dataPathKey = -1;

    /// <summary>서버실 분기점에서 대상 방까지 데이터선을 따라가는 길 (끊긴 토막은 못 지난다 — 없으면 null). 망이 바뀔 때만 다시 찾는다.</summary>
    private Vector2[]? DataPath(Room from, Room to)
    {
        var net = _world.Net;
        int cuts = 0;
        foreach (var l in net.Links) if (l.Kind == NetKind.Data && l.Cut) cuts++;
        long key = ((long)net.Version * 7919 + cuts) * 1009 + from.Id;
        if (key != _dataPathKey) { _dataPaths.Clear(); _dataPathKey = key; }
        if (_dataPaths.TryGetValue(to.Id, out var hit)) return hit;
        var path = BuildDataPath(from, to);
        _dataPaths[to.Id] = path;
        return path;
    }

    private Vector2[]? BuildDataPath(Room from, Room to)
    {
        var net = _world.Net;
        var off = new Vector2(-0.15f * T, -0.09f * T); // 데이터선 자리 (ShipViewNet과 같게)
        if (from == to) return new[] { ToPx(from.Center), ToPx(to.Center) };
        var hub = new Dictionary<int, int>();
        var adj = new Dictionary<int, List<(NetLink l, int other)>>();
        foreach (var l in net.Links)
        {
            if (l.Kind != NetKind.Data) continue;
            if (l.Key.EndsWith(":A")) hub.TryAdd(l.Room.Id, l.NodeA);
            else if (l.Key.EndsWith(":B")) hub.TryAdd(l.Room.Id, l.NodeB);
            if (l.Cut || l.Room.Detached) continue;
            if (!adj.TryGetValue(l.NodeA, out var la)) adj[l.NodeA] = la = new();
            if (!adj.TryGetValue(l.NodeB, out var lb)) adj[l.NodeB] = lb = new();
            la.Add((l, l.NodeB));
            lb.Add((l, l.NodeA));
        }
        if (!hub.TryGetValue(from.Id, out int start) || !hub.TryGetValue(to.Id, out int goal)) return null;
        var prev = new Dictionary<int, (NetLink l, int from)>();
        var q = new Queue<int>();
        q.Enqueue(start);
        prev[start] = (null!, -1);
        while (q.Count > 0)
        {
            int n = q.Dequeue();
            if (n == goal) break;
            if (!adj.TryGetValue(n, out var list)) continue;
            foreach (var (l, o) in list)
                if (!prev.ContainsKey(o)) { prev[o] = (l, n); q.Enqueue(o); }
        }
        if (!prev.ContainsKey(goal)) return null;
        var hops = new List<(NetLink l, bool forward)>();
        for (int n = goal; n != start;)
        {
            var (l, p) = prev[n];
            hops.Add((l, l.NodeA == p));
            n = p;
        }
        hops.Reverse();
        var pts = new List<Vector2>();
        foreach (var (l, fwd) in hops)
        {
            IEnumerable<Cell> cells = fwd ? l.Cells : Enumerable.Reverse(l.Cells);
            foreach (var c in cells)
            {
                var p = CellRect(c).GetCenter() + off;
                if (pts.Count == 0 || pts[^1].DistanceSquaredTo(p) > 1f) pts.Add(p);
            }
        }
        if (pts.Count < 2) pts.Add(ToPx(to.Center));
        return pts.ToArray();
    }

    /// <summary>폴리선 위 거리 s 자리.</summary>
    private static Vector2 Along(Vector2[] pts, float[] cum, float s)
    {
        if (s <= 0f) return pts[0];
        for (int i = 1; i < pts.Length; i++)
            if (cum[i] >= s) return pts[i - 1].Lerp(pts[i], (s - cum[i - 1]) / Mathf.Max(0.001f, cum[i] - cum[i - 1]));
        return pts[^1];
    }

    private void PaintDataFlows(CanvasItem ci)
    {
        var w = _world;
        var a = w.Automation;
        if (_compBody == null || _compRoom == null) return;
        var book = a.Book;
        long window = SimTime.Minutes(4);
        var src = FurnitureRect(_compBody).GetCenter();
        int drawn = 0;
        var seen = new HashSet<int>();
        for (int i = book.Acts.Count - 1; i >= 0 && drawn < 8; i--)
        {
            var act = book.Acts[i];
            long age = w.Tick - act.Tick;
            if (age > window) break;
            if (act.RoomId < 0 || act.RoomId >= w.Ship.Rooms.Count || !seen.Add(act.RoomId * 31 + (int)act.Kind)) continue;
            var room = w.Ship.Rooms[act.RoomId];
            if (room.Detached) continue;
            drawn++;
            float ph = Mathf.Clamp((age + _main.Alpha) / window, 0f, 1f);
            var col = ActColor(act.Kind);
            var target = ToPx(room.Center);
            var path = DataPath(_compRoom, room);
            if (path == null)
            {
                // 선이 끊겨 닿지 않는다: 서버실에서 나가다 꺼지는 빛 · 대상에는 끊긴 고리
                var dir = (target - src).Normalized();
                float reach = Mathf.Min(src.DistanceTo(target) * 0.35f, T * 3f) * Mathf.Min(1f, ph * 3f);
                ci.DrawLine(src, src + dir * reach, col.WithAlpha(0.5f * (1f - ph)), 1.5f, true);
                var x = src + dir * reach;
                ci.DrawLine(x + new Vector2(-3, -3), x + new Vector2(3, 3), Palette.Danger.WithAlpha(1f - ph), 1.4f, true);
                ci.DrawLine(x + new Vector2(-3, 3), x + new Vector2(3, -3), Palette.Danger.WithAlpha(1f - ph), 1.4f, true);
                ci.DrawArc(target, 8f, 0.3f, Mathf.Tau - 0.3f, 16, new Color("#6b7280").WithAlpha(0.8f * (1f - ph)), 1.2f, true);
                continue;
            }
            var pts = new Vector2[path.Length + 2];
            pts[0] = src;
            Array.Copy(path, 0, pts, 1, path.Length);
            pts[^1] = target;
            var cum = new float[pts.Length];
            for (int k = 1; k < pts.Length; k++) cum[k] = cum[k - 1] + pts[k - 1].DistanceTo(pts[k]);
            float total = Mathf.Max(1f, cum[^1]);
            float travel = Mathf.Clamp(ph / 0.45f, 0f, 1f); // 앞 45%는 흐르고, 나머지는 대상에서 깜빡인다
            float head = travel * travel * (3f - 2f * travel) * total;
            // 지나온 선이 잠깐 빛난다 (흐르는 동안)
            if (travel < 1f)
            {
                float trail = Mathf.Min(total, T * 4f);
                int n = 10;
                for (int s = 0; s < n; s++)
                {
                    float s0 = head - trail * (s + 1) / n, s1 = head - trail * s / n;
                    if (s1 <= 0f) break;
                    ci.DrawLine(Along(pts, cum, Mathf.Max(0f, s0)), Along(pts, cum, s1), col.WithAlpha(0.75f * (1f - s / (float)n)), 2.4f - 0.15f * s, true);
                }
                var hp = Along(pts, cum, head);
                ci.DrawCircle(hp, 4.5f, col.WithAlpha(0.25f), true, -1f, true);
                ci.DrawCircle(hp, 2.2f, Colors.White.WithAlpha(0.95f), true, -1f, true);
                // 서버실에서 나가는 순간: 랙에 짧은 섬광
                if (travel < 0.15f) ci.DrawCircle(src, 6f + 20f * travel, col.WithAlpha(0.35f * (1f - travel / 0.15f)), false, 1.4f, true);
            }
            else
            {
                // 닿았다: 대상 방에 조치 그림이 깜빡이고 고리가 퍼진다
                float k2 = (ph - 0.45f) / 0.55f;
                bool on = Mathf.Sin(_time * 10f) > -0.3f || k2 > 0.7f;
                float rr = 8f + 22f * Mathf.PosMod(k2 * 2f, 1f);
                ci.DrawArc(target, rr, 0f, Mathf.Tau, 28, col.WithAlpha(0.6f * (1f - Mathf.PosMod(k2 * 2f, 1f))), 1.5f, true);
                ci.DrawCircle(target, 10f, new Color("#05070a").WithAlpha(0.75f * (1f - k2 * 0.6f)), true, -1f, true);
                ci.DrawArc(target, 10f, 0f, Mathf.Tau, 24, col.WithAlpha(0.9f * (1f - k2 * 0.5f)), 1.4f, true);
                if (on) ComputerIcons.Act(ci, act.Kind, target, 1.35f, col.WithAlpha(1f - k2 * 0.5f), _time);
                if (Zoom > 1.0f && k2 < 0.8f)
                {
                    string txt = act.Act.Length > 18 ? act.Act[..18] + "…" : act.Act;
                    Gfx.TextCentered(ci, Fonts.Bold, target + new Vector2(0, 19f), txt, 9, col.WithAlpha(1f - k2));
                }
            }
        }
    }

    // ───────────────────── 선내 방송 ─────────────────────

    private readonly Dictionary<int, Vector2> _speakerPx = new();
    private int _speakerKey = -1;

    /// <summary>방 스피커 자리: 위쪽 벽에 붙은 칸 가운데 (방 가운데에 가까운 것).</summary>
    private Vector2? SpeakerPx(Room r)
    {
        var ship = _world.Ship;
        int key = ship.Rooms.Count * 131 + ship.Furniture.Count + _world.Net.Version * 7;
        if (key != _speakerKey) { _speakerPx.Clear(); _speakerKey = key; }
        if (_speakerPx.TryGetValue(r.Id, out var p)) return p;
        Cell? best = null;
        float bd = float.MaxValue;
        foreach (var c in r.Cells)
        {
            if (!ship.IsOpenFloor(c) || ship.RoomAt(c + new Cell(0, -1)) == r) continue;
            float d = MathF.Abs(c.X + 0.5f - r.Center.X) + c.Y * 0.01f;
            if (d < bd) { bd = d; best = c; }
        }
        if (best is not Cell bc) return null;
        p = CellRect(bc).Position + new Vector2(T * 0.5f, 4f);
        _speakerPx[r.Id] = p;
        return p;
    }

    private Broadcast? LatestBroadcast(out float ph)
    {
        ph = 1f;
        var rec = _world.Automation.Speak.Recent;
        if (rec.Count == 0) return null;
        var b = rec[^1];
        long window = SimTime.Minutes(7);
        long age = _world.Tick - b.Tick;
        if (age > window) return null;
        ph = Mathf.Clamp((age + _main.Alpha) / window, 0f, 1f);
        return b;
    }

    /// <summary>벽 스피커 (모든 방): 평소엔 작은 나팔과 초록 점, 전기·데이터가 없으면 꺼진 점, 고장이면 금 간 나팔 · 늘어진 전선 · 불꽃.</summary>
    private void PaintSpeakers(CanvasItem ci)
    {
        var w = _world;
        var pa = w.Automation.Speak;
        var b = LatestBroadcast(out float ph);
        bool far = Zoom < 0.55f;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r.Cells.Count < 2 || SpeakerPx(r) is not Vector2 p) continue;
            bool broken = pa.SpeakerBroken(r);
            bool works = pa.SpeakerWorks(r);
            bool live = b != null && b.Rooms.Contains(r.Id);
            bool silent = b != null && b.Silent.Contains(r.Id);
            if (far && !live && !broken) continue;
            float shake = live ? Mathf.Sin(_time * 40f) * 0.6f * (1f - ph) : 0f;
            var body = new Color(broken ? "#3a2a2a" : "#2a3342");
            // 받침 + 나팔 (아래로 벌어진 사다리꼴)
            ci.DrawRect(new Rect2(p + new Vector2(-3.5f, -3f), new Vector2(7f, 2.5f)), body.Lightened(0.15f));
            var horn = new[] { p + new Vector2(-2.2f + shake, -0.6f), p + new Vector2(2.2f + shake, -0.6f), p + new Vector2(4.2f + shake, 4.2f), p + new Vector2(-4.2f + shake, 4.2f) };
            ci.DrawColoredPolygon(horn, body);
            ci.DrawPolyline(new[] { horn[0], horn[1], horn[2], horn[3], horn[0] }, new Color("#56637a"), 0.7f, true);
            // 그릴 점
            for (int k = -1; k <= 1; k++) ci.DrawCircle(p + new Vector2(k * 1.8f + shake, 2.6f), 0.5f, new Color("#0b0f15"), true, -1f, true);
            // 상태 점
            var led = broken ? Palette.Danger : works ? (live ? CompViolet : Palette.Good) : new Color("#3b4250");
            if (!broken || Mathf.Sin(_time * 6f + r.Id) > 0f) ci.DrawCircle(p + new Vector2(3.6f, -1.8f), 0.9f, led, true, -1f, true);
            if (broken)
            {
                // 금 · 늘어진 전선 · 가끔 튀는 불꽃
                ci.DrawPolyline(new[] { p + new Vector2(-1.5f, 0f), p + new Vector2(0.3f, 1.6f), p + new Vector2(-0.6f, 2.8f), p + new Vector2(1.2f, 4.1f) }, new Color("#ffb4a0"), 0.7f, true);
                float sway = Mathf.Sin(_time * 1.7f + r.Id) * 1.5f;
                ci.DrawPolyline(new[] { p + new Vector2(-3.5f, -0.8f), p + new Vector2(-4.5f + sway * 0.5f, 3f), p + new Vector2(-4f + sway, 6.5f) }, new Color("#c9a14a"), 0.8f, true);
                if (Mathf.PosMod(_time * 1.3f + r.Id * 0.31f, 1f) < 0.12f)
                    for (int k = 0; k < 3; k++)
                    {
                        float ang = (r.Id * 1.3f + k * 2.1f + _time * 11f) % Mathf.Tau;
                        var sp = p + new Vector2(-4f + sway, 6.5f);
                        ci.DrawLine(sp, sp + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 3f, new Color(1f, 0.9f, 0.5f, 0.9f), 0.8f, true);
                    }
            }
            // 울릴 때: 아래로 퍼지는 소리 고리
            if (live && ph < 0.85f)
                for (int k = 0; k < 3; k++)
                {
                    float q = Mathf.PosMod(_time * 1.4f + k / 3f, 1f);
                    ci.DrawArc(p + new Vector2(0, 3f), 5f + q * T * 1.2f, 0.35f, Mathf.Pi - 0.35f, 12, CompViolet.WithAlpha(0.55f * (1f - q) * (1f - ph)), 1.1f, true);
                }
            else if (silent && ph < 0.85f)
            {
                // 못 울린 방: 끊긴 고리 + 빗금
                ci.DrawArc(p + new Vector2(0, 3f), 9f, 0.6f, 1.3f, 5, new Color("#6b7280").WithAlpha(0.6f * (1f - ph)), 1f, true);
                ci.DrawArc(p + new Vector2(0, 3f), 9f, 1.85f, 2.55f, 5, new Color("#6b7280").WithAlpha(0.6f * (1f - ph)), 1f, true);
                ci.DrawLine(p + new Vector2(-6f, 2f), p + new Vector2(6f, 12f), Palette.Danger.WithAlpha(0.6f * (1f - ph)), 1f, true);
            }
        }
    }

    /// <summary>방송 말풍선 (사람이 있는 울린 방) · 못 울린 방은 "…" · 들은 사람 머리 위 소리 표시 · 못 들은 사람은 "?".</summary>
    private void PaintBroadcastBubbles(CanvasItem ci)
    {
        var w = _world;
        var b = LatestBroadcast(out float ph);
        if (b == null) return;
        float alpha = ph < 0.8f ? 1f : (1f - ph) / 0.2f;
        bool talk = Zoom > 0.7f;
        string text = b.Text.Length > 26 ? b.Text[..26] + "…" : b.Text;
        var withPeople = new HashSet<int>();
        foreach (var id in b.HeardBy) if (w.Crew.FirstOrDefault(c => c.Id == id) is { Room: Room r0 }) withPeople.Add(r0.Id);
        foreach (var r in w.Ship.LiveRooms)
        {
            if (SpeakerPx(r) is not Vector2 p) continue;
            if (b.Rooms.Contains(r.Id) && withPeople.Contains(r.Id) && talk)
            {
                float wdt = Gfx.Width(Fonts.Bold, text, 10) + 12f;
                var box = new Rect2(p + new Vector2(-wdt / 2f, 9f), new Vector2(wdt, 15f));
                var border = b.Priority >= 2 ? Palette.Danger : CompViolet;
                Gfx.RoundRect(ci, box, new Color(0.06f, 0.05f, 0.1f, 0.88f * alpha), 6, border.WithAlpha(0.85f * alpha), 1);
                ci.DrawColoredPolygon(new[] { p + new Vector2(-3f, 9.5f), p + new Vector2(3f, 9.5f), p + new Vector2(0f, 5.5f) }, border.WithAlpha(0.85f * alpha));
                Gfx.Text(ci, Fonts.Bold, box.Position + new Vector2(6f, 11f), text, 10, new Color(0.95f, 0.92f, 1f, alpha));
            }
            else if (b.Silent.Contains(r.Id) && talk && r.Type != RoomType.Corridor)
            {
                var box = new Rect2(p + new Vector2(-9f, 9f), new Vector2(18f, 11f));
                Gfx.RoundRect(ci, box, new Color(0.08f, 0.08f, 0.1f, 0.7f * alpha), 5, new Color("#6b7280").WithAlpha(0.7f * alpha), 1);
                for (int k = -1; k <= 1; k++) ci.DrawCircle(box.GetCenter() + new Vector2(k * 4f, 0f), 1f, new Color("#9aa3b5").WithAlpha(alpha), true, -1f, true);
            }
        }
        // 들은 사람 · 못 들은 사람
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Outside) continue;
            bool heard = b.HeardBy.Contains(c.Id), missed = !heard && b.Missed.Contains(c.Id);
            if (!heard && !missed) continue;
            var head = CrewPx(c) + new Vector2(CrewRadius * 0.9f, -CrewRadius - 4f);
            if (heard)
            {
                ci.DrawColoredPolygon(new[] { head + new Vector2(-3f, -1.5f), head + new Vector2(-1f, -1.5f), head + new Vector2(1.5f, -3.5f), head + new Vector2(1.5f, 3.5f), head + new Vector2(-1f, 1.5f), head + new Vector2(-3f, 1.5f) }, CompViolet.WithAlpha(alpha));
                for (int k = 1; k <= 2; k++) ci.DrawArc(head + new Vector2(2f, 0f), 2f + 2.2f * k, -0.8f, 0.8f, 6, CompViolet.WithAlpha(alpha * (1.1f - 0.35f * k)), 1f, true);
            }
            else
            {
                ci.DrawCircle(head, 5f, new Color(0.1f, 0.1f, 0.12f, 0.75f * alpha), true, -1f, true);
                Gfx.TextCentered(ci, Fonts.Bold, head, "?", 10, new Color("#9aa3b5").WithAlpha(alpha));
            }
        }
    }

    // ───────────────────── 방 콘솔 ─────────────────────

    /// <summary>방 콘솔 화면: 컴퓨터가 그 방에 대해 아는 일을 띄운다 (함교 콘솔은 배 전체).</summary>
    private void PaintConsoleScreens(CanvasItem ci)
    {
        var w = _world;
        var a = w.Automation;
        if (!a.Present) return;
        bool close = Zoom > 0.9f;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.Console))
        {
            var room = f.Room;
            var r = FurnitureRect(f);
            var b0 = r.Grow(-3f);
            var scr = new Rect2(b0.Position.X + 4, b0.Position.Y + 4, b0.Size.X - 8, b0.Size.Y * 0.48f);
            float t = _time + f.Id * 0.41f;
            if (!room.Powered) continue;
            if (!room.DataLinked && !ComputerV15.Relay(w))
            {
                // 링크 끊김: 회색 화면 + 끊긴 사슬
                ci.DrawRect(scr, new Color("#12161c"));
                var c0 = scr.GetCenter();
                ci.DrawArc(c0 + new Vector2(-3.5f, 0), 2.5f, 0.6f, Mathf.Tau - 0.6f, 10, new Color("#6b7280"), 1f, true);
                ci.DrawArc(c0 + new Vector2(3.5f, 0), 2.5f, Mathf.Pi + 0.6f, Mathf.Pi + Mathf.Tau - 0.6f, 10, new Color("#6b7280"), 1f, true);
                if (Mathf.Sin(t * 2f) > 0f) ci.DrawLine(c0 + new Vector2(-1, -3), c0 + new Vector2(1, 3), Palette.Danger, 1f);
                continue;
            }
            if (a.Rebooting)
            {
                ci.DrawRect(scr, new Color("#06121a"));
                float span = Mathf.Max(1f, a.RebootUntil - a.RebootStarted);
                float prog = Mathf.Clamp(1f - (a.RebootUntil - w.Tick) / span, 0f, 1f);
                ci.DrawRect(new Rect2(scr.Position + new Vector2(2, scr.Size.Y - 4), new Vector2((scr.Size.X - 4) * prog, 2)), CompCyan);
                ci.DrawArc(scr.GetCenter() + new Vector2(0, -1), 3f, t * 6f, t * 6f + 4.5f, 10, CompCyan, 1f, true);
                continue;
            }
            if (!a.MainOnline)
            {
                // 멎음: 잡음
                for (int k = 0; k < 6; k++)
                {
                    float y = scr.Position.Y + Hash((int)(t * 12f), k, f.Id) * scr.Size.Y;
                    ci.DrawLine(new Vector2(scr.Position.X, y), new Vector2(scr.End.X, y), new Color(0.7f, 0.7f, 0.75f, 0.35f), 1f);
                }
                continue;
            }
            // 이 방에 대해 컴퓨터가 아는 것 (가장 급한 하나)
            var bel = a.Belief.Of(room);
            var ask = a.Asks.Open.FirstOrDefault(p => p.RoomId == room.Id);
            var check = a.Checks.FirstOrDefault(k => k.RoomId == room.Id);
            bool bridge = room.Type == RoomType.Bridge;
            int glyph; string label; Color col;
            if (room.EvacuateBy >= 0) { glyph = 1; label = $"대피 {Mathf.Max(0f, (room.EvacuateBy - w.Tick) * 3600f / SimTime.TicksPerHour):0}초"; col = Palette.Danger; }
            else if (bel.Fire) { glyph = 2; label = "화재"; col = Palette.Danger; }
            else if (ask != null) { glyph = 3; label = "승인?"; col = CompAmber; }
            else if (check != null) { glyph = 4; label = "확인"; col = CompAmber; }
            else if (bridge && a.Asks.Open.Any()) { glyph = 3; label = $"제안 {a.Asks.Open.Count()}"; col = CompAmber; }
            else if (bridge && a.Load > 0.9f) { glyph = 5; label = "과부하"; col = Palette.Warning; }
            else if (a.BugUntil > w.Tick) { glyph = 6; label = "오류"; col = Palette.Danger; }
            else if (bel.Trust < 0.8f) { glyph = 7; label = "감지기?"; col = Palette.Warning; }
            else continue; // 평소 화면 (원래 그림)
            float blink = glyph <= 2 ? (Mathf.Sin(t * 9f) > 0f ? 1f : 0.45f) : 0.75f + 0.25f * Mathf.Sin(t * 3f);
            ci.DrawRect(scr, col.Darkened(0.75f));
            ci.DrawRect(scr, col.WithAlpha(0.8f * blink), false, 1f);
            var g = new Vector2(scr.Position.X + Mathf.Min(7f, scr.Size.X * 0.25f), scr.GetCenter().Y);
            var gc = col.WithAlpha(blink);
            switch (glyph)
            {
                case 1: // 대피: 달리는 화살표
                    ci.DrawColoredPolygon(new[] { g + new Vector2(-3, -2), g + new Vector2(1, -2), g + new Vector2(1, -4), g + new Vector2(4, 0), g + new Vector2(1, 4), g + new Vector2(1, 2), g + new Vector2(-3, 2) }, gc);
                    break;
                case 2: // 화재: 불꽃
                    ci.DrawColoredPolygon(new[] { g + new Vector2(0, -4.5f), g + new Vector2(3, 0.5f), g + new Vector2(1.6f, 3.5f), g + new Vector2(-1.6f, 3.5f), g + new Vector2(-3, 0.5f) }, gc);
                    break;
                case 3: // 승인: 물음표 카드
                    ci.DrawRect(new Rect2(g - new Vector2(3.5f, 4f), new Vector2(7, 8)), gc, false, 1f);
                    if (close) Gfx.TextCentered(ci, Fonts.Bold, g, "?", 8, gc);
                    break;
                case 4: // 확인: 돋보기
                    ci.DrawArc(g + new Vector2(-0.8f, -0.8f), 2.6f, 0f, Mathf.Tau, 10, gc, 1.1f, true);
                    ci.DrawLine(g + new Vector2(1f, 1f), g + new Vector2(3.5f, 3.5f), gc, 1.4f);
                    break;
                case 5: // 과부하: 꽉 찬 막대
                    for (int k = 0; k < 3; k++) ci.DrawRect(new Rect2(g + new Vector2(-3.5f + k * 2.6f, 3.5f - (k + 2) * 1.6f), new Vector2(1.8f, (k + 2) * 1.6f)), gc);
                    break;
                case 6: // 오류: 지지직 블록
                    for (int k = 0; k < 4; k++) ci.DrawRect(new Rect2(g + new Vector2(-4f + Hash((int)(t * 8f), k, 7) * 6f, -4f + k * 2f), new Vector2(3f, 1.5f)), gc);
                    break;
                default: // 감지기 의심: 눈 + 물음
                    ci.DrawArc(g, 3f, 0.3f, Mathf.Pi - 0.3f, 8, gc, 1f, true);
                    ci.DrawArc(g, 3f, Mathf.Pi + 0.3f, Mathf.Tau - 0.3f, 8, gc, 1f, true);
                    ci.DrawCircle(g, 1f, gc, true, -1f, true);
                    break;
            }
            if (close && scr.Size.X > 26f)
                Gfx.Text(ci, Fonts.Bold, new Vector2(g.X + 6f, scr.GetCenter().Y + 3.5f), label, 8, Colors.White.WithAlpha(0.9f * blink));
            // 흐르는 경고 띠
            float sx = Mathf.PosMod(t * 18f, scr.Size.X);
            ci.DrawLine(new Vector2(scr.Position.X + sx, scr.End.Y - 1.5f), new Vector2(Mathf.Min(scr.End.X, scr.Position.X + sx + 6f), scr.End.Y - 1.5f), col, 1.2f);
        }
    }

    // ───────────────────── 제안 · 확인 (방 위) ─────────────────────

    /// <summary>승인을 기다리는 제안: 방 가운데 호박색 카드 + 줄어드는 기한 고리 · 사람 확인 요청: 확인하러 가는 사람에서 방까지 점선 + 돋보기.</summary>
    private void PaintRoomAsks(CanvasItem ci)
    {
        var w = _world;
        var a = w.Automation;
        foreach (var p in a.Asks.Open)
        {
            if (p.RoomId < 0 || p.RoomId >= w.Ship.Rooms.Count) continue;
            var room = w.Ship.Rooms[p.RoomId];
            if (room.Detached) continue;
            var c = ToPx(room.Center) + new Vector2(0, -T * 0.6f);
            float left = Mathf.Clamp((p.Deadline - w.Tick) / (float)Mathf.Max(1, p.Deadline - p.Tick), 0f, 1f);
            float pulse = 0.6f + 0.4f * Mathf.Sin(_time * 4f);
            ci.DrawCircle(c, 13f, new Color(0.08f, 0.06f, 0.02f, 0.85f), true, -1f, true);
            ci.DrawArc(c, 13f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * left, 32, CompAmber.WithAlpha(pulse), 2.2f, true);
            var card = new Rect2(c - new Vector2(6f, 7.5f), new Vector2(12f, 15f));
            ci.DrawRect(card, CompAmber.WithAlpha(0.25f));
            ci.DrawRect(card, CompAmber, false, 1.2f);
            if (p.Kind == "vacuum")
                for (int k = 0; k < 3; k++) // 바깥으로 빠지는 화살표
                {
                    float q = Mathf.PosMod(_time * 1.5f + k / 3f, 1f);
                    ci.DrawLine(c + new Vector2(-3f + k * 3f, 4f - 8f * q), c + new Vector2(-3f + k * 3f, 1f - 8f * q), CompAmber.WithAlpha(1f - q), 1f);
                }
            else Gfx.TextCentered(ci, Fonts.Bold, c, "?", 11, CompAmber);
            if (Zoom > 0.8f)
            {
                string txt = p.Title.Length > 16 ? p.Title[..16] + "…" : p.Title;
                Gfx.TextCentered(ci, Fonts.Bold, c + new Vector2(0, 22f), txt + " — 승인?", 9, CompAmber);
            }
        }
        foreach (var k in a.Checks)
        {
            if (k.RoomId < 0 || k.RoomId >= w.Ship.Rooms.Count) continue;
            var room = w.Ship.Rooms[k.RoomId];
            if (room.Detached) continue;
            var c = ToPx(room.Center);
            float bob = Mathf.Sin(_time * 3f) * 2f;
            var g = c + new Vector2(T * 0.7f, -T * 0.4f + bob);
            ci.DrawCircle(g, 8f, new Color(0.08f, 0.06f, 0.02f, 0.75f), true, -1f, true);
            ci.DrawArc(g + new Vector2(-1.2f, -1.2f), 3.8f, 0f, Mathf.Tau, 14, CompAmber, 1.4f, true);
            ci.DrawLine(g + new Vector2(1.5f, 1.5f), g + new Vector2(5f, 5f), CompAmber, 2f, true);
            if (k.Seen) ci.DrawPolyline(new[] { g + new Vector2(-3f, 6f), g + new Vector2(-1f, 8f), g + new Vector2(3f, 4f) }, Palette.Good, 1.4f, true);
            if (w.Crew.FirstOrDefault(x => x.Id == k.CheckerId) is CrewMember who && !who.Dead && !k.Seen)
                ci.DrawDashedLine(CrewPx(who), g, CompAmber.WithAlpha(0.55f), 1.2f, 4f);
        }
    }

    // ───────────────────── 홀로그램 정보판 ─────────────────────

    /// <summary>함교 홀로그램 옆에 뜬 정보판 넷: 부하 · 온도 / 30일 물 곡선 / 제안 · 오늘의 결정 / 하루 보고 숫자. 컴퓨터가 멎으면 꺼진다.</summary>
    private void PaintHoloBoard(CanvasItem ci)
    {
        if (HoloSpot() is not Cell hc) return;
        var w = _world;
        var a = w.Automation;
        var bridge = w.Ship.RoomAt(hc);
        if (bridge == null || !bridge.Powered || !a.Present) return;
        bool online = a.MainOnline;
        if (!online && Mathf.Sin(_time * 17f) < 0.4f) return; // 지지직
        var origin = new Vector2((hc.X + 2) * T + 6f, hc.Y * T - 4f);
        float flick = online ? 0.85f + 0.15f * Mathf.Sin(_time * 7f) : 0.4f;
        bool close = Zoom > 0.85f;
        var cyan = CompCyan.WithAlpha(flick);
        // 패드에서 판으로 빛줄
        ci.DrawLine(new Vector2((hc.X + 1) * T, (hc.Y + 1) * T), origin + new Vector2(0, 10), cyan.WithAlpha(0.25f), 1f, true);
        Rect2 Panel(int i) => new(origin + new Vector2((i % 2) * 52f, (i / 2) * 30f), new Vector2(48f, 26f));
        void Frame(Rect2 p)
        {
            ci.DrawRect(p, new Color(0.2f, 0.7f, 1f, 0.08f * flick));
            ci.DrawRect(p, cyan.WithAlpha(0.5f * flick), false, 0.8f);
            for (float y = p.Position.Y + 2f; y < p.End.Y; y += 3f) ci.DrawLine(new Vector2(p.Position.X, y), new Vector2(p.End.X, y), new Color(0.4f, 0.85f, 1f, 0.04f), 1f); // 주사선
        }
        // 1) 부하 호 + 온도 막대
        var p0 = Panel(0);
        Frame(p0);
        var gc = p0.Position + new Vector2(14f, 15f);
        ci.DrawArc(gc, 9f, Mathf.Pi * 0.8f, Mathf.Pi * 2.2f, 16, new Color(0.4f, 0.85f, 1f, 0.25f), 2f, true);
        float load = Mathf.Clamp(a.Load, 0f, 1.2f) / 1.2f;
        var lc = a.Load > 0.9f ? Palette.Danger : a.Load > 0.7f ? Palette.Warning : Palette.Good;
        ci.DrawArc(gc, 9f, Mathf.Pi * 0.8f, Mathf.Pi * (0.8f + 1.4f * load), 16, lc.WithAlpha(flick), 2.2f, true);
        float temp = a.ComputerBody?.Room.Air.Temperature ?? 20f;
        float th = Mathf.Clamp((temp - 15f) / 30f, 0f, 1f);
        var tb = new Rect2(p0.Position + new Vector2(32f, 4f), new Vector2(4f, 18f));
        ci.DrawRect(tb, new Color(0.4f, 0.85f, 1f, 0.15f));
        ci.DrawRect(new Rect2(tb.Position.X, tb.End.Y - tb.Size.Y * th, tb.Size.X, tb.Size.Y * th), (temp > AutomationSystem.OverheatC - 3f ? Palette.Danger : CompCyan).WithAlpha(flick));
        if (close) Gfx.Text(ci, Fonts.Bold, p0.Position + new Vector2(38f, 24f), $"{temp:0}", 7, cyan);
        // 2) 30일 물 곡선
        var p1 = Panel(1);
        Frame(p1);
        var fc = a.Apps.WaterForecast;
        float max = 1f;
        foreach (var v in fc) max = Mathf.Max(max, v);
        if (fc[0] > 0f)
        {
            var pts = new Vector2[fc.Length];
            for (int i = 0; i < fc.Length; i++) pts[i] = p1.Position + new Vector2(3f + (p1.Size.X - 6f) * i / (fc.Length - 1f), p1.End.Y - 3f - (p1.Size.Y - 6f) * fc[i] / max);
            ci.DrawPolyline(pts, (a.Apps.WaterEmptyDay >= 0 ? Palette.Warning : new Color("#4f9fdc")).WithAlpha(flick), 1.2f, true);
            if (a.Apps.WaterEmptyDay >= 0) ci.DrawCircle(pts[Mathf.Clamp(a.Apps.WaterEmptyDay, 0, fc.Length - 1)], 2f, Palette.Danger, true, -1f, true);
        }
        else
        {
            // 물 관리 모듈이 없다: 물방울 윤곽만
            var d = p1.GetCenter();
            ci.DrawArc(d + new Vector2(0, 2), 5f, 0f, Mathf.Pi, 10, cyan.WithAlpha(0.3f), 1f, true);
            ci.DrawLine(d + new Vector2(-5, 2), d + new Vector2(0, -6), cyan.WithAlpha(0.3f), 1f);
            ci.DrawLine(d + new Vector2(5, 2), d + new Vector2(0, -6), cyan.WithAlpha(0.3f), 1f);
        }
        // 3) 제안 · 오늘의 결정: 카드 더미 (기다림 호박 · 받음 초록 · 거절 붉음)
        var p2 = Panel(2);
        Frame(p2);
        int day = SimTime.Day(w.Tick);
        int k2 = 0;
        foreach (var pr in a.Asks.All.Where(x => SimTime.Day(x.Tick) == day).Reverse().Take(6))
        {
            var cc = pr.State == ProposalState.Pending ? CompAmber : pr.Accepted ? Palette.Good : Palette.Danger;
            var cr = new Rect2(p2.Position + new Vector2(4f + k2 * 7f, 5f + (pr.State == ProposalState.Pending ? Mathf.Sin(_time * 5f) * 1.5f : 0f)), new Vector2(6f, 9f));
            ci.DrawRect(cr, cc.WithAlpha(0.35f * flick));
            ci.DrawRect(cr, cc.WithAlpha(flick), false, 0.8f);
            k2++;
        }
        if (k2 == 0) ci.DrawRect(new Rect2(p2.Position + new Vector2(4f, 5f), new Vector2(6f, 9f)), cyan.WithAlpha(0.2f), false, 0.8f);
        if (close) Gfx.Text(ci, Fonts.Bold, p2.Position + new Vector2(4f, 23f), $"맞음 {a.Book.RightToday} · 틀림 {a.Book.WrongToday}", 7, cyan);
        // 4) 하루 보고: 아낀 전력 막대 · 물 막대 · 미룬 고장 점
        var p3 = Panel(3);
        Frame(p3);
        var td = a.Book.Today;
        float kb = Mathf.Clamp(td.Kwh / 40f, 0f, 1f), wb = Mathf.Clamp(td.WaterL / 20f, 0f, 1f);
        ci.DrawRect(new Rect2(p3.Position + new Vector2(4f, 5f), new Vector2(40f * kb, 4f)), new Color("#e8b84a").WithAlpha(flick));
        ci.DrawRect(new Rect2(p3.Position + new Vector2(4f, 11f), new Vector2(40f * wb, 4f)), new Color("#4f9fdc").WithAlpha(flick));
        for (int i = 0; i < Mathf.Min(8, td.Deferred); i++) ci.DrawCircle(p3.Position + new Vector2(6f + i * 5f, 20f), 1.5f, Palette.Good.WithAlpha(flick), true, -1f, true);
        if (close) Gfx.Text(ci, Fonts.Bold, p3.Position + new Vector2(4f, p3.Size.Y + 8f), $"{a.Voice.Call} · {td.Kwh:0}kWh · {td.WaterL:0}L", 7, cyan);
    }

    // ───────────────────── 컴퓨터가 보는 배 ─────────────────────

    /// <summary>실제 위에 어둠을 덮고 컴퓨터가 믿는 배를 선 그림으로 — 사람 수 · 불 · 기압 · 산소 · 낡은 값 · 감지기 믿음.</summary>
    private void PaintBeliefOverlay(CanvasItem ci, bool primary)
    {
        var w = _world;
        var a = w.Automation;
        var bm = a.Belief;
        bool close = Zoom > 0.75f;
        float veil = primary ? 0.8f : 0.45f;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r.Cells.Count == 0) continue;
            var b = bm.Of(r);
            bool reading = bm.Reading(r);
            float age = b.Updated < 0 ? 999f : (w.Tick - b.Updated) / (float)SimTime.Minutes(1);
            bool stale = !reading || age > 3f;
            var tone = stale ? new Color("#56607a") : b.Fire ? Palette.Danger : b.Pressure < 60f ? Palette.Warning : CompCyan;
            foreach (var c in r.Cells)
            {
                var cr = CellRect(c);
                ci.DrawRect(cr, new Color(0.01f, 0.04f, 0.07f, veil));
                // 컴퓨터 화면 무늬: 칸 점 격자
                if (close) ci.DrawCircle(cr.Position + new Vector2(2f, 2f), 0.7f, tone.WithAlpha(0.35f), true, -1f, true);
                if (stale && (c.X + c.Y) % 2 == 0) ci.DrawLine(cr.Position, cr.End, tone.WithAlpha(0.22f), 1f); // 낡은 값: 빗금
            }
            foreach (var (p0, p1) in Outline(r)) ci.DrawLine(p0, p1, tone.WithAlpha(0.85f), 1.4f);
            // 방마다 위에서 아래로 훑는 주사선
            var top = r.Cells.Min(c => c.Y) * T;
            var bottom = (r.Cells.Max(c => c.Y) + 1) * T;
            float sy = top + Mathf.PosMod(_time * 0.35f + r.Id * 0.17f, 1f) * (bottom - top);
            var row = r.Cells.Where(c => c.Y == Mathf.FloorToInt(sy / T)).ToList();
            if (row.Count > 0 && !stale) ci.DrawLine(new Vector2(row.Min(c => c.X) * T, sy), new Vector2((row.Max(c => c.X) + 1) * T, sy), tone.WithAlpha(0.3f), 1f);
            var center = ToPx(r.Center);
            // 믿는 사람: 선 그림 사람 (여섯까지, 넘치면 +n)
            int n = Math.Min(6, b.People);
            for (int i = 0; i < n; i++)
            {
                var p = center + new Vector2((i - (n - 1) / 2f) * 9f, -4f);
                ci.DrawArc(p + new Vector2(0, -4f), 2.2f, 0f, Mathf.Tau, 10, CompCyan, 1.1f, true);
                ci.DrawLine(p + new Vector2(0, -1.8f), p + new Vector2(0, 3.5f), CompCyan, 1.1f);
                ci.DrawLine(p + new Vector2(-2.5f, 0.5f), p + new Vector2(2.5f, 0.5f), CompCyan, 1f);
                ci.DrawLine(p + new Vector2(0, 3.5f), p + new Vector2(-2f, 7f), CompCyan, 1f);
                ci.DrawLine(p + new Vector2(0, 3.5f), p + new Vector2(2f, 7f), CompCyan, 1f);
            }
            if (b.People > 6 && close) Gfx.Text(ci, Fonts.Bold, center + new Vector2(30f, 0f), $"+{b.People - 6}", 9, CompCyan);
            // 믿는 불: 선으로 그린 불꽃
            if (b.Fire)
            {
                var fp = center + new Vector2(0, -T * 0.55f);
                float fl = Mathf.Sin(_time * 8f) * 1.2f;
                ci.DrawPolyline(new[] { fp + new Vector2(0, -7f - fl), fp + new Vector2(4.5f, 0f), fp + new Vector2(3f, 5f), fp + new Vector2(-3f, 5f), fp + new Vector2(-4.5f, 0f), fp + new Vector2(0, -7f - fl) }, Palette.Danger, 1.4f, true);
            }
            if (close)
            {
                string line = stale
                    ? (!reading ? "값 없음" : $"{age:0}분 전 값")
                    : $"{b.People}명 · {b.Pressure:0}kPa · {b.O2:0}%";
                Gfx.TextCentered(ci, Fonts.Bold, center + new Vector2(0, 14f), line, 8, tone.WithAlpha(0.95f));
                if (b.Trust < 1f) Gfx.TextCentered(ci, Fonts.Body, center + new Vector2(0, 24f), $"감지기 믿음 {b.Trust * 100:0}%", 7, Palette.Warning);
            }
            if (b.People == 0 && !stale && !b.Fire && r.Type != RoomType.Corridor && close)
                Gfx.TextCentered(ci, Fonts.Body, center + new Vector2(0, -2f), "비었다", 8, tone.WithAlpha(0.6f));
            if (stale && Mathf.Sin(_time * 2f + r.Id) > 0f) Gfx.TextCentered(ci, Fonts.Bold, center + new Vector2(0, -2f), "?", 12, tone);
        }
        // 컴퓨터의 눈: 서버 랙 위에 열린 눈 (멎으면 감긴다)
        if (_compBody != null)
        {
            var e = FurnitureRect(_compBody).GetCenter() + new Vector2(0, -T * 0.9f);
            bool open = a.MainOnline;
            ci.DrawArc(e, 8f, Mathf.Pi + 0.5f, Mathf.Tau - 0.5f, 12, CompCyan, 1.5f, true);
            ci.DrawArc(e, 8f, 0.5f, Mathf.Pi - 0.5f, 12, CompCyan, open ? 1.5f : 0.6f, true);
            if (open) ci.DrawCircle(e + new Vector2(Mathf.Sin(_time * 0.7f) * 2f, 0f), 2.6f, CompCyan, true, -1f, true);
            else ci.DrawLine(e + new Vector2(-6f, 0f), e + new Vector2(6f, 0f), CompCyan, 1.5f);
        }
    }
}
