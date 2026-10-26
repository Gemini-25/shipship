using ShipEngine;

namespace Lab;

/// <summary>법칙 시험 — 물리 세계 뒤 커밋 (docs/PHYSICS.md §10 · §11 PH11~PH16).</summary>
public static partial class Program
{
    /// <summary>왼쪽 외판에 구멍을 낼 작은 방 (구멍 곁에 물건).</summary>
    internal const string SpaceRoom = @"
#########
#.......#
#.......#
#.......#
#########";

    private static void Physics2Laws()
    {
        if (!Do("우주의 물체")) return;
        Console.WriteLine("\n[우주의 물체 — 세상 틀로 난다 (PH11)]");
        var t = Tables.Default;
        int kit = t.People.Items.Keys.ToList().IndexOf("수리 꾸러미");
        // 법칙 16 · 법칙 6 '점화 중 나감': 0.3g 점화 중 외판 구멍 밖으로 나간 꾸러미는 세상 틀로 곧게 난다 (자리 = x₀ + v t · 빠르기 그대로 1e-9) ·
        //   배의 몸은 그동안 0.3g로 빨라진다 · 몸 + 물체 + 기체 − 점화 장부가 맞는다
        {
            var lay = Worlds.Ascii(FlingRooms, Worlds.Seats);
            var s = FieldStructure(lay);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            st.Debris = false;
            st.PutItem(kit, 1, Worlds.At(lay, 1, 2));
            st.RequestDoor(lay.DoorAt[Worlds.At(lay, 6, 2)], 1);   // 열어 둔 문 (통로 공기까지 — 바람이 몇 틱 분다)
            s.Run(10);
            const double a = 0.3 * Structure.Gravity;
            var book0 = WorldBook(st);
            f.SetHole(Worlds.At(lay, 0, 2), 1.5f);
            int fly = -1; long t0 = 0; double x0 = 0, y0 = 0, vx0 = 0, vy0 = 0, bv0 = 0;
            for (int k = 0; k < 200 && fly < 0; k++)
            {
                st.ShipAx = a; s.Step();
                int i = st.Props.FindIndex(p => Structure.Flying(p));
                if (i >= 0) { var p = st.Props[i]; fly = p.Id; t0 = s.Tick; x0 = p.X; y0 = p.Y; vx0 = p.Vx; vy0 = p.Vy; bv0 = st.Bodies[0].Vx; }
            }
            const int n = 3;   // 배가 점화로 멀어진다 — 300m 안에서 (세 틱 · 7.2초)
            for (int k = 0; k < n; k++) { st.ShipAx = a; s.Step(); }
            int j = fly > 0 ? st.IndexOf(fly) : -1;
            var q = j >= 0 ? st.Props[j] : default;
            double dt = n * Time.SecondsPerTick, ex = x0 + vx0 * dt, ey = y0 + vy0 * dt;
            double bdv = st.Bodies.Count > 0 ? st.Bodies[0].Vx - bv0 : 0;
            var book1 = WorldBook(st);
            double gas = Math.Abs(st.GasPx) + Math.Abs(st.BurnPx) + 1;
            // 검토 고침: 이미 나는 것이 있어도 이번 틱에 나간 것은 그 틱에 날지 않는다 (나간 자리 그대로 세상 틀로 옮길 뿐) · 쓸어 담은 조각이 가져간 운동량도 장부에
            int cup = t.People.Items.Keys.ToList().IndexOf("컵");
            int late = st.PutItem(cup, 0, Worlds.At(lay, 3, 1));
            int li = st.IndexOf(late); var lp = st.Props[li];
            double lx0 = lp.X, ly0 = lp.Y;
            lp.State = 5; lp.Vx = 3; lp.Kg = 0.3f; st.Props[li] = lp;   // 시험 차림: 이번 틱에 빨려 나간 것처럼
            st.ShipAx = 0; s.Step();
            var lq = st.Props[st.IndexOf(late)];
            var (llx, lly) = st.LocalOf(0, lq.X, lq.Y);
            bool sameTick = Structure.Flying(lq) && Math.Abs(llx - lx0) < 1e-6 && Math.Abs(lly - ly0) < 1e-6;
            int shard = st.PutItem(cup, 0, Worlds.At(lay, 10, 2));
            { int si = st.IndexOf(shard); var sp = st.Props[si]; sp.Kind = 2; st.Props[si] = sp; }   // 깨진 조각처럼
            var bk0 = WorldBook(st);
            st.Sweep(new[] { Worlds.At(lay, 10, 2) });
            var bk1 = WorldBook(st);
            Check("검토 고침 (PH11 · PH13): 이미 나는 것이 있어도 이번 틱에 나간 컵은 그 틱에 날지 않는다 (나간 자리 그대로 — 배 틀 1e-6) · 움직이는 배에서 쓸어 담은 조각의 운동량이 장부에 남는다 (1e-9)",
                sameTick && Math.Abs(bk1.px - bk0.px) < 1e-9 * Math.Max(1, Math.Abs(bk0.px)) && Math.Abs(bk1.py - bk0.py) < 1e-9 * Math.Max(1, Math.Abs(bk0.py)),
                $"나간 컵 배 틀 ({lx0:0.000},{ly0:0.000}) → ({llx:0.000},{lly:0.000}) · 쓸어 담기 장부 어긋남 ({bk1.px - bk0.px:0.0e0}, {bk1.py - bk0.py:0.0e0})");
            Check("세상 빠르기 (법칙 16 · 6): 0.3g 점화 중 외판 구멍 밖으로 나간 수리 꾸러미는 세상 틀로 곧게 난다 — 세 틱 뒤 자리 x₀ + v t · 빠르기 그대로 (1e-9) · 배의 몸은 a·t (+ 뿜는 공기)만큼 빨라진다 · 몸 + 물체 + 기체 − 점화 장부가 그대로",
                fly > 0 && j >= 0 && Structure.Flying(q) && Math.Abs(q.X - ex) < 1e-9 * Math.Max(1, Math.Abs(ex)) && Math.Abs(q.Y - ey) < 1e-9 * Math.Max(1, Math.Abs(ey)) && q.Vx == vx0 && q.Vy == vy0
                && bdv > a * dt * 0.999 && Math.Abs(book1.px - book0.px) < 1e-6 * gas && Math.Abs(book1.py - book0.py) < 1e-6 * gas,
                $"나감 틱 {t0} · 꾸러미 ({q.X:0.000000}, {q.Y:0.000}) 바람 ({ex:0.000000}, {ey:0.000}) · 빠르기 ({q.Vx:0.000}, {q.Vy:0.000}) · 몸 Δv {bdv:0.0000} (a·t {a * dt:0.0000}) · 장부 어긋남 ({book1.px - book0.px:0.0e0}, {book1.py - book0.py:0.0e0}) / 크기 {gas:0.0e0}");
        }
    }
}
