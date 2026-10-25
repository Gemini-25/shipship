using ShipEngine;

namespace ShipView;

/// <summary>
/// 한 틱 늦은 시계의 재생 (docs/PHYSICS.md §6-1 — PH5): 그리는 때 = 지난 틱 끝 + 틱 사이 f. ④가 지난 틱에 남긴 틱 안 기록 (<see cref="MotionTrack"/>)의 열쇠틀을
/// 에르미트로 메워 물체 · 몸 · 배의 자세를 그 때의 것으로 준다 — 관전 화면 · 다시보기 · 관찰 장면 (틱마다 8장)이 같은 셈을 쓴다. 기록이 없으면 지금 자세 (되살린 첫 틱 · 건너뛴 틱).
/// </summary>
public static class TrackPlay
{
    /// <summary>그 세계가 지난 틱에 남긴 기록 (돌린 뒤 세계의 틱은 기록한 틱 + 1 — 아니면 null).</summary>
    public static MotionTrack? Of(Sim w)
    {
        var t = w.Find<Structure>()?.Track;
        return t != null && t.Tick == w.Tick - 1 ? t : null;
    }

    /// <summary>물체 id의 그 때 자리 · 자세 (도면 m): 기록대로 (이번 틱에 사라진 것은 그 때까지만 — 뒤면 거짓) · 기록이 없으면 지금 자리.</summary>
    public static (bool ok, double x, double y, double th) Prop(Sim w, int id, double f)
    {
        var st = w.Find<Structure>();
        if (st == null) return (false, 0, 0, 0);
        var tr = Of(w);
        int i = st.IndexOf(id);
        double t = Math.Clamp(f, 0, 1) * Time.SecondsPerTick;
        if (tr != null && tr.Pose(MotionTrack.WhoProp, id, t, out double x, out double y, out double th))
        {
            if (i < 0 && t > tr.LastT(MotionTrack.WhoProp, id)) return (false, 0, 0, 0);   // 사라졌다 (우주로)
            return (true, x, y, th);
        }
        if (i < 0) return (false, 0, 0, 0);
        var p = st.Props[i];
        return (true, p.X, p.Y, p.Th);
    }

    /// <summary>몸 b의 그 때 세상 자세 (몸 열쇠틀 — 없으면 지금).</summary>
    public static (double x, double y, double th) BodyWorld(Structure st, MotionTrack? tr, int b, double t)
    {
        var o = st.Bodies[b];
        return tr != null && tr.Pose(MotionTrack.WhoBody, b, t, out double x, out double y, out double th) ? (x, y, th) : (o.X, o.Y, o.Th);
    }

    /// <summary>몸 b의 그 때 배 틀 무게중심 자리 · 배에 대한 자세 (몸 b · 배의 세상 자세를 그 때로 — 고르게 가속 · 부딪힘까지 그 길대로).</summary>
    public static (double qx, double qy, double th) ShipFrame(Structure st, MotionTrack tr, int b, double f)
    {
        double t = Math.Clamp(f, 0, 1) * Time.SecondsPerTick;
        var (bx, by, bth) = BodyWorld(st, tr, b, t);
        var (sx, sy, sth) = BodyWorld(st, tr, 0, t);
        var o0 = st.Bodies[0];
        double c = Math.Cos(-sth), sn = Math.Sin(-sth), dx = bx - sx, dy = by - sy;
        return (o0.Lx + c * dx - sn * dy, o0.Ly + sn * dx + c * dy, bth - sth);
    }

    /// <summary>
    /// 관찰 장면 (관전 화면 없이 Painter만): 그림판에 그 때 f의 자세를 넣는다 — 물체 자리 · 조각 자세 · 배의 세상 자세 (별 · 부스러기) · 틱 사이 몫. 기록이 없으면 지금 자세.
    /// </summary>
    public static void Apply(Painter p, Sim w, double f)
    {
        p.PropAt = id => Prop(w, id, f);
        var st = w.Find<Structure>();
        var tr = Of(w);
        p.TickFrac = tr != null ? Math.Clamp(f, 0, 1) : 1;
        p.Sub = Math.Clamp(f, 0, 1);
        if (st == null || st.Bodies.Count == 0 || tr == null) { p.Poses = null; p.SkyTh = double.NaN; p.ShipX = p.ShipY = double.NaN; return; }
        var (sx, sy, sth) = BodyWorld(st, tr, 0, Math.Clamp(f, 0, 1) * Time.SecondsPerTick);
        p.SkyTh = sth; p.ShipX = sx; p.ShipY = sy;
        if (st.Bodies.Count < 2) { p.Poses = null; return; }
        var poses = new (double, double, double, double)[st.Bodies.Count];
        for (int b = 1; b < poses.Length; b++)
        {
            var o = st.Bodies[b];
            var (qx, qy, th) = ShipFrame(st, tr, b, f);
            double cs = Math.Cos(th), sn = Math.Sin(th);
            poses[b] = (cs, sn, qx - (cs * o.Lx - sn * o.Ly), qy - (sn * o.Lx + cs * o.Ly));
        }
        poses[0] = (1, 0, 0, 0);
        p.Poses = poses;
    }
}
