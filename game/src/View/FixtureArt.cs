using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.5c 설비 · 가구 그림 표: FurnitureType → (몸체 · 움직임 · 가까이 디테일) 그림 함수 셋.
///   몸체(Body)  — 정적 층에 한 번만 그린다 (실루엣 · 무늬 · 큰 부품). 멀리서도 이것만으로 무엇인지 안다.
///   디테일(Fine) — 정적 디테일 층에 한 번만 그린다 (볼트 · 명판 글씨 · 바느질 · 작은 광택). 가까이(확대 1.1 이상)서만 보인다.
///   움직임(Life) — 동적 층에 매 프레임 (도는 것 · 빛 · 물 · 화면). 상태(가동 · 대기 · 고장 · 꺼짐 · 파손)를 읽어 다르게 움직인다.
/// 고장 효과(불꽃 · 연기 · 샘 · 김 · 깜빡임 · 갈림 · 가스 · 열 · 걸림)는 설비마다 다른 모양 · 자리에서 나온다.
/// 단계(II~IV) · 등급(Mk1/Mk3)은 Fix.Tier · Fix.Grade 로 넘겨 둔다 — v16.5b 가 이 위에 덧그린다.
/// 그리기는 Core 상태를 읽기만 한다 (결정론).
/// 표에 없는 종류만 예전 그림(ShipView.PaintFurniture · PaintFurnitureLife)으로 그린다.
/// </summary>
public static partial class FixtureArt
{
    public delegate void Paint(in Fix x);

    /// <summary>설비 상태 (그림이 읽는 것).</summary>
    public enum State { Passive, Running, Standby, Off, Parked, Fault, Broken, Wrecked }

    /// <summary>고장 났을 때 무엇이 나오는지.</summary>
    public enum Look { Sparks, Smoke, Leak, Steam, Flicker, Grind, Gas, Heat, Jam }

    /// <summary>표 한 줄: 몸체 · 움직임 · 디테일 · 고장 모양 · 고장 효과가 나오는 자리(긴 쪽 u, 짧은 쪽 v — 0~1).</summary>
    public sealed record Art(Paint Body, Paint Life, Paint Fine, Look Fault, float Eu, float Ev);

    /// <summary>확대가 이보다 크면 디테일 층을 보인다.</summary>
    public const float FineZoom = ZoomDetail.NearFrom;
    /// <summary>확대가 이보다 작으면 멀리 (움직임을 줄인다).</summary>
    public const float FarZoom = ZoomDetail.FarBelow;

    private static Dictionary<FurnitureType, Art>? _table;
    private static Dictionary<FurnitureType, Art> Table => _table ??= Build();

    private static Dictionary<FurnitureType, Art> Build()
    {
        var t = new Dictionary<FurnitureType, Art>();
        HomeArt(t);     // FixtureArtHome.cs — 잠 · 식사 · 의료 · 보관
        PowerArt(t);    // FixtureArtPower.cs — 동력 · 냉각 · 감시
        LifeArt(t);     // FixtureArtLife.cs — 생명 유지 · 재배 · 위생
        WorkArt(t);     // FixtureArtWork.cs — 정비 · 제작 · 채집
        CommandArt(t);  // FixtureArtCommand.cs — 지휘 · 통신 · 안전
        LeisureArt(t);  // FixtureArtLeisure.cs — 쉼 · 잠자리 환경
        GearArt(t); GearArt2(t); // FixtureArtGear*.cs — 압축-마 새 설비 30
        MedArt1(t); // FixtureArtMed1.cs — 의료 1차 수술실 · 혈액 냉장고 · 약장
        MedArt2(t);     // FixtureArtMed2.cs — 의료 2차 (투석기 · 인공 폐 · 인공 심장 충전대 · 장기 보관함 · 바이오 프린터 · 음압기)
        MedArt3(t);     // FixtureArtMed3.cs — 의료 3차 수술 로봇 팔
        return t;
    }

    public static bool Has(FurnitureType t) => Table.ContainsKey(t);
    public static int Count => Table.Count;
    public static Art? Of(FurnitureType t) => Table.TryGetValue(t, out var a) ? a : null;

    // ═══════════════════════════════ 그리기 문맥 ═══════════════════════════════

    /// <summary>
    /// 한 설비를 그리는 데 필요한 것 (읽기 전용). u = 긴 쪽(0~1), v = 짧은 쪽(0~1) — 가로든 세로든 같은 함수로 그린다.
    /// </summary>
    public readonly struct Fix
    {
        public readonly CanvasItem Ci;
        public readonly Furniture F;
        public readonly Machine? M;
        public readonly World? W;
        /// <summary>가구 칸 전체.</summary>
        public readonly Rect2 R;
        /// <summary>몸체 (가장자리 3px 안쪽).</summary>
        public readonly Rect2 B;
        public readonly Vector2 C;
        /// <summary>긴 쪽이 가로인지.</summary>
        public readonly bool Wide;
        /// <summary>긴 쪽 · 짧은 쪽 단위 벡터.</summary>
        public readonly Vector2 U, V;
        /// <summary>몸체 길이 (긴 쪽 · 짧은 쪽).</summary>
        public readonly float Lu, Lv;
        /// <summary>크기 배율 (한 칸 몸체 26px = 1).</summary>
        public readonly float S;
        /// <summary>쓰는 사람이 서는 쪽 (단위 벡터 · 모르면 아래).</summary>
        public readonly Vector2 Front;
        /// <summary>시각 (설비마다 위상이 다르다).</summary>
        public readonly float T;
        /// <summary>0 멀리 · 1 보통 · 2 가까이.</summary>
        public readonly int Lod;
        public readonly State St;
        /// <summary>실제 효율 0~1.</summary>
        public readonly float Eff;
        /// <summary>빛 세기 0~1 (가동 밝음 · 대기 희미 · 고장 깜빡임 · 꺼짐 0).</summary>
        public readonly float Glow;
        /// <summary>움직임 빠르기 0~1 (가동 · 대기 느림 · 고장 덜컥임 · 꺼짐 0).</summary>
        public readonly float Spin;
        /// <summary>기술 단계 1~4 (v16.5b 덧그림 자리).</summary>
        public readonly int Tier;
        /// <summary>등급 Mk1 · 정품 · Mk3 (v16.5b 덧그림 자리).</summary>
        public readonly MachineGrade Grade;
        public readonly Color Accent;
        public readonly int Id;

        public Fix(CanvasItem ci, Furniture f, World? w, float time, float zoom, bool live)
        {
            Ci = ci; F = f; M = f.Machine; W = w; Id = f.Id;
            R = ShipView.FurnitureRect(f);
            B = R.Grow(-3f);
            C = R.GetCenter();
            Wide = B.Size.X >= B.Size.Y;
            U = Wide ? Vector2.Right : Vector2.Down;
            V = Wide ? Vector2.Down : Vector2.Right;
            Lu = Wide ? B.Size.X : B.Size.Y;
            Lv = Wide ? B.Size.Y : B.Size.X;
            S = Mathf.Clamp(Mathf.Min(B.Size.X, B.Size.Y) / 26f, 0.85f, 2.6f);
            Front = FrontOf(f);
            T = time;
            Lod = ZoomDetail.Lod(zoom); // v16.24 확대 3단계 (UiZoom)
            Accent = Palette.Room(f.Room.Kind);
            Tier = M?.Tier ?? 1;
            Grade = M?.Grade ?? MachineGrade.Standard;
            if (!live) { St = State.Passive; Eff = 1f; Glow = 1f; Spin = 0f; return; }
            St = StateOf(f, M);
            Eff = M?.Efficiency ?? 1f;
            switch (St)
            {
                case State.Passive: Glow = 1f; Spin = 1f; break;
                case State.Running: Glow = 0.55f + 0.45f * Mathf.Clamp(Eff, 0f, 1f); Spin = 0.25f + 0.75f * Mathf.Clamp(Eff, 0f, 1f); break;
                case State.Standby: Glow = 0.22f + 0.1f * (0.5f + 0.5f * Mathf.Sin(time * 1.3f)); Spin = 0.06f; break;
                case State.Fault:
                {
                    float k = Hash(f.Id, (int)(time * 11f), 5);
                    Glow = k > 0.33f ? 0.75f : 0.08f;
                    Spin = 0.15f + 0.6f * (Mathf.Sin(time * 2.7f) > 0f ? 1f : 0.2f);
                    break;
                }
                default: Glow = 0f; Spin = 0f; break;
            }
        }

        public bool On => St is State.Running or State.Passive;
        public bool Dead => St is State.Off or State.Parked or State.Broken or State.Wrecked;
        public bool Lit => Glow > 0.05f;
        /// <summary>몸체 안의 한 점 (u 긴 쪽, v 짧은 쪽).</summary>
        public Vector2 P(float u, float v) => B.Position + U * (u * Lu) + V * (v * Lv);
        /// <summary>몸체 안의 네모 (u0,v0)~(u1,v1).</summary>
        public Rect2 Q(float u0, float v0, float u1, float v1)
        {
            var a = P(u0, v0);
            var b = P(u1, v1);
            return new Rect2(new Vector2(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y)), new Vector2(Mathf.Abs(b.X - a.X), Mathf.Abs(b.Y - a.Y)));
        }
        /// <summary>크기에 맞춘 픽셀.</summary>
        public float Px(float px) => px * S;
        /// <summary>움직이는 부품의 각도 (상태별 빠르기).</summary>
        public float Ang(float speed) => T * speed * Spin;
        /// <summary>쓰고 있는 사람 (예약하고 그 자리에서 일하거나 앉거나 누웠다).</summary>
        public CrewMember? User
        {
            get => F.ReservedBy is CrewMember c && Near(c) ? c : null;
        }
        private bool Near(CrewMember c)
        {
            var cell = c.Cell;
            foreach (var s in F.UseSpots) if (s == cell) return true;
            foreach (var s in F.Cells) if (s == cell) return true;
            return false;
        }
        /// <summary>이 가구 칸 위에 누운 · 앉은 사람 (침대 · 치료 침대 · 의자).</summary>
        public CrewMember? Occupant
        {
            get
            {
                if (W == null) return null;
                foreach (var c in W.Crew)
                {
                    if (c.Room != F.Room) continue;
                    var cell = c.Cell;
                    foreach (var s in F.Cells) if (s == cell) return c;
                }
                return null;
            }
        }
    }

    private static Vector2 FrontOf(Furniture f)
    {
        if (f.UseSpots.Count == 0) return Vector2.Down;
        float sx = 0f, sy = 0f;
        var fc = f.Center;
        foreach (var c in f.UseSpots) { sx += c.X + 0.5f - fc.X; sy += c.Y + 0.5f - fc.Y; }
        if (Mathf.Abs(sx) < 0.01f && Mathf.Abs(sy) < 0.01f) return Vector2.Down;
        return Mathf.Abs(sx) >= Mathf.Abs(sy) ? new Vector2(Mathf.Sign(sx), 0f) : new Vector2(0f, Mathf.Sign(sy));
    }

    /// <summary>Core 상태 → 그림 상태 (읽기만).</summary>
    public static State StateOf(Furniture f, Machine? m)
    {
        if (m == null) return State.Passive;
        bool wreck = false, strip = false;
        foreach (var q in m.Faults)
        {
            if (q.Kind == FaultKind.Wrecked) wreck = true;
            else if (q.Kind == FaultKind.Stripped) strip = true;
        }
        if (wreck) return State.Wrecked;
        if (strip || m.Faults.Count > 0 && m.Stopped) return State.Broken;
        if (m.LockedOut) return State.Off;
        if (m.Spec.PowerDraw > 0f && !m.Powered && f.Type != FurnitureType.EmergencyLight) return State.Off;
        if (m.Parked) return State.Parked;
        if (m.Faults.Count > 0) return State.Fault;
        if (m.CoolingDown || !m.Active || f.Room.Abandoned) return State.Standby;
        if (m.Efficiency < 0.01f) return State.Off;
        return State.Running;
    }

    // ═══════════════════════════════ 입구 (ShipView 가 부른다) ═══════════════════════════════

    /// <summary>정적 층: 표에 있으면 몸체를 그리고 true (없으면 예전 그림).</summary>
    public static bool PaintBody(CanvasItem ci, Furniture f)
    {
        if (!Table.TryGetValue(f.Type, out var a)) return false;
        var x = new Fix(ci, f, null, 0f, 1f, false);
        a.Body(in x);
        Weathering(in x, a); // 낡음 · 상처 (FixtureArtWear.cs)
        TierGrade(in x, a); // v16.5b 기술 수준 테두리 · 단계 부품 · 등급 손질 (FixtureArtTier.cs)
        return true;
    }

    /// <summary>정적 디테일 층 (가까이서만 보인다).</summary>
    public static void PaintFine(CanvasItem ci, Furniture f)
    {
        if (!Table.TryGetValue(f.Type, out var a)) return;
        var x = new Fix(ci, f, null, 0f, 2f, false);
        a.Fine(in x);
        WearClose(in x); // v16.24 가까이: 잔 낡은 자국
    }

    /// <summary>동적 층: 움직이는 부분 (단계 · 핵융합 같은 덧그림은 ShipView 가 앞뒤로 부른다).</summary>
    public static void PaintLife(CanvasItem ci, Furniture f, World w, float time, float zoom)
    {
        if (!Table.TryGetValue(f.Type, out var a)) return;
        var x = new Fix(ci, f, w, time, zoom, true);
        a.Life(in x);
    }

    /// <summary>동적 층 맨 위: 상태 표시 (꺼짐 어둠 · 대기 불 · 고장 효과 · 파손 연기 · 달아오름 · 가스 · 분말).</summary>
    public static void PaintState(CanvasItem ci, Furniture f, World w, float time, float zoom)
    {
        if (!Table.TryGetValue(f.Type, out var a) || f.Machine is not Machine m) return;
        var x = new Fix(ci, f, w, time, zoom, true);
        var e = x.P(a.Eu, a.Ev);
        switch (x.St)
        {
            case State.Off:
                Gfx.RoundRect(ci, x.R.Grow(-2f), new Color(0, 0, 0, 0.38f), 5);
                break;
            case State.Parked:
                Gfx.RoundRect(ci, x.R.Grow(-2f), new Color(0.02f, 0.04f, 0.08f, 0.26f), 5);
                Led(ci, new Vector2(x.B.Position.X + 3.5f, x.B.End.Y - 3.5f), ParkedBlue, 0.85f, 1.6f);
                break;
            case State.Standby:
                Led(ci, new Vector2(x.B.Position.X + 3.5f, x.B.End.Y - 3.5f), Amber, 0.35f + 0.4f * Pulse(time, 1.6f), 1.5f);
                break;
            case State.Fault:
                FaultFx(in x, a.Fault, e, 0.55f);
                break;
            case State.Broken:
                Gfx.RoundRect(ci, x.R.Grow(-2f), new Color(0, 0, 0, 0.22f), 5);
                FaultFx(in x, a.Fault, e, 1f);
                break;
            case State.Wrecked:
                Smoke(ci, e, time, x.Id, 0.5f, x.Lod, 2);
                break;
        }
        TierGradeLife(in x, a, m); // v16.5b 단계 부품 빛 · Mk.3 테 · Mk.1 손질 움직임 (FixtureArtTier.cs)
        Moments(in x, a, m, e); // 고장 순간 · 고치는 중 (FixtureArtWear.cs)
        if (x.Lod == 0) return;
        // 열 · 압력 스트레스: 아지랑이
        if (m.Heat > 0.85f) Shimmer(ci, x.B, time, Mathf.Clamp((m.Heat - 0.85f) / 0.5f, 0.15f, 1f));
        // 새어 고인 기체: 옅은 노란 초록 안개
        if (m.Vapor > 0.25f)
            for (int k = 0; k < 3; k++)
            {
                float ph = Mathf.PosMod(time * 0.25f + k / 3f, 1f);
                Dot(ci, x.C + new Vector2(Mathf.Sin(time * 0.7f + k * 2f) * x.B.Size.X * 0.35f, -ph * x.B.Size.Y * 0.4f), 5f + 9f * ph, GasTint.WithAlpha(0.1f * m.Vapor * (1f - ph)));
            }
        // 소화 분말 · 그을음: 회색 얼룩 (닦기 전까지)
        if (m.Fouled > 0.25f)
            for (int k = 0; k < 7; k++)
                Dot(ci, x.B.Position + new Vector2(Hash(x.Id, k, 61), Hash(x.Id, k, 62)) * x.B.Size, 1.2f + 2.2f * Hash(x.Id, k, 63), Powder.WithAlpha(0.35f * m.Fouled));
    }

    /// <summary>tier · grade · 주인 · 치움 · 낡음 단계가 바뀌면 정적 몸체를 다시 그린다 (v16.5b 덧그림이 바로 보이게).</summary>
    public static int Signature(Ship ship)
    {
        int h = 17;
        foreach (var f in ship.Furniture)
        {
            int k = f.Id * 31 + (f.Owner?.Id ?? -1) * 7 + (f.Stowed ? 3 : 0) + (f.Improved ? 5 : 0);
            if (f.Machine is Machine m) k += m.Tier * 101 + (int)m.Grade * 1009 + WearStep(m) * 10007 + ScarStep(m) * 100003; // 낡음 단계도
            h = unchecked(h * 486187739 + k);
        }
        return h;
    }

    // ═══════════════════════════════ 고장 효과 (모양마다 다르다) ═══════════════════════════════

    private static void FaultFx(in Fix x, Look look, Vector2 e, float strength)
    {
        var ci = x.Ci;
        float t = x.T;
        bool far = x.Lod == 0;
        switch (look)
        {
            case Look.Sparks:
            {
                // 짧게 튀는 불꽃 묶음 (주기마다 한 번 · 망가지면 자주)
                float cyc = Mathf.PosMod(t * (strength > 0.9f ? 1.6f : 0.8f) + Hash(x.Id, 1, 70), 1f);
                if (cyc < 0.16f)
                {
                    float k = 1f - cyc / 0.16f;
                    Dot(ci, e, 5f * k + 2f, SparkHot.WithAlpha(0.35f * k));
                    if (!far)
                        for (int i = 0; i < 6; i++)
                        {
                            var d = Vector2.FromAngle(Hash(x.Id, i + (int)(t * 1.6f) * 7, 71) * Mathf.Tau);
                            Line(ci, e + d * 1.5f, e + d * (3f + 7f * (1f - k)), SparkHot.WithAlpha(k), 1.2f);
                        }
                }
                break;
            }
            case Look.Smoke:
                Smoke(ci, e, t, x.Id, strength, x.Lod, 4);
                break;
            case Look.Leak:
            {
                // 떨어지는 방울과 번지는 웅덩이
                var puddle = e + new Vector2(0f, x.Px(6f));
                float grow = 0.6f + 0.4f * Mathf.Sin(t * 0.4f);
                ci.DrawSetTransform(puddle, 0f, new Vector2(1f, 0.45f));
                ci.DrawCircle(Vector2.Zero, x.Px(5f) * (0.6f + 0.6f * strength) * grow, LeakBlue.WithAlpha(0.35f), true, -1f, true);
                ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                if (far) break;
                for (int i = 0; i < 2; i++)
                {
                    float ph = Mathf.PosMod(t * 1.3f + i * 0.5f, 1f);
                    Dot(ci, e + new Vector2(0f, ph * x.Px(6f)), 1.3f, LeakBlue.WithAlpha(0.9f * (1f - ph * 0.5f)));
                }
                break;
            }
            case Look.Steam:
                for (int i = 0; i < (far ? 2 : 5); i++)
                {
                    float ph = Mathf.PosMod(t * 1.1f + i / 5f, 1f);
                    var p = e + new Vector2(Mathf.Sin(ph * 5f + i) * 4f + ph * 6f, -ph * x.Px(18f));
                    Dot(ci, p, 1.5f + 4.5f * ph, SteamWhite.WithAlpha(0.42f * strength * (1f - ph)));
                }
                break;
            case Look.Flicker:
            {
                // 화면 · 등이 지직거린다: 흰 줄 몇 개와 번쩍임
                var box = new Rect2(e - new Vector2(x.Px(7f), x.Px(5f)), new Vector2(x.Px(14f), x.Px(10f)));
                if (Hash(x.Id, (int)(t * 14f), 72) > 0.55f)
                {
                    ci.DrawRect(box, NoiseWhite.WithAlpha(0.18f * strength));
                    if (!far)
                        for (int i = 0; i < 3; i++)
                        {
                            float y = box.Position.Y + box.Size.Y * Hash(x.Id, (int)(t * 14f) + i, 73);
                            ci.DrawLine(new Vector2(box.Position.X, y), new Vector2(box.End.X, y), NoiseWhite.WithAlpha(0.6f), 1f);
                        }
                }
                break;
            }
            case Look.Grind:
            {
                // 덜컥이며 갈리는 소리: 떨림 선과 주황 쇳가루
                float j = Mathf.Sin(t * 37f) * 1.2f * strength;
                ci.DrawArc(e + new Vector2(j, 0f), x.Px(6f), 0f, Mathf.Tau, 12, Amber.WithAlpha(0.5f), 1f, true);
                if (far) break;
                for (int i = 0; i < 4; i++)
                {
                    float ph = Mathf.PosMod(t * 2.2f + i / 4f, 1f);
                    var d = Vector2.FromAngle(Hash(x.Id, i, 74) * Mathf.Tau);
                    Dot(ci, e + d * x.Px(4f + 8f * ph), 0.9f, Ember.WithAlpha(1f - ph));
                }
                break;
            }
            case Look.Gas:
                for (int i = 0; i < (far ? 2 : 4); i++)
                {
                    float ph = Mathf.PosMod(t * 0.35f + i / 4f, 1f);
                    var d = Vector2.FromAngle(Hash(x.Id, i, 75) * Mathf.Tau);
                    Dot(ci, e + d * x.Px(14f * ph), x.Px(3f + 8f * ph), GasTint.WithAlpha(0.22f * strength * (1f - ph)));
                }
                break;
            case Look.Heat:
            {
                float glow = 0.5f + 0.5f * Mathf.Sin(t * 3f);
                Dot(ci, e, x.Px(7f), Ember.WithAlpha((0.15f + 0.2f * glow) * strength));
                Shimmer(ci, new Rect2(e - new Vector2(x.Px(8f), x.Px(4f)), new Vector2(x.Px(16f), x.Px(8f))), t, strength);
                break;
            }
            case Look.Jam:
            {
                // 걸려서 안 움직인다: 빨간 걸쇠 표시와 먼지
                var s = x.Px(3.5f);
                Line(ci, e - new Vector2(s, s), e + new Vector2(s, s), Danger.WithAlpha(0.8f * (0.6f + 0.4f * Mathf.Sin(t * 4f))), 1.6f);
                Line(ci, e + new Vector2(-s, s), e + new Vector2(s, -s), Danger.WithAlpha(0.8f * (0.6f + 0.4f * Mathf.Sin(t * 4f))), 1.6f);
                if (!far)
                    for (int i = 0; i < 3; i++)
                    {
                        float ph = Mathf.PosMod(t * 0.5f + i / 3f, 1f);
                        Dot(ci, e + new Vector2((i - 1) * 4f, -ph * 8f), 1.4f + 2f * ph, Powder.WithAlpha(0.3f * (1f - ph)));
                    }
                break;
            }
        }
    }

    private static void Smoke(CanvasItem ci, Vector2 e, float t, int id, float strength, int lod, int n)
    {
        int count = lod == 0 ? 2 : n;
        for (int i = 0; i < count; i++)
        {
            float ph = Mathf.PosMod(t * 0.45f + i / (float)count + Hash(id, i, 76) * 0.2f, 1f);
            var p = e + new Vector2(Mathf.Sin(ph * 4f + i * 1.7f) * 4f + ph * 5f, -ph * 22f);
            Dot(ci, p, 2f + 6f * ph, SmokeGrey.WithAlpha(0.38f * strength * (1f - ph)));
        }
    }

    private static void Shimmer(CanvasItem ci, Rect2 r, float t, float a)
    {
        for (int k = 0; k < 3; k++)
        {
            float y = r.Position.Y + 2f - Mathf.PosMod(t * 9f + k * 5f, 14f);
            float x0 = r.Position.X + r.Size.X * (0.2f + 0.3f * k);
            ci.DrawLine(new Vector2(x0, y), new Vector2(x0 + 3f, y - 3f), Ember.WithAlpha(0.35f * a), 1f, true);
            ci.DrawLine(new Vector2(x0 + 3f, y - 3f), new Vector2(x0, y - 6f), Ember.WithAlpha(0.25f * a), 1f, true);
        }
    }

    // ═══════════════════════════════ 공통 색 ═══════════════════════════════

    internal static readonly Color Steel0 = new("#141922");
    internal static readonly Color Steel1 = new("#1d2430");
    internal static readonly Color Steel2 = new("#2a3340");
    internal static readonly Color Steel3 = new("#3d4757");
    internal static readonly Color Steel4 = new("#5b6577");
    internal static readonly Color Chrome = new("#a8b2c0");
    internal static readonly Color Rubber = new("#101215");
    internal static readonly Color GlassDark = new("#081218");
    internal static readonly Color Copper = new("#b87333");
    internal static readonly Color Brass = new("#a8844f");
    internal static readonly Color WarnYellow = new("#e0b64a");
    internal static readonly Color WarnBlack = new("#16140f");
    internal static readonly Color Good = new("#6ee7b7");
    internal static readonly Color Cyan = new("#5fd0c8");
    internal static readonly Color Amber = new("#ffb347");
    internal static readonly Color Danger = new("#ff5c6c");
    internal static readonly Color ParkedBlue = new("#7aa2c8");
    internal static readonly Color SparkHot = new("#ffe08a");
    internal static readonly Color Ember = new("#ff8a3c");
    internal static readonly Color SmokeGrey = new(0.55f, 0.55f, 0.58f);
    internal static readonly Color SteamWhite = new(0.92f, 0.95f, 1f);
    internal static readonly Color LeakBlue = new("#4aa3e0");
    internal static readonly Color GasTint = new("#c8e05a");
    internal static readonly Color Powder = new("#c9c4b8");
    internal static readonly Color NoiseWhite = new("#e8f4ff");
    internal static readonly Color Water = new("#3a8fd9");
    internal static readonly Color WaterLight = new("#9ad0ff");
    internal static readonly Color Leaf = new("#5fae3e");
    internal static readonly Color LeafLight = new("#8fd65a");
    internal static readonly Color Soil = new("#2a2017");
    internal static readonly Color Cream = new("#e8e2d4");
    internal static readonly Color Shadow = new(0f, 0f, 0f, 0.35f);
    internal static readonly Color Hi = new(1f, 1f, 1f, 0.10f);

    // ═══════════════════════════════ 그리기 도구 ═══════════════════════════════

    internal static float Hash(int x, int y, int k = 0)
    {
        uint h = unchecked((uint)(x * 374761393 + y * 668265263 + k * 2246822519u));
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    internal static float Pulse(float t, float speed) => 0.5f + 0.5f * Mathf.Sin(t * speed);

    internal static void Dot(CanvasItem ci, Vector2 p, float r, Color c) => ci.DrawCircle(p, r, c, true, -1f, true);
    internal static void Ring(CanvasItem ci, Vector2 p, float r, Color c, float w = 1f, int n = 24) => ci.DrawArc(p, r, 0f, Mathf.Tau, n, c, w, true);
    internal static void Line(CanvasItem ci, Vector2 a, Vector2 b, Color c, float w = 1f) => ci.DrawLine(a, b, c, w, true);
    internal static void Box(CanvasItem ci, Rect2 r, Color fill, float rad, Color? edge = null, int bw = 1) => Gfx.RoundRect(ci, r, fill, rad, edge, bw);

    /// <summary>위 · 왼쪽은 밝게, 아래 · 오른쪽은 어둡게 (빛이 왼쪽 위에서).</summary>
    internal static void Bevel(CanvasItem ci, Rect2 r, float a = 0.1f)
    {
        ci.DrawLine(new Vector2(r.Position.X + 2f, r.Position.Y + 1f), new Vector2(r.End.X - 2f, r.Position.Y + 1f), new Color(1, 1, 1, a), 1f);
        ci.DrawLine(new Vector2(r.Position.X + 1f, r.Position.Y + 2f), new Vector2(r.Position.X + 1f, r.End.Y - 2f), new Color(1, 1, 1, a * 0.7f), 1f);
        ci.DrawLine(new Vector2(r.Position.X + 2f, r.End.Y - 1f), new Vector2(r.End.X - 2f, r.End.Y - 1f), new Color(0, 0, 0, a * 2.5f), 1f);
        ci.DrawLine(new Vector2(r.End.X - 1f, r.Position.Y + 2f), new Vector2(r.End.X - 1f, r.End.Y - 2f), new Color(0, 0, 0, a * 2f), 1f);
    }

    /// <summary>나사 머리: 둥근 머리 + 홈.</summary>
    internal static void Bolt(CanvasItem ci, Vector2 p, float s = 1f)
    {
        ci.DrawCircle(p, 1.4f * s, new Color("#6b7486"), true, -1f, true);
        ci.DrawLine(p + new Vector2(-0.9f, -0.5f) * s, p + new Vector2(0.9f, 0.5f) * s, new Color("#2a303a"), 0.7f, true);
    }

    internal static void Bolts(CanvasItem ci, Rect2 r, float inset, float s = 1f)
    {
        Bolt(ci, r.Position + new Vector2(inset, inset), s);
        Bolt(ci, new Vector2(r.End.X - inset, r.Position.Y + inset), s);
        Bolt(ci, new Vector2(r.Position.X + inset, r.End.Y - inset), s);
        Bolt(ci, r.End - new Vector2(inset, inset), s);
    }

    /// <summary>통풍 틈 (가로 또는 세로 줄).</summary>
    internal static void Vents(CanvasItem ci, Rect2 r, int n, bool vertical, Color c, float w = 1.2f)
    {
        for (int k = 0; k < n; k++)
        {
            float f = (k + 0.5f) / n;
            if (vertical) ci.DrawLine(new Vector2(r.Position.X + r.Size.X * f, r.Position.Y), new Vector2(r.Position.X + r.Size.X * f, r.End.Y), c, w);
            else ci.DrawLine(new Vector2(r.Position.X, r.Position.Y + r.Size.Y * f), new Vector2(r.End.X, r.Position.Y + r.Size.Y * f), c, w);
        }
    }

    /// <summary>격자 (망 · 그릴).</summary>
    internal static void Grille(CanvasItem ci, Rect2 r, float step, Color c, float w = 1f)
    {
        for (float x = r.Position.X + step; x < r.End.X - 0.5f; x += step) ci.DrawLine(new Vector2(x, r.Position.Y), new Vector2(x, r.End.Y), c, w);
        for (float y = r.Position.Y + step; y < r.End.Y - 0.5f; y += step) ci.DrawLine(new Vector2(r.Position.X, y), new Vector2(r.End.X, y), c, w);
    }

    /// <summary>유리: 어두운 바탕 + 대각 반사.</summary>
    internal static void Glass(CanvasItem ci, Rect2 r, Color tint, float rad = 2f)
    {
        Box(ci, r, tint, rad);
        float d = Mathf.Min(r.Size.X, r.Size.Y) * 0.5f;
        ci.DrawLine(r.Position + new Vector2(2f, d), r.Position + new Vector2(d, 2f), new Color(1, 1, 1, 0.12f), 1.2f, true);
        ci.DrawLine(r.Position + new Vector2(2f, d + 3f), r.Position + new Vector2(d + 3f, 2f), new Color(1, 1, 1, 0.06f), 1f, true);
    }

    /// <summary>바늘 계기 (frac 0~1).</summary>
    internal static void Gauge(CanvasItem ci, Vector2 p, float r, float frac, Color needle, bool face = true)
    {
        if (face)
        {
            ci.DrawCircle(p, r, new Color("#d8dee8"), true, -1f, true);
            ci.DrawArc(p, r, 0f, Mathf.Tau, 16, new Color("#2a303a"), 1f, true);
            ci.DrawArc(p, r * 0.75f, Mathf.Pi * 0.1f, Mathf.Pi * 0.45f, 6, new Color("#c0392b"), 1f, true);
        }
        float a = Mathf.Pi * 0.75f + Mathf.Clamp(frac, 0f, 1f) * Mathf.Pi * 1.5f;
        ci.DrawLine(p, p + Vector2.FromAngle(a) * r * 0.85f, needle, 1f, true);
        ci.DrawCircle(p, 0.9f, new Color("#2a303a"), true, -1f, true);
    }

    /// <summary>명판 (얇은 판 + 글씨 자리 줄).</summary>
    internal static void Plate(CanvasItem ci, Rect2 r, Color c)
    {
        ci.DrawRect(r, c);
        ci.DrawLine(r.Position + new Vector2(1.5f, r.Size.Y * 0.5f), new Vector2(r.End.X - 1.5f, r.Position.Y + r.Size.Y * 0.5f), new Color(0, 0, 0, 0.45f), 1f);
    }

    /// <summary>경고 빗금 (노랑 · 검정).</summary>
    internal static void Stripes(CanvasItem ci, Rect2 r, float step = 6f)
    {
        ci.DrawRect(r, WarnBlack);
        float lean = r.Size.Y * 0.6f;
        for (float x = r.Position.X + 1f; x + lean < r.End.X; x += step)
            ci.DrawLine(new Vector2(x, r.End.Y - 0.5f), new Vector2(x + lean, r.Position.Y + 0.5f), WarnYellow.WithAlpha(0.8f), step * 0.4f);
    }

    /// <summary>관: 몸통 + 윗면 광택 + 끝 이음 고리.</summary>
    internal static void Pipe(CanvasItem ci, Vector2 a, Vector2 b, float w, Color c, bool flanges = true)
    {
        ci.DrawLine(a, b, c.Darkened(0.35f), w + 1.5f, true);
        ci.DrawLine(a, b, c, w, true);
        var d = (b - a).Normalized();
        var n = new Vector2(-d.Y, d.X);
        ci.DrawLine(a - n * w * 0.25f, b - n * w * 0.25f, c.Lightened(0.35f).WithAlpha(0.5f), Mathf.Max(0.8f, w * 0.25f), true);
        if (!flanges) return;
        ci.DrawLine(a - n * (w * 0.75f), a + n * (w * 0.75f), c.Lightened(0.15f), 2f, true);
        ci.DrawLine(b - n * (w * 0.75f), b + n * (w * 0.75f), c.Lightened(0.15f), 2f, true);
    }

    /// <summary>날개 (팬 · 임펠러).</summary>
    internal static void Fan(CanvasItem ci, Vector2 p, float r, int blades, float ang, Color c, float w = 2f, float sweep = 0.45f)
    {
        for (int k = 0; k < blades; k++)
        {
            float a = ang + k * Mathf.Tau / blades;
            ci.DrawLine(p + Vector2.FromAngle(a) * r * 0.25f, p + Vector2.FromAngle(a + sweep) * r, c, w, true);
        }
        ci.DrawCircle(p, r * 0.22f, c.Lightened(0.2f), true, -1f, true);
    }

    /// <summary>빛나는 점 (번짐 포함).</summary>
    internal static void Led(CanvasItem ci, Vector2 p, Color c, float a, float r = 1.5f)
    {
        if (a <= 0.02f) { ci.DrawCircle(p, r, new Color(0.08f, 0.09f, 0.11f), true, -1f, true); return; }
        ci.DrawCircle(p, r * 2.6f, c.WithAlpha(0.16f * a), true, -1f, true);
        ci.DrawCircle(p, r, c.WithAlpha(Mathf.Clamp(a, 0f, 1f)), true, -1f, true);
    }

    /// <summary>돌림 손잡이 (눈금 표시).</summary>
    internal static void Knob(CanvasItem ci, Vector2 p, float r, float ang, Color c)
    {
        ci.DrawCircle(p, r, c, true, -1f, true);
        ci.DrawArc(p, r, 0f, Mathf.Tau, 12, c.Darkened(0.4f), 1f, true);
        ci.DrawLine(p, p + Vector2.FromAngle(ang) * r * 0.9f, new Color(1, 1, 1, 0.7f), 1f, true);
    }

    /// <summary>늘어진 전선 · 호스 (두 점 사이 처짐).</summary>
    internal static void Cable(CanvasItem ci, Vector2 a, Vector2 b, float sag, Color c, float w)
    {
        var pts = new Vector2[7];
        var n = (b - a).Orthogonal().Normalized();
        for (int k = 0; k < 7; k++)
        {
            float s = k / 6f;
            pts[k] = a.Lerp(b, s) + n * sag * 4f * s * (1f - s);
        }
        ci.DrawPolyline(pts, c, w, true);
    }

    /// <summary>둥근 통 (위에서 본 원통: 테 + 뚜껑 + 광택).</summary>
    internal static void Can(CanvasItem ci, Vector2 p, float r, Color body, Color rim)
    {
        ci.DrawCircle(p + new Vector2(1.2f, 1.6f), r, new Color(0, 0, 0, 0.3f), true, -1f, true);
        ci.DrawCircle(p, r, body, true, -1f, true);
        ci.DrawArc(p, r, 0f, Mathf.Tau, 24, rim, 1.2f, true);
        ci.DrawArc(p, r * 0.7f, Mathf.Pi * 1.05f, Mathf.Pi * 1.55f, 8, new Color(1, 1, 1, 0.18f), 1.2f, true);
    }

    /// <summary>글씨 (가까이서만 쓰는 작은 표식).</summary>
    internal static void Tag(CanvasItem ci, Vector2 center, string text, int size, Color c) =>
        Gfx.TextCentered(ci, Fonts.Bold, center + new Vector2(0f, Gfx.CenterOffset(Fonts.Bold, size)), text, size, c);
}

/// <summary>ShipView 쪽 연결: 표를 부르고, 디테일 층 · 화면 밖 건너뛰기 · 상태 덧그림을 붙인다.</summary>
public partial class ShipView
{
    private DrawLayer? _fixFine;
    private Rect2 _fixView = new(-1e6f, -1e6f, 2e6f, 2e6f);
    private float _fixZoom = 1f;

    /// <summary>v16.5c 정적 디테일 층 (볼트 · 명판 글씨 · 바느질): 가까이서만 보인다. 정적 층처럼 바뀔 때만 다시 그린다.</summary>
    private void AddFixtureFineLayer()
    {
        _fixFine = new DrawLayer { Name = "FixtureFine", Painter = PaintFixtureFine, Visible = false };
        AddChild(_fixFine);
    }

    /// <summary>매 프레임: 확대 단계 · 보이는 범위.</summary>
    private void UpdateFixtureLod()
    {
        _fixZoom = Zoom;
        bool near = _fixZoom >= FixtureArt.FineZoom;
        if (_fixFine != null && _fixFine.Visible != near) _fixFine.Visible = near;
        var vp = GetViewport();
        var inv = vp.GetCanvasTransform().AffineInverse();
        _fixView = inv * vp.GetVisibleRect();
    }

    private void PaintFixtureFine(CanvasItem ci)
    {
        foreach (var f in _world.Ship.Furniture)
            if (!f.Stowed && !f.Room.Detached) FixtureArt.PaintFine(ci, f);
    }

    /// <summary>동적 층: 표에 있으면 표의 움직임 + 단계 모양(ShipViewTiers) + 상태. 표에 없으면 false (예전 그림).</summary>
    private bool PaintFixtureLife(CanvasItem ci, Furniture f)
    {
        if (!FixtureArt.Has(f.Type)) return false;
        if (!_fixView.Intersects(FurnitureRect(f).Grow(T * 4f))) return true; // 화면 밖은 건너뛴다
        float t = _time + f.Id * 0.73f;
        var m = f.Machine;
        if (m is { Tier: >= 2 } && f.Type != FurnitureType.ReactorCore) PaintTierLife(ci, f, m, t); // v11.3 단계마다 다른 모양 (유지)
        if (f.Type == FurnitureType.ReactorCore && m is { Tier: >= 3 }) PaintFusion(ci, f, t); // v10.8 핵융합로 (유지)
        else FixtureArt.PaintLife(ci, f, _world, t, _fixZoom);
        if (f.Type == FurnitureType.GrowBed && m?.Crop is CropState crop) PaintBlight(ci, f, crop); // v11.2
        if (f.Type is FurnitureType.Fridge or FurnitureType.MealDispenser) PaintTaint(ci, f); // v11.2
        FixtureArt.PaintState(ci, f, _world, t, _fixZoom);
        if (ZoomDetail.Shows(_fixZoom, Detail.GaugeDigits)) FixtureArt.PaintReadout(ci, f, _world, t); // v16.24 가까이: 계기 숫자
        return true;
    }
}
