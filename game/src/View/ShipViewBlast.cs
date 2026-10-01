using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.13 폭발 그림 — 종류마다 확실히 다르게:
///   섬광(수소 파란 · 산소 백열 · 폭약 짧은 흰빛) · 화구(연료 주황과 검은 연기 · 가스 · 추진제 · 아세틸렌) · 배터리 흰 불꽃과 튀는 셀 · 유독 연기 ·
///   분진의 번지는 노란 불길 · 증기 하얀 분출 · 냉매 서리 구름 · 소화기 분말 · 발효 국물 · 신호탄 붉은 불똥 · 아크 번개.
///   충격파 고리는 압력파가 닿은 칸에만 그려 벽 · 닫힌 문에 막히는 모양이 그대로 보인다 · 파편 궤적 · 불똥 · 연기 기둥 · 흔들림(끌 수 있다).
///   흔적: 방사형 그을음 줄기 · 깨진 조명 · 날아간 문짝 · 추모 촛불. 폭발성 물건 23종은 저마다 실루엣 · 무늬 · 상태(달아오름 · 쉭 · 카운트다운 · 터진 잔해).
/// 그리기는 시뮬레이션 상태를 읽기만 한다.
/// </summary>
public partial class ShipView
{
    /// <summary>폭발 화면 흔들림 (설정에서 끈다 — user://settings.cfg [view] blast_shake).</summary>
    public static bool BlastShake { get; set; } = LoadBlastShake();

    private static bool LoadBlastShake()
    {
        try
        {
            var cfg = new ConfigFile();
            return cfg.Load("user://settings.cfg") != Error.Ok || cfg.GetValue("view", "blast_shake", true).AsBool();
        }
        catch { return true; }
    }

    private readonly Dictionary<int, float> _blastSeen = new();

    private readonly record struct BlastLook(Color Outer, Color Mid, Color Core, Color Smoke, Color Flash, Color Spark);

    private static BlastLook Look(BlastKind k) => k switch
    {
        BlastKind.Battery => new(new("#cfe2ff"), new("#ffffff"), new("#ffffff"), new(0.42f, 0.47f, 0.28f), new("#eaf4ff"), new("#f1f7ff")),
        BlastKind.Hydrogen => new(new("#3d7dff"), new("#79b6ff"), new("#e2f1ff"), new(0.78f, 0.84f, 0.95f), new("#a9d4ff"), new("#bfe0ff")),
        BlastKind.Arc => new(new("#4f6bff"), new("#9fb4ff"), new("#ffffff"), new(0.3f, 0.3f, 0.36f), new("#c9d6ff"), new("#dbe4ff")),
        BlastKind.Fuel => new(new("#c2410c"), new("#f97316"), new("#fde047"), new(0.05f, 0.045f, 0.045f), new("#ffd9a0"), new("#ffb347")),
        BlastKind.Combustion => new(new("#b4380c"), new("#ff7b24"), new("#ffe9a8"), new(0.12f, 0.11f, 0.1f), new("#ffe1b0"), new("#ffb066")),
        BlastKind.Steam => new(new("#dfe8ee"), new("#f4f8fb"), new("#ffffff"), new(0.92f, 0.94f, 0.97f), new("#f0f6ff"), new("#ffffff")),
        BlastKind.Grease => new(new("#d9480f"), new("#ff8a1f"), new("#ffd166"), new(0.15f, 0.12f, 0.1f), new("#ffcf91"), new("#ffa94d")),
        BlastKind.Furnace => new(new("#e85d04"), new("#ffaa00"), new("#fff3b0"), new(0.2f, 0.18f, 0.16f), new("#ffe3a3"), new("#ff9500")),
        BlastKind.Refrigerant => new(new("#a5d8ff"), new("#d0ebff"), new("#ffffff"), new(0.8f, 0.9f, 0.97f), new("#e7f5ff"), new("#e7f5ff")),
        BlastKind.Oxygen => new(new("#fff4d6"), new("#ffffff"), new("#ffffff"), new(0.72f, 0.72f, 0.74f), new("#ffffff"), new("#fffbe6")),
        BlastKind.Gas => new(new("#f76707"), new("#ffa94d"), new("#fff3bf"), new(0.2f, 0.18f, 0.17f), new("#ffe8b0"), new("#ffc078")),
        BlastKind.Dust => new(new("#e8b100"), new("#ffd43b"), new("#fff9db"), new(0.45f, 0.38f, 0.28f), new("#fff3c4"), new("#ffe066")),
        BlastKind.Charge => new(new("#fff8e1"), new("#ffffff"), new("#ffffff"), new(0.33f, 0.32f, 0.31f), new("#ffffff"), new("#fff3bf")),
        BlastKind.ColdGas => new(new("#ced4da"), new("#f1f3f5"), new("#ffffff"), new(0.86f, 0.89f, 0.92f), new("#f8f9fa"), new("#ffffff")),
        BlastKind.Ferment => new(new("#7a5c2e"), new("#a07a44"), new("#d8c18f"), new(0.5f, 0.45f, 0.3f), new("#e9dcb8"), new("#8d6e3f")),
        BlastKind.Aerosol => new(new("#ff6b6b"), new("#ffa94d"), new("#ffe066"), new(0.36f, 0.3f, 0.42f), new("#ffe0b0"), new("#ffd8a8")),
        BlastKind.Propellant => new(new("#d9480f"), new("#ff922b"), new("#ffe066"), new(0.3f, 0.22f, 0.15f), new("#ffdba0"), new("#ffa94d")),
        BlastKind.Powder => new(new("#f1f3f5"), new("#ffffff"), new("#ffffff"), new(0.96f, 0.96f, 0.95f), new("#ffffff"), new("#ffffff")),
        BlastKind.Acetylene => new(new("#ffd43b"), new("#fff3bf"), new("#ffffff"), new(0.04f, 0.04f, 0.05f), new("#fffbe6"), new("#ffe8a1")),
        BlastKind.Nitrate => new(new("#d9480f"), new("#e8590c"), new("#ffd8a8"), new(0.62f, 0.3f, 0.12f), new("#ffd8a8"), new("#ff922b")),
        BlastKind.Flare => new(new("#ff1e3c"), new("#ff6b81"), new("#ffe3e3"), new(0.62f, 0.26f, 0.32f), new("#ff8fa3"), new("#ff4d6d")),
        _ => new(new("#ff5a1f"), new("#ff9a3a"), new("#ffe08a"), new(0.25f, 0.24f, 0.24f), new("#fff1d0"), new("#ffc078")),
    };

    private static bool Cloudy(BlastKind k) => k is BlastKind.Steam or BlastKind.Refrigerant or BlastKind.ColdGas or BlastKind.Powder or BlastKind.Ferment;

    // ═══════════════════════════════ 폭발 연출 (동적 레이어 맨 위) ═══════════════════════════════

    private void PaintBlasts(CanvasItem ci)
    {
        var b = _world.Blast;
        foreach (var rec in b.Recent)
        {
            if (!_blastSeen.TryGetValue(rec.Id, out float seen))
            {
                bool fresh = _world.Tick - rec.Tick < SimTime.Minutes(4);
                seen = fresh ? _time : -1000f; // 늦게 본 기록(불러오기)은 연출 없이 흔적만
                _blastSeen[rec.Id] = seen;
                if (fresh && BlastShake && rec.Power >= 0.08f) _main.Camera.Shake(Mathf.Clamp(4f + 18f * rec.Power * rec.Spec.Shock, 3f, 22f));
            }
            float age = _time - seen;
            if (age < 0f || age > 14f) continue;
            PaintBlast(ci, rec, age);
        }
        PaintJets(ci);
        if (_blastSeen.Count > 40)
            foreach (var old in _blastSeen.Keys.Where(id => b.Recent.All(r => r.Id != id)).ToList()) _blastSeen.Remove(old);
    }

    private void PaintBlast(CanvasItem ci, BlastRecord rec, float age)
    {
        var spec = rec.Spec;
        var look = Look(rec.Kind);
        var o = CellRect(rec.At).GetCenter();
        float pw = Mathf.Clamp(rec.Power, 0.05f, 1.5f);
        int seed = rec.Id * 131 + rec.At.X * 7 + rec.At.Y;

        // ── 0) 방 기압 출렁: 닿은 칸이 한 번 숨을 쉰다 ──
        if (age < 1.6f && spec.Shock > 0.1f)
        {
            float u = age / 1.6f;
            float breathe = Mathf.Sin(u * Mathf.Pi * 3f) * (1f - u);
            foreach (var wc in rec.Wave)
                if (wc.P > 0.05f && (wc.X + wc.Y) % 2 == 0)
                    ci.DrawRect(CellRect(new Cell(wc.X, wc.Y)), new Color(1f, 1f, 1f, 0.05f * Mathf.Abs(breathe) * Mathf.Min(1f, wc.P * 3f)));
        }

        // ── 1) 섬광: 닿은 칸이 하얗게 (수소는 파랗게, 산소는 오래 · 폭약은 아주 짧게) ──
        float flashDur = rec.Kind switch { BlastKind.Charge => 0.1f, BlastKind.Oxygen => 0.5f, BlastKind.Hydrogen => 0.35f, BlastKind.Arc => 0.3f, _ => 0.22f };
        if (age < flashDur && spec.Flash > 0.05f)
        {
            float u = 1f - age / flashDur;
            foreach (var wc in rec.Wave)
                if (wc.P > 0.02f) ci.DrawRect(CellRect(new Cell(wc.X, wc.Y)), look.Flash.WithAlpha(0.6f * u * spec.Flash * Mathf.Min(1f, wc.P * 3f + 0.25f)));
            ci.DrawCircle(o, T * (0.8f + 3f * pw) * (0.7f + 0.6f * (1f - u)), look.Flash.WithAlpha(0.7f * u), true, -1f, true);
        }

        // ── 2) 충격파 고리: 닿은 칸만 — 벽 · 닫힌 문에서 끊긴다 ──
        const float ringDur = 0.85f;
        if (age < ringDur && spec.Shock > 0.15f)
        {
            float maxD = 0.5f;
            foreach (var wc in rec.Wave) maxD = Mathf.Max(maxD, wc.D);
            float r = maxD * Mathf.Sqrt(age / ringDur);
            float fade = 1f - age / ringDur;
            var ring = Cloudy(rec.Kind) ? new Color(0.9f, 0.95f, 1f) : new Color(1f, 0.93f, 0.8f);
            foreach (var wc in rec.Wave)
            {
                float dd = Mathf.Abs(wc.D - r);
                if (dd > 0.75f) continue;
                var p = CellRect(new Cell(wc.X, wc.Y)).GetCenter();
                var dir = (p - o).LengthSquared() < 1f ? Vector2.Right : (p - o).Normalized();
                var perp = new Vector2(-dir.Y, dir.X);
                float a = Mathf.Min(1f, wc.P * 3.5f) * fade * (1f - dd / 0.75f);
                ci.DrawLine(p - perp * T * 0.48f, p + perp * T * 0.48f, ring.WithAlpha(0.85f * a), 1.5f + 3.5f * wc.P, true);
                ci.DrawLine(p - perp * T * 0.48f - dir * 3f, p + perp * T * 0.48f - dir * 3f, ring.WithAlpha(0.25f * a), 5f, true);
            }
            // 벽에 부딪친 곳: 벽면이 번쩍
            foreach (var wh in rec.WallHits)
            {
                float dd = Mathf.Abs(wh.D - r);
                if (dd > 0.8f) continue;
                var wr = CellRect(new Cell(wh.X, wh.Y)).Grow(-3f);
                ci.DrawRect(wr, new Color(1f, 0.85f, 0.6f, Mathf.Min(0.8f, wh.P * 2f) * fade * (1f - dd / 0.8f)), false, 2f);
            }
        }

        // ── 3) 종류마다 다른 몸통 ──
        switch (rec.Kind)
        {
            case BlastKind.Dust: PaintDustFront(ci, rec, age, look); break;
            case BlastKind.Arc: PaintArcBolts(ci, o, age, pw, seed); PaintFireball(ci, o, age, pw * 0.5f, look, seed, 0.6f); break;
            case BlastKind.Steam or BlastKind.Refrigerant or BlastKind.ColdGas or BlastKind.Powder: PaintBurstCloud(ci, rec, o, age, pw, look, seed); break;
            case BlastKind.Ferment: PaintSplatter(ci, o, age, pw, look, seed); break;
            case BlastKind.Grease: PaintFlameColumn(ci, o, age, pw, look, seed); break;
            case BlastKind.Hydrogen: PaintFireball(ci, o, age, pw, look, seed, 0.45f); break; // 거의 안 보이는 파란 불
            case BlastKind.Charge: PaintFireball(ci, o, age, pw * 0.7f, look, seed, 0.35f); break; // 짧고 작다 — 압력이 전부
            default: PaintFireball(ci, o, age, pw * Mathf.Max(0.4f, spec.Heat + 0.3f), look, seed, 1.1f); break;
        }

        // ── 4) 파편 궤적 ──
        foreach (var (s, k) in rec.Shards.Select((s, k) => (s, k)))
        {
            var from = ToPx(s.From);
            var to = ToPx(s.To);
            float len = (to - from).Length();
            if (len < 1f) continue;
            var dir = (to - from) / len;
            bool rocket = len > T * 6f && rec.Kind is BlastKind.Gas or BlastKind.Propellant or BlastKind.Oxygen && k == rec.Shards.Count - 1;
            float travel = (rocket ? 0.6f : 0.12f) + len / (T * (rocket ? 10f : 22f));
            float u = Mathf.Min(1f, age / travel);
            var head = from + dir * len * u;
            if (age < travel + 0.25f)
            {
                float tail = rocket ? Mathf.Min(len * u, T * 3f) : Mathf.Min(len * u, 20f);
                var c0 = rec.Kind == BlastKind.Battery ? new Color(0.9f, 0.95f, 1f) : look.Spark;
                ci.DrawLine(head - dir * tail, head, c0.WithAlpha(0.35f), rocket ? 7f : 3.5f, true);
                ci.DrawLine(head - dir * tail * 0.5f, head, new Color(1f, 1f, 0.95f, 0.9f), rocket ? 3f : 1.4f, true);
                if (rocket)
                {
                    // 통째로 날아가는 실린더: 몸통 + 뒤로 뿜는 불
                    ci.DrawCircle(head, 5f, new Color(0.75f, 0.45f, 0.2f), true, -1f, true);
                    ci.DrawLine(head - dir * 6f, head - dir * (16f + 6f * Mathf.Sin(_time * 40f)), look.Mid.WithAlpha(0.9f), 5f, true);
                    for (int t = 0; t < 6; t++) ci.DrawCircle(head - dir * (20f + t * 10f), 4f + t * 1.6f, look.Smoke.WithAlpha(0.25f * (1f - t / 6f)), true, -1f, true);
                }
                if (rec.Kind == BlastKind.Battery && k < 3)
                {
                    // 튀는 셀: 빙글 도는 작은 원통
                    float rot = age * 18f + k;
                    var ax = new Vector2(Mathf.Cos(rot), Mathf.Sin(rot)) * 4f;
                    ci.DrawLine(head - ax, head + ax, new Color(0.35f, 0.38f, 0.45f), 4f, true);
                    ci.DrawLine(head + ax, head + ax * 1.2f, new Color(0.95f, 0.85f, 0.4f), 2f, true);
                }
            }
            // 맞은 자리
            if (age > travel && age < travel + 0.6f && s.Hit != 0)
            {
                float v = 1f - (age - travel) / 0.6f;
                switch (s.Hit)
                {
                    case 2: // 외벽을 뚫었다: 흰 섬광 + 빨려 나가는 공기
                        ci.DrawCircle(to, 6f + 10f * (1f - v), new Color(1f, 1f, 1f, 0.8f * v), true, -1f, true);
                        for (int q = 0; q < 5; q++) ci.DrawLine(to - dir * (4f + q * 4f), to + dir * (8f + q * 5f) * (1f - v), new Color(0.8f, 0.9f, 1f, 0.5f * v), 1.2f, true);
                        break;
                    case 3: // 사람
                        for (int q = 0; q < 4; q++) ci.DrawCircle(to + new Vector2(Hash(seed, k, q) - 0.5f, Hash(k, seed, q) - 0.5f) * 10f, 1.6f, new Color(0.85f, 0.15f, 0.15f, 0.8f * v), true, -1f, true);
                        break;
                    default: // 벽 · 설비 · 문 · 물건: 회색 먼지와 불똥
                        for (int q = 0; q < 5; q++)
                        {
                            var sp = new Vector2(Hash(seed, k, q + 9) - 0.5f, Hash(k, seed, q + 4) - 0.5f) * 16f * (1f - v);
                            ci.DrawCircle(to + sp, 1.4f, (q % 2 == 0 ? look.Spark : new Color(0.6f, 0.6f, 0.6f)).WithAlpha(0.8f * v), true, -1f, true);
                        }
                        break;
                }
            }
        }

        // ── 5) 불똥 · 잔해 조각 ──
        int sparks = rec.Kind switch { BlastKind.Battery => 46, BlastKind.Flare => 60, BlastKind.Furnace => 36, BlastKind.Arc => 30, BlastKind.Charge => 26, _ => 18 } + (int)(20 * pw);
        if (Cloudy(rec.Kind)) sparks /= 4;
        float sparkLife = rec.Kind is BlastKind.Flare or BlastKind.Battery ? 3.2f : 1.6f;
        if (age < sparkLife)
            for (int k = 0; k < sparks; k++)
            {
                float ang = Hash(seed, k, 1) * Mathf.Tau;
                float sp = T * (2f + 7f * Hash(k, seed, 2)) * (0.5f + pw);
                float life = sparkLife * (0.4f + 0.6f * Hash(seed, k, 3));
                if (age > life) continue;
                float dist = sp * (1f - Mathf.Exp(-2.8f * age)) / 2.8f;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var p = o + dir * dist;
                if (rec.Kind is BlastKind.Flare or BlastKind.Furnace) p += new Vector2(0f, T * 0.6f * age * age); // 무거운 불똥은 떨어진다
                float fade = 1f - age / life;
                var c = rec.Kind == BlastKind.Battery && k % 3 == 0 ? new Color(0.6f, 0.85f, 1f) : look.Spark;
                ci.DrawLine(p - dir * 4f, p, c.WithAlpha(0.95f * fade), rec.Kind == BlastKind.Flare ? 2.4f : 1.5f, true);
            }
        if (age < 1.4f && spec.Shock > 0.3f && !Cloudy(rec.Kind))
            for (int k = 0; k < 10 + (int)(10 * pw); k++)
            {
                float ang = Hash(seed, k, 5) * Mathf.Tau;
                float dist = T * (0.8f + 3f * pw * Hash(k, seed, 6)) * Mathf.Min(1f, age * 2.5f);
                var p = o + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                float rot = age * 9f * (Hash(seed, k, 7) - 0.5f);
                var e1 = new Vector2(Mathf.Cos(rot), Mathf.Sin(rot)) * 3f;
                var e2 = new Vector2(-e1.Y, e1.X) * 0.6f;
                ci.DrawColoredPolygon(new[] { p - e1 - e2, p + e1 - e2 * 0.4f, p + e1 * 0.6f + e2, p - e1 * 0.3f + e2 }, new Color(0.45f, 0.45f, 0.47f, 0.9f * (1f - age / 1.4f)));
            }

        // ── 6) 연기 기둥 (연료 · 아세틸렌 검정 · 배터리 유독 누런빛 · 질산염 주황 갈색) — 가라앉는 구름 종류는 바닥으로 번진다 ──
        float smokeAmt = Mathf.Clamp(spec.Smoke + (rec.Kind == BlastKind.Battery ? 0.4f : 0f), 0f, 1.2f);
        if (smokeAmt > 0.05f && age > 0.3f && !Cloudy(rec.Kind))
        {
            int n = 8 + (int)(22 * pw * smokeAmt);
            for (int i = 0; i < n; i++)
            {
                float t = age - 0.3f - i * 0.22f;
                if (t < 0f || t > 9f) continue;
                float rise = T * (0.6f + 0.9f * t) * (0.8f + 0.4f * Hash(seed, i, 11));
                float drift = Mathf.Sin(i * 1.7f + t * 0.8f) * T * 0.35f * t;
                var p = o + new Vector2(drift, -rise);
                float rad = T * (0.35f + 0.25f * t) * (0.7f + pw);
                float a = smokeAmt * 0.45f * Mathf.Min(1f, t * 2f) * (1f - t / 9f);
                ci.DrawCircle(p, rad, look.Smoke.WithAlpha(a), true, -1f, true);
                if (rec.Kind == BlastKind.Battery && i % 3 == 0) ci.DrawCircle(p + new Vector2(rad * 0.3f, 0f), rad * 0.5f, new Color(0.6f, 0.7f, 0.2f, a * 0.5f), true, -1f, true); // 유독한 누런 띠
            }
        }

        // ── 7) 이름표: 종류 · 규모 ──
        if (age < 4.5f)
        {
            float a = Mathf.Min(1f, (4.5f - age) * 1.5f);
            var scaleCol = rec.Scale switch { BlastScale.Ship => Palette.Danger, BlastScale.System => Palette.Warning, BlastScale.Room => new Color("#ffd43b"), _ => Palette.TextDim };
            Gfx.Pill(ci, Fonts.Bold, o - new Vector2(0f, T * (1.3f + pw) + age * 6f), $"{spec.Name} · {BlastSystem.ScaleName(rec.Scale)} 규모" + (rec.Depth > 0 ? $" · 연쇄 {rec.Depth}" : ""), 11,
                Palette.Text.WithAlpha(a), new Color(0.05f, 0.05f, 0.07f, 0.8f * a), scaleCol.WithAlpha(a));
        }
    }

    /// <summary>화구: 겹친 둥근 불덩이가 부풀며 위로 올라가고 가장자리가 검게 식는다.</summary>
    private void PaintFireball(CanvasItem ci, Vector2 o, float age, float size, BlastLook look, int seed, float dur)
    {
        if (age > dur * 1.6f) return;
        float u = age / (dur * 1.6f);
        float grow = Mathf.Sqrt(Mathf.Min(1f, age / (dur * 0.5f)));
        float rad = T * (0.5f + 2.4f * size) * grow;
        var c = o - new Vector2(0f, T * 0.8f * u * size);
        for (int k = 0; k < 7; k++)
        {
            float ang = Hash(seed, k, 21) * Mathf.Tau + age * 0.6f;
            var off = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad * 0.45f * Hash(k, seed, 22);
            float r = rad * (0.45f + 0.35f * Hash(seed, k, 23));
            ci.DrawCircle(c + off, r, look.Outer.WithAlpha(0.55f * (1f - u)), true, -1f, true);
        }
        ci.DrawCircle(c, rad * 0.7f, look.Mid.WithAlpha(0.7f * (1f - u)), true, -1f, true);
        ci.DrawCircle(c, rad * 0.38f * (1f - 0.5f * u), look.Core.WithAlpha(0.9f * (1f - u * u)), true, -1f, true);
        // 식어 가는 가장자리 (그을음 고리)
        if (u > 0.4f) ci.DrawArc(c, rad * 0.95f, 0f, Mathf.Tau, 36, new Color(0.08f, 0.06f, 0.05f, 0.45f * (u - 0.4f) * (1f - u) * 4f), rad * 0.25f, true);
    }

    /// <summary>아크: 지그재그 번개 줄기.</summary>
    private void PaintArcBolts(CanvasItem ci, Vector2 o, float age, float pw, int seed)
    {
        if (age > 0.7f) return;
        int frame = (int)(age * 24f);
        for (int b = 0; b < 5; b++)
        {
            var p = o;
            float ang = Hash(seed, b, frame) * Mathf.Tau;
            var pts = new List<Vector2> { p };
            for (int s = 0; s < 6; s++)
            {
                ang += (Hash(b, s, frame + seed) - 0.5f) * 1.6f;
                p += new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * T * (0.25f + 0.35f * pw);
                pts.Add(p);
            }
            var arr = pts.ToArray();
            ci.DrawPolyline(arr, new Color(0.55f, 0.65f, 1f, 0.45f * (1f - age / 0.7f)), 5f, true);
            ci.DrawPolyline(arr, new Color(1f, 1f, 1f, 0.95f * (1f - age / 0.7f)), 1.6f, true);
        }
    }

    /// <summary>분진: 노란 불길이 방 안 가루를 타고 번져 나간다.</summary>
    private void PaintDustFront(CanvasItem ci, BlastRecord rec, float age, BlastLook look)
    {
        if (age > 3.2f || rec.Room < 0 || rec.Room >= _world.Ship.Rooms.Count) return;
        var room = _world.Ship.Rooms[rec.Room];
        float front = age * 4.5f;
        foreach (var c in room.Cells)
        {
            float d = (new System.Numerics.Vector2(c.X, c.Y) - new System.Numerics.Vector2(rec.At.X, rec.At.Y)).Length();
            if (d > front) continue;
            float since = (front - d) / 4.5f;
            float a = Mathf.Clamp(1f - since / 1.6f, 0f, 1f) * Mathf.Min(1f, (3.2f - age) * 1.2f);
            if (a <= 0.02f) continue;
            var r = CellRect(c);
            float fl = Mathf.Sin(_time * 22f + c.X * 1.3f + c.Y * 0.7f);
            ci.DrawRect(r, look.Outer.WithAlpha(0.35f * a));
            var base_ = r.GetCenter() + new Vector2(0f, T * 0.3f);
            ci.DrawColoredPolygon(new[] { base_ + new Vector2(-T * 0.35f, 0f), base_ + new Vector2(T * 0.05f * fl, -T * (0.5f + 0.25f * a)), base_ + new Vector2(T * 0.35f, 0f) }, look.Mid.WithAlpha(0.8f * a));
            ci.DrawCircle(r.GetCenter(), T * 0.12f, look.Core.WithAlpha(0.7f * a), true, -1f, true);
        }
    }

    /// <summary>증기 · 냉매 · 차가운 가스 · 소화 분말: 하얗게 부풀어 바닥으로 번지는 구름 (냉매는 서리 반짝임 · 분말은 천천히 가라앉는다).</summary>
    private void PaintBurstCloud(CanvasItem ci, BlastRecord rec, Vector2 o, float age, float pw, BlastLook look, int seed)
    {
        float dur = rec.Kind == BlastKind.Powder ? 7f : rec.Kind == BlastKind.Steam ? 3.5f : 5f;
        if (age > dur) return;
        float u = age / dur;
        // 분출 줄기 (처음 0.6초)
        if (age < 0.6f)
            for (int k = 0; k < 14; k++)
            {
                float ang = Hash(seed, k, 31) * Mathf.Tau;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float len = T * (1.2f + 3f * pw) * Mathf.Sqrt(age / 0.6f);
                ci.DrawLine(o + dir * T * 0.2f, o + dir * len, look.Mid.WithAlpha(0.7f * (1f - age / 0.6f)), 3f, true);
            }
        // 구름: 닿은 칸을 따라 번진다 (벽에 막힌 모양 그대로)
        foreach (var wc in rec.Wave)
        {
            if (wc.D > (1f + 4f * pw) * Mathf.Sqrt(Mathf.Min(1f, age / 1.2f)) + 0.3f) continue;
            var p = CellRect(new Cell(wc.X, wc.Y)).GetCenter();
            float wob = Mathf.Sin(_time * 1.3f + wc.X * 0.9f + wc.Y * 1.7f) * 4f;
            float a = 0.55f * (1f - u) * Mathf.Min(1f, wc.P * 4f + 0.3f);
            ci.DrawCircle(p + new Vector2(wob, -wob * 0.5f), T * (0.55f + 0.2f * u), look.Smoke.WithAlpha(a), true, -1f, true);
            if (rec.Kind == BlastKind.Refrigerant && (wc.X * 3 + wc.Y) % 4 == 0)
            {
                // 서리 반짝임 (육각 별)
                float tw = 0.5f + 0.5f * Mathf.Sin(_time * 6f + wc.X + wc.Y * 2f);
                for (int r = 0; r < 3; r++)
                {
                    var ax = new Vector2(Mathf.Cos(r * Mathf.Pi / 3f), Mathf.Sin(r * Mathf.Pi / 3f)) * 4f;
                    ci.DrawLine(p - ax, p + ax, new Color(0.9f, 0.97f, 1f, 0.8f * tw * (1f - u)), 1f, true);
                }
            }
            if (rec.Kind == BlastKind.Powder && (wc.X + wc.Y * 3) % 3 == 0)
                ci.DrawCircle(p + new Vector2(0f, T * 0.3f * u), 2f, new Color(1f, 1f, 1f, 0.6f * (1f - u)), true, -1f, true); // 가라앉는 가루
        }
    }

    /// <summary>발효 항아리: 갈색 국물 방울이 사방으로 튄다 (흔적에 얼룩으로 남는다).</summary>
    private void PaintSplatter(CanvasItem ci, Vector2 o, float age, float pw, BlastLook look, int seed)
    {
        if (age > 1.5f) return;
        for (int k = 0; k < 26; k++)
        {
            float ang = Hash(seed, k, 41) * Mathf.Tau;
            float reach = T * (0.6f + 2.2f * Hash(k, seed, 42));
            float u = Mathf.Min(1f, age / 0.45f);
            var p = o + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * reach * u;
            float r = 1.5f + 2.5f * Hash(seed, k, 43);
            ci.DrawCircle(p, r * (u >= 1f ? 1.4f : 1f), look.Mid.WithAlpha(0.85f * (1f - age / 1.5f)), true, -1f, true);
            if (k % 4 == 0) ci.DrawCircle(p, r * 0.5f, new Color(0.9f, 0.95f, 1f, 0.6f * (1f - age / 1.5f)), true, -1f, true); // 유리 조각
        }
        ci.DrawCircle(o, T * 0.4f * Mathf.Min(1f, age * 4f), look.Mid.WithAlpha(0.4f * (1f - age / 1.5f)), true, -1f, true);
    }

    /// <summary>기름 불: 위로 치솟는 불기둥.</summary>
    private void PaintFlameColumn(CanvasItem ci, Vector2 o, float age, float pw, BlastLook look, int seed)
    {
        if (age > 2.6f) return;
        float h = T * (1.5f + 2f * pw) * Mathf.Min(1f, age * 3f) * (1f - age / 2.6f);
        for (int k = 0; k < 9; k++)
        {
            float t = k / 8f;
            float sway = Mathf.Sin(_time * 12f + k) * T * 0.15f * t;
            var p = o + new Vector2(sway, -h * t);
            float r = T * (0.45f - 0.3f * t) * (0.8f + 0.4f * pw);
            ci.DrawCircle(p, r, (t < 0.3f ? look.Core : t < 0.65f ? look.Mid : look.Outer).WithAlpha(0.75f * (1f - t * 0.6f)), true, -1f, true);
        }
    }

    /// <summary>불기둥 · 셀 분출: 몇 분 이어지는 분출 (배터리는 흰 불꽃 분수, 연료 · 가스는 주황 불혀).</summary>
    private void PaintJets(CanvasItem ci)
    {
        foreach (var j in _world.Blast.Jets)
        {
            var o = CellRect(j.at).GetCenter();
            var look = Look(j.kind);
            float a = Mathf.Clamp((j.until - _world.Tick) / (float)SimTime.Minutes(1f), 0.2f, 1f);
            if (j.kind == BlastKind.Battery)
            {
                for (int k = 0; k < 14; k++)
                {
                    float ph = Mathf.PosMod(_time * 2.2f + k * 0.37f, 1f);
                    float ang = -Mathf.Pi / 2f + (Hash(j.at.X, k, 51) - 0.5f) * 1.4f;
                    var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    var p = o + dir * T * 1.4f * ph + new Vector2(0f, T * 0.9f * ph * ph);
                    ci.DrawLine(p - dir * 3f, p, new Color(0.95f, 0.97f, 1f, a * (1f - ph)), 1.5f, true);
                }
                ci.DrawCircle(o, T * 0.18f, new Color(1f, 1f, 1f, 0.8f * a), true, -1f, true);
            }
            else
            {
                for (int k = 0; k < 6; k++)
                {
                    float t = k / 5f;
                    float sway = Mathf.Sin(_time * 14f + k * 1.3f) * T * 0.12f * t;
                    ci.DrawCircle(o + new Vector2(sway, -T * 0.9f * t), T * (0.3f - 0.18f * t), (t < 0.35f ? look.Core : t < 0.7f ? look.Mid : look.Outer).WithAlpha(0.7f * a), true, -1f, true);
                }
            }
        }
    }

    // ═══════════════════════════════ 흔적 (바닥 위 · 며칠) ═══════════════════════════════

    private void PaintBlastScars(CanvasItem ci)
    {
        var b = _world.Blast;
        foreach (var s in b.Scars)
        {
            if (s.Fade <= 0.01f && !s.Memorial) continue;
            var o = CellRect(s.At).GetCenter();
            float f = Mathf.Max(s.Fade, s.Memorial ? 0.25f : 0f);
            var tone = s.Kind switch
            {
                BlastKind.Steam or BlastKind.ColdGas => new Color(0.55f, 0.65f, 0.72f),
                BlastKind.Refrigerant => new Color(0.75f, 0.88f, 0.95f),
                BlastKind.Powder => new Color(0.92f, 0.92f, 0.9f),
                BlastKind.Ferment => new Color(0.45f, 0.33f, 0.16f),
                BlastKind.Nitrate => new Color(0.35f, 0.17f, 0.06f),
                BlastKind.Dust => new Color(0.22f, 0.17f, 0.08f),
                _ => new Color(0.03f, 0.025f, 0.02f),
            };
            float baseA = s.Kind is BlastKind.Powder or BlastKind.Refrigerant ? 0.5f : 0.6f;
            // 방사형 줄기: 막힌 쪽은 짧다
            foreach (var (ang, len) in s.Rays)
            {
                if (len <= 0.02f) continue;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var perp = new Vector2(-dir.Y, dir.X);
                float L = len * s.Reach * T;
                float w0 = T * (0.18f + 0.12f * s.Power);
                ci.DrawColoredPolygon(new[] { o + perp * w0, o + dir * L, o - perp * w0 }, tone.WithAlpha(baseA * f * 0.8f));
            }
            ci.DrawCircle(o, T * (0.35f + 0.35f * s.Power), tone.WithAlpha(baseA * f), true, -1f, true);
            if (s.Kind == BlastKind.Oxygen) ci.DrawCircle(o, T * 0.18f, new Color(0.95f, 0.9f, 0.8f, 0.35f * f), true, -1f, true); // 백열에 하얗게 탄 한가운데
            if (s.Kind == BlastKind.Charge) ci.DrawArc(o, T * 0.3f, 0f, Mathf.Tau, 16, new Color(0f, 0f, 0f, 0.6f * f), 3f, true); // 움푹 팬 자국
            // 깨진 조명: 매달린 등 + 가끔 튀는 불꽃
            foreach (var lc in s.Lights)
            {
                var r = CellRect(lc);
                var top = r.GetCenter() - new Vector2(0f, T * 0.32f);
                float swing = Mathf.Sin(_time * 1.7f + lc.X) * 0.25f;
                var hang = top + new Vector2(Mathf.Sin(swing) * 8f, Mathf.Cos(swing) * 8f);
                ci.DrawLine(top, hang, new Color(0.2f, 0.2f, 0.22f, 0.9f * Mathf.Max(f, 0.4f)), 1f, true);
                ci.DrawColoredPolygon(new[] { hang + new Vector2(-5f, 0f), hang + new Vector2(5f, 0f), hang + new Vector2(2f, 5f), hang + new Vector2(-1f, 3f) }, new Color(0.75f, 0.78f, 0.8f, 0.8f * Mathf.Max(f, 0.4f)));
                if (Mathf.PosMod(_time * 0.9f + lc.X * 0.37f + lc.Y * 0.11f, 3f) < 0.08f)
                    for (int k = 0; k < 4; k++) ci.DrawLine(hang, hang + new Vector2((k - 1.5f) * 3f, 6f + k * 2f), new Color(1f, 0.9f, 0.5f, 0.9f), 1f, true);
            }
            // 추모: 촛불과 꽃
            if (s.Memorial)
            {
                var m = o + new Vector2(T * 0.55f, T * 0.25f);
                ci.DrawRect(new Rect2(m - new Vector2(2f, 0f), new Vector2(4f, 7f)), new Color(0.95f, 0.93f, 0.85f));
                float fl = 0.7f + 0.3f * Mathf.Sin(_time * 9f);
                ci.DrawCircle(m - new Vector2(0f, 2.5f), 2.2f * fl, new Color(1f, 0.8f, 0.35f, 0.95f), true, -1f, true);
                ci.DrawCircle(m - new Vector2(0f, 2.5f), 6f, new Color(1f, 0.75f, 0.3f, 0.12f * fl), true, -1f, true);
                for (int k = 0; k < 3; k++) ci.DrawCircle(m + new Vector2(-8f + k * 3.5f, 5f), 2f, k == 1 ? new Color(0.95f, 0.95f, 1f) : new Color(0.95f, 0.75f, 0.85f), true, -1f, true);
            }
        }
        // 문짝이 날아간 문: 바닥에 나뒹구는 찌그러진 문짝 + 비틀린 문틀
        foreach (int id in b.BlownDoors)
        {
            if (id >= _world.Ship.Doors.Count) continue;
            var d = _world.Ship.Doors[id];
            var r = CellRect(d.Cell);
            var side = d.ConnectsVertically ? new Vector2(1, 0) : new Vector2(0, 1);
            var along = d.ConnectsVertically ? new Vector2(0, 1) : new Vector2(1, 0);
            var leaf = r.GetCenter() + along * T * 0.95f + side * T * 0.2f;
            var a = side.Rotated(0.35f) * T * 0.42f;
            var bb = along.Rotated(0.35f) * T * 0.12f;
            ci.DrawColoredPolygon(new[] { leaf - a - bb, leaf + a - bb * 0.6f, leaf + a + bb, leaf - a * 0.8f + bb * 1.3f }, new Color(0.38f, 0.42f, 0.5f, 0.95f));
            ci.DrawLine(leaf - a * 0.5f, leaf + a * 0.4f + bb, new Color(0.15f, 0.15f, 0.18f, 0.9f), 1.5f, true);
            ci.DrawPolyline(new[] { r.Position + side * 2f, r.GetCenter() + side * 5f - along * 4f, r.End - side * 2f }, new Color(0.95f, 0.55f, 0.25f, 0.9f), 2f, true);
        }
    }

    // ═══════════════════════════════ 폭발성 물건 23종 ═══════════════════════════════

    private void PaintExplosives(CanvasItem ci)
    {
        var items = _world.Blast.Items;
        float z = Zoom;
        foreach (var e in items.All)
        {
            if (e.Spent && _world.Tick - e.SpentAt > SimTime.TicksPerDay) continue;
            if (e.Kind == ExplosiveKind.PortableBattery && !e.Primed && e.Heat < 0.35f && !e.Spent) continue; // 몸통은 이동식 장비 그림이 맡는다
            Vector2 c;
            float s;
            if (e.Carried >= 0 && _world.Crew.FirstOrDefault(x => x.Id == e.Carried) is CrewMember carrier) { c = CrewPx(carrier) + new Vector2(7f, -4f); s = 0.55f; }
            else
            {
                var r = CellRect(e.Cell);
                // 보관함 안 물건은 구석에 작게 (꼬리표)
                c = e.Inside ? r.Position + new Vector2(T * (0.22f + 0.18f * (e.Id % 3)), T * 0.78f) : r.GetCenter() + new Vector2(T * 0.12f * ((e.Id % 3) - 1), T * 0.05f);
                s = e.Inside ? 0.42f : 0.8f;
            }
            if (e.Spent) { PaintWreckItem(ci, c, s, e); continue; }
            if (z < 0.45f)
            {
                // 멀리서: 실루엣만
                ci.DrawCircle(c, 3.5f, ItemTint(e.Kind), true, -1f, true);
                if (e.Primed) ci.DrawArc(c, 6f + 2f * Mathf.Sin(_time * 10f), 0f, Mathf.Tau, 14, Palette.Danger, 1.5f, true);
                continue;
            }
            PaintItemBody(ci, c, s * T * 0.5f, e);
            PaintItemState(ci, c, s * T * 0.5f, e);
        }
    }

    private static Color ItemTint(ExplosiveKind k) => k switch
    {
        ExplosiveKind.OxygenTank => new("#3c9a5f"), ExplosiveKind.HydrogenTank => new("#d64545"), ExplosiveKind.FuelCan => new("#e8590c"),
        ExplosiveKind.BatteryCell => new("#495057"), ExplosiveKind.GasCylinder => new("#f59f00"), ExplosiveKind.Dust => new("#d9c9a3"),
        ExplosiveKind.PressureVessel => new("#5c7cfa"), ExplosiveKind.FermentJar => new("#a07a44"), ExplosiveKind.Extinguisher => new("#e03131"),
        ExplosiveKind.Aerosol => new("#be4bdb"), ExplosiveKind.RefrigerantCan => new("#74c0fc"), ExplosiveKind.SuitO2Pack => new("#e9ecef"),
        ExplosiveKind.WeldingGas => new("#862e2e"), ExplosiveKind.Propellant => new("#fcc419"), ExplosiveKind.Charge => new("#8a7a52"),
        ExplosiveKind.Solvent => new("#4c6ef5"), ExplosiveKind.PortableBattery => new("#2b8a3e"), ExplosiveKind.CapacitorPack => new("#fab005"),
        ExplosiveKind.Fertilizer => new("#f8f9fa"), ExplosiveKind.MethaneTank => new("#5c940d"), ExplosiveKind.CO2Cylinder => new("#868e96"),
        ExplosiveKind.Flare => new("#ff2d55"), ExplosiveKind.Lubricant => new("#343a40"), _ => new("#adb5bd"),
    };

    /// <summary>물건 몸통: 종류마다 다른 실루엣과 세부 (밸브 · 계기 · 손잡이 · 띠 · 라벨).</summary>
    private void PaintItemBody(CanvasItem ci, Vector2 c, float h, Explosive e)
    {
        var tint = ItemTint(e.Kind);
        var dark = tint.Darkened(0.45f);
        var line = new Color(0f, 0f, 0f, 0.55f);
        var hi = new Color(1f, 1f, 1f, 0.35f);
        float w = h * 0.55f;
        // 그림자
        ci.DrawCircle(c + new Vector2(1.5f, h * 0.85f), h * 0.6f, new Color(0f, 0f, 0f, 0.25f), true, -1f, true);
        switch (e.Kind)
        {
            case ExplosiveKind.OxygenTank or ExplosiveKind.CO2Cylinder:
            {
                // 세운 원통: 둥근 어깨 · 흰 띠(산소) / 검은 머리(CO2) · 밸브 · 손바퀴
                var body = new Rect2(c.X - w * 0.5f, c.Y - h * 0.6f, w, h * 1.4f);
                Gfx.RoundRect(ci, body, tint, w * 0.5f, line, 1);
                ci.DrawRect(new Rect2(body.Position.X + 1f, body.Position.Y + h * 0.25f, w - 2f, h * 0.16f), e.Kind == ExplosiveKind.OxygenTank ? new Color(0.95f, 0.95f, 0.95f) : new Color(0.1f, 0.1f, 0.1f));
                ci.DrawLine(new Vector2(body.Position.X + w * 0.25f, body.Position.Y + 3f), new Vector2(body.Position.X + w * 0.25f, body.End.Y - 3f), hi, 1.2f, true);
                ci.DrawRect(new Rect2(c.X - w * 0.18f, body.Position.Y - h * 0.22f, w * 0.36f, h * 0.24f), new Color(0.75f, 0.75f, 0.78f));
                ci.DrawCircle(new Vector2(c.X, body.Position.Y - h * 0.26f), w * 0.3f, new Color(0.55f, 0.57f, 0.6f), false, 1.2f, true);
                if (e.Kind == ExplosiveKind.OxygenTank) ci.DrawCircle(new Vector2(c.X + w * 0.5f, body.Position.Y - h * 0.05f), w * 0.2f, new Color(0.95f, 0.95f, 0.9f), true, -1f, true); // 압력계
                break;
            }
            case ExplosiveKind.HydrogenTank or ExplosiveKind.MethaneTank:
            {
                // 굵은 탱크: 가로띠 두 줄 · 밸브 둘 (메탄은 녹색 · 불꽃 경고판)
                var body = new Rect2(c.X - w * 0.75f, c.Y - h * 0.55f, w * 1.5f, h * 1.3f);
                Gfx.RoundRect(ci, body, tint, w * 0.6f, line, 1);
                for (int k = 0; k < 2; k++) ci.DrawRect(new Rect2(body.Position.X + 1f, body.Position.Y + h * (0.3f + 0.45f * k), body.Size.X - 2f, h * 0.1f), new Color(1f, 1f, 1f, 0.75f));
                for (int k = -1; k <= 1; k += 2) ci.DrawRect(new Rect2(c.X + k * w * 0.35f - 2f, body.Position.Y - h * 0.18f, 4f, h * 0.2f), new Color(0.7f, 0.7f, 0.74f));
                if (e.Kind == ExplosiveKind.MethaneTank) ci.DrawColoredPolygon(new[] { c + new Vector2(0f, -h * 0.15f), c + new Vector2(w * 0.3f, h * 0.25f), c + new Vector2(-w * 0.3f, h * 0.25f) }, new Color(1f, 0.85f, 0.2f));
                break;
            }
            case ExplosiveKind.FuelCan or ExplosiveKind.Solvent:
            {
                // 각진 통: 모서리 깎임 · X 무늬(연료) / 나사 뚜껑 + 불꽃 그림(용제) · 손잡이
                var a = c + new Vector2(-w * 0.8f, -h * 0.55f);
                var pts = new[] { a + new Vector2(w * 0.35f, 0f), a + new Vector2(w * 1.6f, 0f), a + new Vector2(w * 1.6f, h * 1.3f), a + new Vector2(0f, h * 1.3f), a + new Vector2(0f, h * 0.35f) };
                ci.DrawColoredPolygon(pts, tint);
                ci.DrawPolyline(pts.Append(pts[0]).ToArray(), line, 1f, true);
                if (e.Kind == ExplosiveKind.FuelCan)
                {
                    ci.DrawLine(a + new Vector2(w * 0.3f, h * 0.45f), a + new Vector2(w * 1.3f, h * 1.15f), dark, 1.2f, true);
                    ci.DrawLine(a + new Vector2(w * 1.3f, h * 0.45f), a + new Vector2(w * 0.3f, h * 1.15f), dark, 1.2f, true);
                    ci.DrawLine(a + new Vector2(w * 0.05f, h * 0.2f), a + new Vector2(-w * 0.25f, -h * 0.15f), new Color(0.3f, 0.3f, 0.3f), 2f, true); // 주둥이
                }
                else
                {
                    ci.DrawRect(new Rect2(a + new Vector2(w * 1.05f, -h * 0.15f), new Vector2(w * 0.35f, h * 0.18f)), new Color(0.85f, 0.85f, 0.85f));
                    ci.DrawColoredPolygon(new[] { c + new Vector2(0f, -h * 0.05f), c + new Vector2(w * 0.25f, h * 0.4f), c + new Vector2(-w * 0.25f, h * 0.4f) }, new Color(1f, 0.6f, 0.1f));
                }
                ci.DrawRect(new Rect2(a + new Vector2(w * 0.6f, -h * 0.12f), new Vector2(w * 0.6f, h * 0.12f)), dark);
                break;
            }
            case ExplosiveKind.BatteryCell or ExplosiveKind.CapacitorPack:
            {
                // 셀 세 개 / 축전기 원통 묶음 (노란 띠 · 번개 표시)
                for (int k = -1; k <= 1; k++)
                {
                    var cc = c + new Vector2(k * w * 0.62f, 0f);
                    if (e.Kind == ExplosiveKind.BatteryCell)
                    {
                        var r = new Rect2(cc.X - w * 0.28f, cc.Y - h * 0.5f, w * 0.56f, h * 1.1f);
                        Gfx.RoundRect(ci, r, tint, 2f, line, 1);
                        ci.DrawRect(new Rect2(r.Position.X, r.Position.Y + h * 0.35f, r.Size.X, h * 0.14f), new Color(0.3f, 0.6f, 1f));
                        ci.DrawRect(new Rect2(cc.X - 1.5f, r.Position.Y - 2.5f, 3f, 2.5f), new Color(0.85f, 0.75f, 0.4f)); // + 단자
                    }
                    else
                    {
                        ci.DrawCircle(cc, w * 0.34f, tint, true, -1f, true);
                        ci.DrawCircle(cc, w * 0.34f, line, false, 1f, true);
                        ci.DrawCircle(cc, w * 0.12f, new Color(0.2f, 0.2f, 0.2f), true, -1f, true);
                    }
                }
                if (e.Kind == ExplosiveKind.CapacitorPack) ci.DrawPolyline(new[] { c + new Vector2(-2f, -h * 0.55f), c + new Vector2(1f, -h * 0.2f), c + new Vector2(-1f, -h * 0.2f), c + new Vector2(2f, h * 0.15f) }, new Color(0.1f, 0.1f, 0.1f), 1.2f, true);
                break;
            }
            case ExplosiveKind.GasCylinder:
            {
                // 땅딸막한 실린더 + 보호 고리(손잡이 구멍)
                var body = new Rect2(c.X - w * 0.7f, c.Y - h * 0.3f, w * 1.4f, h * 1.1f);
                Gfx.RoundRect(ci, body, tint, w * 0.45f, line, 1);
                ci.DrawArc(new Vector2(c.X, body.Position.Y - h * 0.05f), w * 0.55f, Mathf.Pi, Mathf.Tau, 12, new Color(0.45f, 0.45f, 0.48f), 2.5f, true);
                ci.DrawRect(new Rect2(c.X - 2f, body.Position.Y - h * 0.18f, 4f, h * 0.18f), new Color(0.7f, 0.7f, 0.7f));
                ci.DrawLine(body.Position + new Vector2(2f, h * 0.5f), body.Position + new Vector2(body.Size.X - 2f, h * 0.5f), dark, 1f, true);
                break;
            }
            case ExplosiveKind.Dust or ExplosiveKind.Fertilizer:
            {
                // 포대: 위가 묶인 자루 (비료는 두 겹 · 초록 띠) · 날린 가루 구름
                int bags = e.Kind == ExplosiveKind.Fertilizer ? 2 : 1;
                for (int k = 0; k < bags; k++)
                {
                    var cc = c + new Vector2((k - (bags - 1) * 0.5f) * w * 0.9f, k * 2f);
                    var pts = new[] { cc + new Vector2(-w * 0.75f, h * 0.6f), cc + new Vector2(-w * 0.55f, -h * 0.35f), cc + new Vector2(-w * 0.15f, -h * 0.5f), cc + new Vector2(w * 0.15f, -h * 0.5f), cc + new Vector2(w * 0.55f, -h * 0.35f), cc + new Vector2(w * 0.75f, h * 0.6f) };
                    ci.DrawColoredPolygon(pts, tint);
                    ci.DrawPolyline(pts.Append(pts[0]).ToArray(), line, 1f, true);
                    ci.DrawLine(cc + new Vector2(-w * 0.2f, -h * 0.5f), cc + new Vector2(w * 0.2f, -h * 0.5f), new Color(0.4f, 0.3f, 0.2f), 2f, true);
                    if (e.Kind == ExplosiveKind.Fertilizer) ci.DrawRect(new Rect2(cc.X - w * 0.6f, cc.Y + h * 0.05f, w * 1.2f, h * 0.15f), new Color(0.3f, 0.65f, 0.3f));
                }
                if (e.Cloud > 0.05f)
                    for (int k = 0; k < 8; k++)
                    {
                        float ang = _time * 0.6f + k * 0.8f;
                        ci.DrawCircle(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.6f) * T * (0.4f + 0.5f * e.Cloud), T * 0.22f, new Color(0.9f, 0.85f, 0.7f, 0.35f * e.Cloud), true, -1f, true);
                    }
                break;
            }
            case ExplosiveKind.PressureVessel:
            {
                // 둥근 압력 용기 + 계기판(바늘이 압력을 가리킨다) + 관 토막
                ci.DrawCircle(c, h * 0.62f, tint, true, -1f, true);
                ci.DrawCircle(c, h * 0.62f, line, false, 1f, true);
                ci.DrawCircle(c - new Vector2(h * 0.2f, h * 0.2f), h * 0.18f, hi, true, -1f, true);
                var g = c + new Vector2(h * 0.5f, -h * 0.5f);
                ci.DrawCircle(g, h * 0.24f, new Color(0.95f, 0.95f, 0.92f), true, -1f, true);
                float needle = -Mathf.Pi * 0.75f + Mathf.Pi * 1.5f * Mathf.Clamp(0.3f + e.Heat, 0f, 1f);
                ci.DrawLine(g, g + new Vector2(Mathf.Cos(needle), Mathf.Sin(needle)) * h * 0.2f, Palette.Danger, 1f, true);
                ci.DrawRect(new Rect2(c.X - h * 0.9f, c.Y - 2f, h * 0.3f, 4f), new Color(0.6f, 0.6f, 0.62f));
                break;
            }
            case ExplosiveKind.FermentJar:
            {
                // 유리 항아리: 비치는 국물 · 뚜껑 (부풀면 뚜껑이 들리고 거품)
                float bulge = Mathf.Clamp(e.Pressure - 0.5f, 0f, 0.6f);
                var body = new Rect2(c.X - w * (0.7f + bulge * 0.3f), c.Y - h * 0.5f, w * (1.4f + bulge * 0.6f), h * 1.2f);
                Gfx.RoundRect(ci, body, new Color(0.85f, 0.95f, 1f, 0.25f), w * 0.4f, new Color(0.8f, 0.9f, 1f, 0.7f), 1);
                ci.DrawRect(new Rect2(body.Position.X + 2f, body.Position.Y + h * 0.35f, body.Size.X - 4f, h * 0.8f), tint.WithAlpha(0.85f));
                ci.DrawRect(new Rect2(c.X - w * 0.55f, body.Position.Y - 3f - bulge * 6f, w * 1.1f, 4f), new Color(0.75f, 0.3f, 0.25f));
                for (int k = 0; k < 3; k++)
                {
                    float ph = Mathf.PosMod(_time * (0.6f + e.Pressure) + k * 0.33f, 1f);
                    ci.DrawCircle(new Vector2(c.X + (k - 1) * w * 0.4f, body.End.Y - 3f - ph * h * 0.7f), 1.2f, new Color(1f, 1f, 1f, 0.6f * (1f - ph)), true, -1f, true);
                }
                break;
            }
            case ExplosiveKind.Extinguisher:
            {
                // 빨간 소화기: 원통 · 검은 호스 곡선 · 손잡이 레버 · 안전핀 고리
                var body = new Rect2(c.X - w * 0.45f, c.Y - h * 0.45f, w * 0.9f, h * 1.3f);
                Gfx.RoundRect(ci, body, tint, w * 0.4f, line, 1);
                ci.DrawRect(new Rect2(body.Position.X + 1f, c.Y, w * 0.9f - 2f, h * 0.25f), new Color(0.95f, 0.95f, 0.95f, 0.8f));
                ci.DrawLine(new Vector2(c.X - w * 0.1f, body.Position.Y - h * 0.15f), new Vector2(c.X + w * 0.5f, body.Position.Y - h * 0.3f), new Color(0.2f, 0.2f, 0.2f), 2f, true);
                ci.DrawArc(new Vector2(c.X + w * 0.6f, c.Y - h * 0.05f), h * 0.4f, -Mathf.Pi * 0.5f, Mathf.Pi * 0.5f, 10, new Color(0.08f, 0.08f, 0.08f), 1.6f, true);
                ci.DrawCircle(new Vector2(c.X - w * 0.25f, body.Position.Y - h * 0.08f), 1.6f, new Color(0.9f, 0.85f, 0.2f), false, 1f, true);
                break;
            }
            case ExplosiveKind.Aerosol:
            {
                // 작은 캔 셋 (색이 다르다 · 노즐 뚜껑)
                var cols = new[] { tint, new Color("#40c057"), new Color("#fab005") };
                for (int k = 0; k < 3; k++)
                {
                    var cc = c + new Vector2((k - 1) * w * 0.6f, (k % 2) * 2f);
                    var r = new Rect2(cc.X - w * 0.25f, cc.Y - h * 0.4f, w * 0.5f, h * 0.95f);
                    Gfx.RoundRect(ci, r, cols[k], 2f, line, 1);
                    ci.DrawRect(new Rect2(cc.X - w * 0.15f, r.Position.Y - 3f, w * 0.3f, 3f), new Color(0.9f, 0.9f, 0.9f));
                }
                break;
            }
            case ExplosiveKind.RefrigerantCan:
            {
                // 하늘색 원통 + 눈송이
                var body = new Rect2(c.X - w * 0.6f, c.Y - h * 0.45f, w * 1.2f, h * 1.15f);
                Gfx.RoundRect(ci, body, tint, w * 0.3f, line, 1);
                for (int k = 0; k < 3; k++)
                {
                    var ax = new Vector2(Mathf.Cos(k * Mathf.Pi / 3f), Mathf.Sin(k * Mathf.Pi / 3f)) * w * 0.35f;
                    ci.DrawLine(c + new Vector2(0f, h * 0.1f) - ax, c + new Vector2(0f, h * 0.1f) + ax, new Color(1f, 1f, 1f, 0.9f), 1f, true);
                }
                ci.DrawRect(new Rect2(c.X - 2f, body.Position.Y - 3f, 4f, 3f), new Color(0.3f, 0.35f, 0.4f));
                break;
            }
            case ExplosiveKind.SuitO2Pack:
            {
                // 등짐: 흰 팩 + 작은 탱크 둘 + 끈
                var body = new Rect2(c.X - w * 0.75f, c.Y - h * 0.55f, w * 1.5f, h * 1.25f);
                Gfx.RoundRect(ci, body, tint, 4f, line, 1);
                for (int k = -1; k <= 1; k += 2) Gfx.RoundRect(ci, new Rect2(c.X + k * w * 0.35f - w * 0.2f, body.Position.Y + 3f, w * 0.4f, h * 0.9f), new Color(0.6f, 0.75f, 0.85f), w * 0.2f);
                ci.DrawLine(body.Position + new Vector2(2f, h * 0.2f), body.Position + new Vector2(-2f, h * 1.1f), new Color(0.3f, 0.3f, 0.35f), 2f, true);
                ci.DrawCircle(new Vector2(c.X, body.End.Y - 4f), 1.6f, new Color(0.4f, 1f, 0.6f), true, -1f, true);
                break;
            }
            case ExplosiveKind.WeldingGas:
            {
                // 쌍둥이 실린더 (아세틸렌 밤색 + 산소 검정) · 사슬 · 호스 고리
                for (int k = -1; k <= 1; k += 2)
                {
                    var r = new Rect2(c.X + k * w * 0.42f - w * 0.32f, c.Y - h * 0.6f, w * 0.64f, h * 1.45f);
                    Gfx.RoundRect(ci, r, k < 0 ? tint : new Color(0.12f, 0.12f, 0.14f), w * 0.3f, line, 1);
                    ci.DrawRect(new Rect2(r.Position.X + r.Size.X * 0.3f, r.Position.Y - 3f, r.Size.X * 0.4f, 3f), new Color(0.7f, 0.7f, 0.7f));
                }
                ci.DrawLine(c + new Vector2(-w * 0.8f, 0f), c + new Vector2(w * 0.8f, 0f), new Color(0.75f, 0.75f, 0.7f), 1f, true);
                ci.DrawArc(c + new Vector2(0f, -h * 0.65f), w * 0.5f, Mathf.Pi, Mathf.Tau, 10, new Color(0.15f, 0.4f, 0.15f), 1.5f, true);
                break;
            }
            case ExplosiveKind.Propellant:
            {
                // 누운 긴 탱크 + 양 끝 노랑 · 검정 빗금
                var body = new Rect2(c.X - h * 0.95f, c.Y - h * 0.35f, h * 1.9f, h * 0.75f);
                Gfx.RoundRect(ci, body, new Color(0.75f, 0.77f, 0.8f), h * 0.35f, line, 1);
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < 3; k++)
                    {
                        float x = c.X + side * h * (0.55f + k * 0.12f);
                        ci.DrawLine(new Vector2(x, body.Position.Y + 1f), new Vector2(x + 3f * side, body.End.Y - 1f), k % 2 == 0 ? tint : new Color(0.1f, 0.1f, 0.1f), 2.5f, true);
                    }
                ci.DrawRect(new Rect2(c.X - 2f, body.Position.Y - 3f, 4f, 3f), new Color(0.5f, 0.5f, 0.5f));
                break;
            }
            case ExplosiveKind.Charge:
            {
                // 폭약: 올리브빛 벽돌 · 감긴 테이프 · 전선 · 기폭 장치(숫자판 · LED)
                var body = new Rect2(c.X - w * 0.85f, c.Y - h * 0.35f, w * 1.7f, h * 0.8f);
                ci.DrawRect(body, tint);
                ci.DrawRect(body, line, false, 1f);
                ci.DrawRect(new Rect2(body.Position.X + w * 0.5f, body.Position.Y, w * 0.18f, body.Size.Y), new Color(0.2f, 0.2f, 0.2f, 0.8f));
                var det = new Rect2(c.X + w * 0.2f, body.Position.Y - h * 0.32f, w * 0.7f, h * 0.32f);
                ci.DrawRect(det, new Color(0.15f, 0.15f, 0.17f));
                bool armed = e.Armed || e.Primed;
                float blink = armed ? (Mathf.PosMod(_time * (e.Primed ? 6f : 1.5f), 1f) < 0.5f ? 1f : 0.2f) : 0.15f;
                ci.DrawCircle(det.Position + new Vector2(det.Size.X - 3f, det.Size.Y * 0.5f), 1.8f, new Color(1f, 0.2f, 0.2f, blink), true, -1f, true);
                ci.DrawRect(new Rect2(det.Position + new Vector2(2f, 2f), new Vector2(det.Size.X * 0.55f, det.Size.Y - 4f)), new Color(0.2f, 0.9f, 0.4f, armed ? 0.9f : 0.25f));
                ci.DrawPolyline(new[] { det.Position + new Vector2(0f, det.Size.Y), c + new Vector2(-w * 0.6f, -h * 0.5f), c + new Vector2(-w * 1.1f, -h * 0.2f) }, new Color(0.85f, 0.2f, 0.2f), 1f, true);
                break;
            }
            case ExplosiveKind.Flare:
            {
                // 신호탄 상자: 뚜껑 열린 상자 + 붉은 막대 넷
                var box = new Rect2(c.X - w * 0.9f, c.Y - h * 0.15f, w * 1.8f, h * 0.75f);
                ci.DrawRect(box, new Color(0.25f, 0.27f, 0.3f));
                ci.DrawRect(box, line, false, 1f);
                for (int k = 0; k < 4; k++) ci.DrawLine(new Vector2(box.Position.X + w * (0.35f + k * 0.38f), box.Position.Y + 2f), new Vector2(box.Position.X + w * (0.3f + k * 0.38f), box.Position.Y - h * 0.5f), tint, 3f, true);
                break;
            }
            case ExplosiveKind.Lubricant:
            {
                // 드럼 (위에서 본 동그라미 · 테 두 줄 · 마개 · 흘러내린 기름)
                ci.DrawCircle(c, h * 0.62f, tint, true, -1f, true);
                ci.DrawCircle(c, h * 0.62f, new Color(0.5f, 0.5f, 0.55f), false, 1.5f, true);
                ci.DrawCircle(c, h * 0.42f, new Color(0.5f, 0.5f, 0.55f, 0.6f), false, 1f, true);
                ci.DrawCircle(c + new Vector2(h * 0.25f, -h * 0.2f), h * 0.1f, new Color(0.75f, 0.7f, 0.3f), true, -1f, true);
                ci.DrawCircle(c + new Vector2(h * 0.55f, h * 0.55f), h * 0.18f, new Color(0.15f, 0.12f, 0.05f, 0.6f), true, -1f, true);
                break;
            }
            case ExplosiveKind.PortableBattery:
                break;
        }
    }

    /// <summary>상태: 달아오름(아지랑이 · 붉은 빛) · 쉭(가스 줄기 · 깜빡이는 테) · 컴퓨터가 읽은 위험(노란 삼각형) · 들고 감.</summary>
    private void PaintItemState(CanvasItem ci, Vector2 c, float h, Explosive e)
    {
        if (e.Heat > 0.3f)
        {
            float a = Mathf.Clamp((e.Heat - 0.3f) * 1.6f, 0f, 1f);
            ci.DrawCircle(c, h * 1.1f, new Color(1f, 0.35f, 0.1f, 0.22f * a), true, -1f, true);
            for (int k = 0; k < 3; k++)
            {
                float ph = Mathf.PosMod(_time * 1.4f + k * 0.33f, 1f);
                var x0 = c + new Vector2((k - 1) * h * 0.5f, -h * (0.9f + ph * 1.2f));
                var pts = new Vector2[5];
                for (int q = 0; q < 5; q++) pts[q] = x0 + new Vector2(Mathf.Sin(_time * 8f + q + k) * 2f, -q * 3f);
                ci.DrawPolyline(pts, new Color(1f, 0.85f, 0.7f, 0.5f * a * (1f - ph)), 1f, true);
            }
        }
        if (e.Primed)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 14f);
            ci.DrawArc(c, h * 1.25f + 2f * pulse, 0f, Mathf.Tau, 20, Palette.Danger.WithAlpha(0.6f + 0.4f * pulse), 1.6f, true);
            if (e.Spec.Hiss)
                for (int k = 0; k < 6; k++)
                {
                    float ph = Mathf.PosMod(_time * 5f + k * 0.17f, 1f);
                    var dir = new Vector2(0.4f + 0.2f * Mathf.Sin(k), -1f).Normalized();
                    var p0 = c + new Vector2(0f, -h * 0.8f);
                    ci.DrawLine(p0 + dir * h * ph * 1.6f, p0 + dir * h * (ph * 1.6f + 0.3f), new Color(0.92f, 0.95f, 1f, 0.7f * (1f - ph)), 1.2f, true);
                }
            if (e.Kind is ExplosiveKind.BatteryCell or ExplosiveKind.PortableBattery)
                for (int k = 0; k < 3; k++) ci.DrawCircle(c + new Vector2((k - 1) * 4f, -h * (1f + Mathf.PosMod(_time + k * 0.3f, 1f))), 3f, new Color(0.55f, 0.6f, 0.35f, 0.4f), true, -1f, true);
            if (e.Kind == ExplosiveKind.Charge)
            {
                float left = Mathf.Max(0f, (e.FuseAt - _world.Tick) / (float)SimTime.TicksPerSecond);
                Gfx.TextCentered(ci, Fonts.Bold, c + new Vector2(0f, -h * 1.8f), $"{Mathf.CeilToInt(left / 10f)}", 12, Palette.Danger);
            }
        }
        if (e.Risk >= 0.5f && _world.Tick - e.WarnedAt < SimTime.Hours(6))
        {
            // 주 컴퓨터가 읽은 위험: 노란 삼각형
            var t0 = c + new Vector2(h * 0.9f, -h * 1.1f);
            ci.DrawColoredPolygon(new[] { t0 + new Vector2(0f, -5f), t0 + new Vector2(5f, 4f), t0 + new Vector2(-5f, 4f) }, new Color(1f, 0.82f, 0.2f, 0.95f));
            ci.DrawLine(t0 + new Vector2(0f, -2f), t0 + new Vector2(0f, 1.5f), new Color(0.1f, 0.1f, 0.1f), 1.2f, true);
        }
        if (e.Moved && !e.Primed && e.Heat < 0.3f) ci.DrawArc(c, h * 1.15f, 0f, Mathf.Tau, 16, Palette.Good.WithAlpha(0.35f), 1f, true); // 안전한 곳으로 옮겨 둔 것
    }

    /// <summary>터진 잔해: 꽃잎처럼 찢겨 벌어진 통 · 그을린 바닥.</summary>
    private void PaintWreckItem(CanvasItem ci, Vector2 c, float s, Explosive e)
    {
        float h = s * T * 0.5f;
        ci.DrawCircle(c, h * 0.9f, new Color(0.03f, 0.03f, 0.03f, 0.5f), true, -1f, true);
        var tint = ItemTint(e.Kind).Darkened(0.5f);
        for (int k = 0; k < 5; k++)
        {
            float ang = k * Mathf.Tau / 5f + e.Id;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var perp = new Vector2(-dir.Y, dir.X);
            ci.DrawColoredPolygon(new[] { c + perp * 2f, c + dir * h * 0.9f + perp * 1f, c + dir * h * 1.05f, c - perp * 2f }, tint);
        }
    }
}

/// <summary>v16.13 폭발음: 종류마다 다른 소리(쿵 · 쨍 · 펑 · 쉭 · 지직) · 거리 · 벽 너머면 먹먹하게.</summary>
public partial class SoundSystem
{
    private readonly Dictionary<(int sound, bool muffled), AudioStreamWav> _blastWav = new();
    private int _lastBlast;

    private static int SoundOf(BlastKind k) => k switch
    {
        BlastKind.Charge or BlastKind.Hydrogen or BlastKind.Acetylene => 0, // 날카로운 쾅
        BlastKind.Gas or BlastKind.Propellant or BlastKind.Combustion or BlastKind.Nitrate or BlastKind.Oxygen or BlastKind.Generic or BlastKind.Furnace => 1, // 깊은 쿵 + 우르릉
        BlastKind.Fuel or BlastKind.Grease or BlastKind.Dust => 2, // 훅 (불길)
        BlastKind.Steam or BlastKind.ColdGas or BlastKind.Refrigerant or BlastKind.Powder => 3, // 쉭 터짐
        BlastKind.Battery or BlastKind.Arc or BlastKind.Flare => 4, // 지직 · 탁탁
        _ => 5, // 펑 (작은 것)
    };

    private void ProcessBlasts(World w, bool quiet)
    {
        var recent = w.Blast.Recent;
        if (recent.Count == 0 || recent[^1].Id == _lastBlast) return;
        foreach (var rec in recent)
        {
            if (rec.Id <= _lastBlast) continue;
            _lastBlast = rec.Id;
            if (quiet || !Settings.Effects || w.Tick - rec.Tick > SimTime.Minutes(2)) continue;
            int walls = WallsBetween(w, rec.At);
            bool muffled = walls > 0;
            var key = (SoundOf(rec.Kind), muffled);
            if (!_blastWav.TryGetValue(key, out var wav)) _blastWav[key] = wav = Make(BlastSound(key.Item1, muffled));
            float loud = Math.Clamp(rec.Power * rec.Spec.Loud, 0.05f, 1.3f);
            float gain = MathF.Max(0.25f, Gain(rec.At.Center)) * (muffled ? MathF.Max(0.35f, 1f - 0.2f * walls) : 1f);
            Play2D(wav, rec.At.Center, -10f + 12f * loud, gain, 1.15f - 0.3f * Math.Clamp(rec.Power, 0f, 1f));
        }
    }

    /// <summary>화면 가운데(듣는 자리)와 폭발 사이 벽 칸 수 (읽기만).</summary>
    private int WallsBetween(World w, Cell at)
    {
        var size = GetViewport().GetVisibleRect().Size;
        var global = GetViewport().GetCanvasTransform().AffineInverse() * (size * 0.5f);
        var local = _main.ShipView.ToLocal(global);
        var from = ShipView.CellAtPx(local);
        var g = w.Ship.Grid;
        int n = 0;
        int steps = (int)MathF.Max(MathF.Abs(at.X - from.X), MathF.Abs(at.Y - from.Y));
        Cell last = from;
        for (int i = 1; i <= steps && i < 80; i++)
        {
            float t = i / (float)steps;
            var c = new Cell((int)MathF.Round(from.X + (at.X - from.X) * t), (int)MathF.Round(from.Y + (at.Y - from.Y) * t));
            if (c == last) continue;
            last = c;
            if (g.Kind(c) == TileKind.Wall || g.Kind(c) == TileKind.Door && w.Ship.DoorAt(c) is Door d && d.Openness < 0.5f) n++;
        }
        return n;
    }

    /// <summary>폭발음 파형: 0 날카로운 쾅 · 1 깊은 쿵 · 2 불길 훅 · 3 쉭 터짐 · 4 지직 · 5 펑. 먹먹하면 높은 소리를 깎는다.</summary>
    private static float[] BlastSound(int kind, bool muffled)
    {
        float len = kind switch { 1 => 2.2f, 2 => 1.6f, 3 => 1.8f, 4 => 1.4f, 5 => 0.5f, _ => 1.2f };
        int n = (int)(Rate * len);
        var s = new float[n];
        var rng = new Random(91 + kind);
        float low = 0f, lp = 0f, crackle = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            low += 0.06f * (noise - low);
            float x = kind switch
            {
                0 => 0.9f * noise * MathF.Exp(-t * 18f) + 0.8f * MathF.Sin(t * MathF.Tau * (70f - 30f * t)) * MathF.Exp(-t * 4f) + 0.4f * low * 3f * MathF.Exp(-t * 3f),
                1 => 0.9f * MathF.Sin(t * MathF.Tau * (48f - 18f * t)) * MathF.Exp(-t * 2.2f) + 0.6f * low * 4f * MathF.Exp(-t * 1.6f) + 0.3f * noise * MathF.Exp(-t * 12f),
                2 => 0.7f * low * 5f * MathF.Min(1f, t * 12f) * MathF.Exp(-t * 2.4f) + 0.3f * MathF.Sin(t * MathF.Tau * 60f) * MathF.Exp(-t * 3f),
                3 => 0.7f * noise * MathF.Min(1f, t * 40f) * MathF.Exp(-t * 2.5f) + 0.4f * MathF.Sin(t * MathF.Tau * 55f) * MathF.Exp(-t * 6f),
                4 => (crackle = rng.NextDouble() < 0.012 * MathF.Exp(-t * 1.5f) ? 1f : crackle * 0.9f) * noise + 0.4f * noise * MathF.Exp(-t * 14f) + 0.3f * MathF.Sin(t * MathF.Tau * 90f) * MathF.Exp(-t * 5f),
                _ => 0.8f * noise * MathF.Exp(-t * 22f) + 0.5f * MathF.Sin(t * MathF.Tau * 140f) * MathF.Exp(-t * 14f),
            };
            if (muffled) { lp += 0.05f * (x - lp); x = lp * 2.2f; }
            s[i] = Math.Clamp(0.75f * x, -1f, 1f);
        }
        return s;
    }
}
