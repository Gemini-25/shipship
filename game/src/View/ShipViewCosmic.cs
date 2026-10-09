using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v18.13 우주 대재난 화면. 재난마다 다른 그림 (실루엣 · 무늬 · 움직임):
//   하늘(배경 층, 화면 좌표) — 다가오는 것 · 본 사건 · 남은 성운 / 앞 층 — 섬광 · 지지직 · 붉은 안개 · 서리 · 충격파 테 /
//   배 위(세계 좌표) — 방사선 반짝임 · 물벽 · 꺼 둔 설비 · 묶어 둔 방 · 봉쇄 · 충격파 앞머리 · 소행성 접근 · 자기력선 · 조석 화살.
// 그리기는 읽기만 한다 (결정론). 바깥 시점(배 전체로 물러나기)은 설정으로 끈다 (user://cosmic.cfg).

/// <summary>재난 서른 가지의 그림 (하늘 · 작은 아이콘이 같은 붓을 쓴다).</summary>
public static class CosmicArt
{
    public static float N(int i, int j = 0)
    {
        uint h = unchecked((uint)(i * 374761393 + j * 668265263 + 1442695041));
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        h ^= h >> 16;
        return (h & 0xffffff) / 16777216f;
    }

    public static Vector2 P(float a, float r) => new(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
    public static Color A(Color c, float a) => new(c.R, c.G, c.B, Mathf.Clamp(a, 0f, 1f));

    public static void Glow(CanvasItem ci, Vector2 c, float r, Color col, int layers = 6, float alpha = 0.1f)
    {
        for (int i = 0; i < layers; i++) ci.Circle(c, r * (1f - i / (float)layers), A(col, alpha), true, -1f, true);
    }

    public static void Star(CanvasItem ci, Vector2 c, float r, Color col, float spikes = 1f)
    {
        Glow(ci, c, r * 3.2f, col, 5, 0.07f);
        ci.Circle(c, r, col, true, -1f, true);
        ci.Circle(c, r * 0.5f, Colors.White, true, -1f, true);
        if (spikes <= 0f) return;
        for (int i = 0; i < 4; i++)
        {
            var d = P(i * Mathf.Pi / 2f + Mathf.Pi / 4f * (i % 2), r * 4f * spikes);
            ci.DrawLine(c - d, c + d, A(col, 0.35f), 1.2f, true);
        }
    }

    public static Vector2[] Blob(Vector2 c, float r, int seed, int n = 12, float rough = 0.3f, float rot = 0f, float sy = 1f)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = rot + i * Mathf.Tau / n;
            float rr = r * (1f - rough + 2f * rough * N(seed, i));
            pts[i] = c + new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr * sy);
        }
        return pts;
    }

    public static Vector2[] Ellipse(Vector2 c, float rx, float ry, float rot, int n, float a0 = 0f, float a1 = Mathf.Tau)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = a0 + (a1 - a0) * i / (n - 1f);
            var p = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
            pts[i] = c + p.Rotated(rot);
        }
        return pts;
    }

    private static void Ring(CanvasItem ci, Vector2 c, float r, Color col, float width, int seed, float wobble = 0.06f, int n = 64)
    {
        var pts = new Vector2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float a = i * Mathf.Tau / n;
            pts[i] = c + P(a, r * (1f + wobble * (N(seed, i % n) - 0.5f) * 2f));
        }
        ci.Polyline(pts, col, width, true);
    }

    private static void Bolt(CanvasItem ci, Vector2 from, Vector2 to, Color col, float width, int seed, int segs = 7, float jag = 0.18f)
    {
        var pts = new Vector2[segs + 1];
        var d = to - from;
        var nrm = new Vector2(-d.Y, d.X).Normalized();
        for (int i = 0; i <= segs; i++)
        {
            float u = i / (float)segs;
            float off = i == 0 || i == segs ? 0f : (N(seed, i) - 0.5f) * 2f * jag * d.Length();
            pts[i] = from + d * u + nrm * off;
        }
        ci.Polyline(pts, col, width, true);
    }

    /// <summary>
    /// 재난 하나. c: 근원 자리 · r: 크기 · near: 다가온 정도 0~1 · hit: 본 사건 세기 0~1 · area: 화면 전체 효과가 닿는 곳 · toward: 배 쪽 (화면 가운데).
    /// </summary>
    public static void Draw(CanvasItem ci, CosmicKind k, Vector2 c, float r, float t, float near, float hit, Rect2 area, Vector2 toward, int seed)
    {
        var spec = CosmicCatalog.Spec(k);
        var a = new Color(spec.Hex);
        var b = new Color(spec.Hex2);
        float s = r * (0.35f + 0.65f * near);
        var dir = (toward - c).LengthSquared() > 1f ? (toward - c).Normalized() : Vector2.Right;
        var side = new Vector2(-dir.Y, dir.X);
        float diag = area.Size.Length();
        switch (k)
        {
            case CosmicKind.Supernova:
            {
                // 무너지기 전: 맥박 치는 푸른 흰 별 → 섬광 → 퍼지는 분홍·하늘빛 고리
                float pulse = 0.8f + 0.2f * Mathf.Sin(t * (2f + 6f * near));
                Star(ci, c, s * 0.18f * pulse, a.Lerp(new Color("#bfe3ff"), 0.4f), 0.6f + near);
                if (hit > 0f)
                {
                    Glow(ci, c, s * (1.2f + 2.5f * hit), Colors.White, 8, 0.08f * hit);
                    for (int i = 0; i < 3; i++)
                    {
                        float rr = s * (0.6f + 0.5f * i) * (1f + 0.15f * Mathf.Sin(t * 0.7f + i));
                        Ring(ci, c, rr, A(i % 2 == 0 ? b : new Color("#7fe3ff"), 0.5f * hit), 2.5f - i * 0.6f, seed + i, 0.12f);
                    }
                    for (int i = 0; i < 24; i++) // 방사선 빛살
                    {
                        float ang = i * Mathf.Tau / 24f + t * 0.05f;
                        ci.DrawLine(c + P(ang, s * 0.4f), c + P(ang, s * (1.5f + 1.5f * N(seed, i)) * (0.5f + hit)), A(b, 0.12f * hit), 1.5f, true);
                    }
                }
                break;
            }
            case CosmicKind.GammaBurst:
            {
                // 보랏빛 한 줄기 — 배를 꿰뚫고 반대편 끝까지
                float flick = 0.7f + 0.3f * N((int)(t * 30f), seed);
                ci.Circle(c, s * 0.08f * (0.6f + flick * 0.4f), a, true, -1f, true);
                Glow(ci, c, s * 0.3f, a, 4, 0.08f + 0.1f * near);
                if (hit > 0f)
                {
                    var end = c + dir * diag * 1.2f;
                    ci.DrawLine(c, end, A(a, 0.12f * hit), 34f * hit, true);
                    ci.DrawLine(c, end, A(a, 0.35f * hit), 12f * hit, true);
                    ci.DrawLine(c + side * (N((int)(t * 40f)) - 0.5f) * 3f, end, A(Colors.White, 0.8f * hit * flick), 3f, true);
                    for (int i = 0; i < 16; i++) // 꼬리 잔광 알갱이
                    {
                        float u = Mathf.PosMod(N(seed, i) + t * 0.3f, 1f);
                        ci.Circle(c + dir * diag * u + side * (N(seed, i + 40) - 0.5f) * 30f, 1.5f, A(b, 0.6f * hit), true, -1f, true);
                    }
                }
                break;
            }
            case CosmicKind.SuperFlare:
            {
                // 커다란 주황 별 가장자리 · 쌀알 무늬 · 솟구치는 플라스마 고리
                float R = s * 1.1f;
                ci.Circle(c, R, a.Darkened(0.15f), true, -1f, true);
                for (int i = 0; i < 26; i++) ci.Circle(c + P(N(seed, i) * Mathf.Tau, R * N(seed, i + 30) * 0.92f), R * 0.08f, A(b, 0.35f), true, -1f, true);
                Glow(ci, c, R * 1.5f, a, 5, 0.06f);
                for (int i = 0; i < 3; i++)
                {
                    float ang = dir.Angle() + (i - 1) * 0.45f + 0.08f * Mathf.Sin(t * 0.8f + i);
                    float h = R * (0.35f + 0.5f * near + 0.6f * hit) * (0.8f + 0.3f * N(seed, i + 7));
                    var foot1 = c + P(ang - 0.12f, R);
                    var foot2 = c + P(ang + 0.12f, R);
                    var top = c + P(ang, R + h);
                    var pts = new Vector2[16];
                    for (int j = 0; j < 16; j++)
                    {
                        float u = j / 15f;
                        pts[j] = (1 - u) * (1 - u) * foot1 + 2 * u * (1 - u) * (top + P(ang + Mathf.Pi / 2f, h * 0.4f)) + u * u * foot2;
                    }
                    ci.Polyline(pts, A(b, 0.85f), 3f, true);
                    ci.Polyline(pts, A(Colors.White, 0.4f), 1f, true);
                }
                if (hit > 0f) Ring(ci, c, R * (1.3f + 0.8f * Mathf.PosMod(t * 0.3f, 1f)), A(a, 0.5f * hit), 3f, seed, 0.03f);
                break;
            }
            case CosmicKind.CoronalMass:
            {
                // 작은 별에서 부풀어 오는 주황 거품 → 초록·보라 오로라
                Star(ci, c, s * 0.12f, a, 0.5f);
                float bubble = s * (0.3f + 1.3f * near);
                var center = c + dir * bubble * 0.8f;
                for (int i = 0; i < 4; i++) ci.Poly(Blob(center, bubble * (1f - i * 0.18f), seed + i, 18, 0.12f, t * 0.05f), A(a, 0.07f));
                for (int i = 0; i < 7; i++) // 실가닥
                {
                    float ang = dir.Angle() + (i - 3) * 0.25f;
                    ci.Arc(center - dir * bubble * 0.2f, bubble * (0.7f + 0.05f * i), ang - 0.6f, ang + 0.6f, 12, A(a.Lightened(0.2f), 0.25f), 1.2f, true);
                }
                if (hit > 0f)
                    for (int band = 0; band < 3; band++)
                    {
                        var col = band == 1 ? b : new Color("#c27cff");
                        var pts = new Vector2[30];
                        float baseY = area.Position.Y + area.Size.Y * (0.15f + 0.2f * band);
                        for (int j = 0; j < 30; j++)
                        {
                            float x = area.Position.X + area.Size.X * j / 29f;
                            pts[j] = new Vector2(x, baseY + Mathf.Sin(x * 0.01f + t * 0.6f + band) * 30f);
                        }
                        ci.Polyline(pts, A(col, 0.25f * hit), 18f, true);
                        ci.Polyline(pts, A(col, 0.5f * hit), 2f, true);
                    }
                break;
            }
            case CosmicKind.PulsarBeam:
            {
                // 작게 빛나는 점 · 돌아가는 두 줄기 등대 빔
                ci.Circle(c, s * 0.07f, b, true, -1f, true);
                Glow(ci, c, s * 0.25f, a, 4, 0.1f);
                float ang = t * 2.6f;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    var d = P(ang, 1f) * sgn;
                    float len = s * (1.4f + 2f * hit) + (hit > 0f ? diag * 0.6f : 0f);
                    var tip = c + d * len;
                    var w = new Vector2(-d.Y, d.X) * len * 0.06f;
                    ci.Poly(new[] { c, tip + w, tip - w }, A(a, 0.18f + 0.25f * hit));
                    ci.DrawLine(c, tip, A(b, 0.5f), 1.5f, true);
                }
                float facing = Mathf.Abs(P(ang, 1f).Dot(dir));
                if (facing > 0.97f && hit > 0f) Glow(ci, toward, 60f, a, 4, 0.12f * hit); // 째깍 — 배를 쓸고 간다
                break;
            }
            case CosmicKind.MagnetarStorm:
            {
                // 자줏빛 별 · 쌍극 자기력선 (일렁이며 그 위를 알갱이가 흐른다)
                Star(ci, c, s * 0.1f, a, 0.4f);
                float L = s * (0.6f + 1.4f * hit);
                float rot = Mathf.Sin(t * 0.3f) * 0.2f;
                for (int i = 1; i <= 5; i++)
                {
                    float Li = L * i / 5f;
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        var pts = new Vector2[24];
                        for (int j = 0; j < 24; j++)
                        {
                            float th = 0.15f + (Mathf.Pi - 0.3f) * j / 23f;
                            float rr = Li * Mathf.Sin(th) * Mathf.Sin(th) * (1f + 0.05f * Mathf.Sin(t * 3f + i + j * 0.4f) * hit);
                            pts[j] = c + new Vector2(sgn * rr * Mathf.Sin(th), rr * Mathf.Cos(th)).Rotated(rot);
                        }
                        ci.Polyline(pts, A(i % 2 == 0 ? a : b, 0.25f + 0.25f * hit), 1.4f, true);
                        int q = (int)(Mathf.PosMod(t * 8f + i * 3, 24f));
                        ci.Circle(pts[q], 2f, A(Colors.White, 0.6f), true, -1f, true);
                    }
                }
                break;
            }
            case CosmicKind.NeutronStar:
            {
                // 작고 시린 푸른 점 · 아인슈타인 고리 · 일그러진 별빛
                ci.Circle(c, s * 0.05f, a, true, -1f, true);
                Glow(ci, c, s * 0.2f, b, 5, 0.1f + 0.1f * Mathf.Abs(Mathf.Sin(t * 5f)));
                ci.Arc(c, s * 0.42f, 0f, Mathf.Tau, 48, A(a, 0.45f), 1.5f, true);
                for (int i = 0; i < 6; i++)
                {
                    float ang = N(seed, i) * Mathf.Tau;
                    ci.Arc(c, s * (0.5f + 0.25f * N(seed, i + 9)), ang, ang + 0.25f, 6, A(Colors.White, 0.6f), 1.2f, true); // 렌즈에 늘어진 별
                }
                if (hit > 0f) for (int i = 0; i < 3; i++) Ring(ci, toward, 40f + 30f * i + 10f * Mathf.Sin(t * 2f), A(b, 0.15f * hit), 1.5f, seed + i, 0.02f);
                break;
            }
            case CosmicKind.BlackHoleTide:
            {
                // 검은 원반 · 기울어진 주황 강착 고리 · 광자 고리 · 휘어 도는 별빛
                float R = s * 0.32f;
                for (int i = 0; i < 4; i++)
                {
                    var ell = Ellipse(c, R * (2.6f - 0.3f * i), R * (0.55f - 0.06f * i), -0.25f, 48);
                    ci.Polyline(ell, A(b.Lerp(new Color("#fff1c2"), i / 4f), 0.35f + 0.1f * i), 3f - 0.5f * i, true);
                }
                ci.Circle(c, R, new Color("#020203"), true, -1f, true);
                ci.Arc(c, R * 1.08f, 0f, Mathf.Tau, 48, A(new Color("#ffd27a"), 0.8f), 1.5f, true);
                var top = Ellipse(c, R * 1.6f, R * 1.3f, -0.25f, 24, Mathf.Pi * 1.05f, Mathf.Pi * 1.95f);
                ci.Polyline(top, A(b, 0.6f), 2f, true);
                for (int i = 0; i < 18; i++)
                {
                    float ang = N(seed, i) * Mathf.Tau + t * (0.4f / (1f + N(seed, i + 3) * 3f));
                    float rr = R * (1.8f + 2.5f * N(seed, i + 20));
                    ci.Arc(c, rr, ang, ang + 0.3f * (R * 3f / rr), 5, A(Colors.White, 0.5f), 1f, true);
                }
                if (hit > 0f) for (int i = 0; i < 5; i++) ci.DrawLine(toward - dir * (30f + 10f * i), toward - dir * (70f + 10f * i), A(b, 0.2f * hit), 1f, true);
                break;
            }
            case CosmicKind.RedGiantShell:
            {
                // 화면 가장자리를 채우는 붉은 거성 표면 · 대류 덩어리 → 붉은 안개 (앞 층)
                float R = s * 2.4f;
                ci.Circle(c, R, A(a.Darkened(0.35f), 0.85f), true, -1f, true);
                for (int i = 0; i < 30; i++)
                {
                    var p = c + P(N(seed, i) * Mathf.Tau + t * 0.01f, R * Mathf.Sqrt(N(seed, i + 50)) * 0.95f);
                    ci.Poly(Blob(p, R * 0.09f, seed + i, 8, 0.3f, t * 0.1f), A(b, 0.25f + 0.15f * Mathf.Sin(t + i)));
                }
                Glow(ci, c, R * 1.3f, a, 6, 0.05f + 0.05f * hit);
                break;
            }
            case CosmicKind.BinaryEclipse:
            {
                // 큰 노란 별 앞을 작은 푸른 별이 지난다 → 하늘이 어두워지고 서리 (앞 층)
                var big = c;
                Star(ci, big, s * 0.3f, b, 0.3f);
                float u = Mathf.Clamp(near * 1.1f, 0f, 1f);
                var small = big + side * s * 0.6f * (1f - 2f * u);
                ci.Circle(small, s * 0.26f, A(new Color("#0a0f20"), 0.92f), true, -1f, true);
                ci.Arc(small, s * 0.27f, 0f, Mathf.Tau, 32, A(a, 0.9f), 2f, true);
                Glow(ci, small, s * 0.4f, a, 3, 0.08f);
                break;
            }
            case CosmicKind.WolfRayetWind:
            {
                // 청백 별 · 겹 껍질 · 비스듬히 흐르는 바람 줄기
                Star(ci, c, s * 0.1f, a, 0.8f);
                for (int i = 1; i <= 3; i++) Ring(ci, c, s * 0.3f * i, A(a, 0.25f / i), 1.5f, seed + i, 0.1f);
                int n = 20 + (int)(30 * hit);
                for (int i = 0; i < n; i++)
                {
                    float u = Mathf.PosMod(N(seed, i) + t * (0.15f + 0.2f * hit), 1f);
                    var p0 = c + dir * (s * 0.3f + u * (s * 1.5f + diag * hit * 0.8f)) + side * (N(seed, i + 70) - 0.5f) * s * (1f + 2f * hit);
                    ci.DrawLine(p0, p0 + dir * 18f, A(b, 0.4f), 1.2f, true);
                }
                break;
            }
            case CosmicKind.BigAsteroid:
            {
                // 다가오는 거대한 바위 · 크레이터 · 배로 오는 궤적 (비키면 휜다)
                float R = s * 0.45f;
                var rock = Blob(c, R, seed, 16, 0.22f, t * 0.05f);
                ci.Poly(rock, a.Darkened(0.2f));
                ci.Polyline(rock.Append(rock[0]).ToArray(), A(b, 0.5f), 1.5f, true);
                for (int i = 0; i < 6; i++)
                {
                    var p = c + P(N(seed, i + 3) * Mathf.Tau + t * 0.05f, R * 0.6f * N(seed, i + 11));
                    ci.Circle(p, R * (0.08f + 0.1f * N(seed, i)), a.Darkened(0.45f), true, -1f, true);
                    ci.Arc(p, R * (0.08f + 0.1f * N(seed, i)), -2.4f, -0.6f, 6, A(b, 0.6f), 1f, true);
                }
                ci.Arc(c, R * 1.02f, dir.Angle() - 1.2f, dir.Angle() + 1.2f, 16, A(b, 0.7f), 2f, true); // 볕 받는 쪽
                ci.DrawDashedLine(c + dir * R * 1.2f, toward, A(Palette.Danger, 0.5f * near), 1.5f, 8f);
                break;
            }
            case CosmicKind.CometCore:
            {
                // 혜성 핵 · 곧은 푸른 이온 꼬리 · 휜 먼지 꼬리 · 분출 줄기
                float R = s * 0.1f;
                var away = -dir;
                for (int i = 0; i < 18; i++)
                {
                    float u = i / 17f;
                    ci.DrawLine(c + away * s * 2.4f * u, c + away * s * 2.4f * (u + 0.06f), A(a, 0.5f * (1f - u)), 3f * (1f - u) + 1f, true);
                    var dust = c + away.Rotated(0.35f * u) * s * 1.8f * u + side * s * 0.3f * u * u;
                    ci.Circle(dust, R * (0.6f + 2f * u), A(b, 0.08f * (1f - u)), true, -1f, true);
                }
                Glow(ci, c, R * 4f, a, 5, 0.1f);
                ci.Poly(Blob(c, R, seed, 9, 0.35f, t * 0.2f), new Color("#6b7380"));
                for (int i = 0; i < 4; i++)
                {
                    float ang = dir.Angle() + (N(seed, i) - 0.5f) * 1.2f;
                    float pl = 0.5f + 0.5f * Mathf.Sin(t * 4f + i * 2f);
                    ci.DrawLine(c, c + P(ang, R * (2f + 3f * pl)), A(Colors.White, 0.6f * pl), 1.2f, true);
                }
                if (hit > 0f) for (int i = 0; i < 40; i++) { float u = Mathf.PosMod(N(seed, i) - t * 0.5f, 1f); var p = area.Position + new Vector2(u * area.Size.X, N(seed, i + 90) * area.Size.Y); ci.DrawLine(p, p - dir * 10f, A(a, 0.5f * hit), 1.5f, true); }
                break;
            }
            case CosmicKind.PlanetRing:
            {
                // 줄무늬 행성 · 화면을 가르는 고리 띠 · 빽빽한 얼음 알갱이
                float R = s * 0.9f;
                ci.Circle(c, R, a.Darkened(0.1f), true, -1f, true);
                for (int i = -4; i <= 4; i++)
                {
                    float y = i * R * 0.2f;
                    float hw = Mathf.Sqrt(Mathf.Max(0f, R * R - y * y));
                    ci.DrawLine(c + new Vector2(-hw, y), c + new Vector2(hw, y), A(b, 0.5f), R * 0.07f, true);
                }
                var ring = Ellipse(c, R * 2.3f, R * 0.35f, -0.2f, 64);
                ci.Polyline(ring, A(a.Lightened(0.3f), 0.7f), R * 0.12f, true);
                ci.Polyline(ring, A(b, 0.6f), R * 0.03f, true);
                int n = (int)(20 + 120 * hit);
                for (int i = 0; i < n; i++)
                {
                    float u = Mathf.PosMod(N(seed, i) + t * (0.05f + 0.2f * hit), 1f);
                    var p = hit > 0f ? area.Position + new Vector2(u * area.Size.X, area.Size.Y * (0.35f + 0.3f * N(seed, i + 7))) : ring[(int)(u * 63)];
                    ci.Circle(p, 1f + N(seed, i + 3) * 1.5f, A(Colors.White, 0.55f), true, -1f, true);
                }
                break;
            }
            case CosmicKind.ShatteredPlanet:
            {
                // 쪼개진 행성 덩어리 · 마그마 금 · 빛나는 파편
                float R = s * 0.6f;
                for (int i = 0; i < 4; i++)
                {
                    var off = P(i * Mathf.Tau / 4f + 0.4f, R * (0.15f + 0.1f * near + 0.03f * Mathf.Sin(t * 0.2f + i)));
                    var chunk = Blob(c + off, R * 0.55f, seed + i, 10, 0.25f, i);
                    ci.Poly(chunk, b.Lightened(0.05f * i));
                    ci.Polyline(chunk.Append(chunk[0]).ToArray(), A(a, 0.9f), 2f, true);
                }
                Glow(ci, c, R * 0.3f, a, 4, 0.25f);
                for (int i = 0; i < 30; i++)
                {
                    var p = c + P(N(seed, i) * Mathf.Tau + t * 0.03f, R * (1f + 1.2f * N(seed, i + 40)));
                    ci.Circle(p, 1.2f + 2f * N(seed, i + 2), A(a, 0.7f), true, -1f, true);
                }
                break;
            }
            case CosmicKind.Kessler:
            {
                // 궤도 선 · 부서지는 위성 · 충돌 불꽃 사슬
                var orbit = Ellipse(c, s * 1.4f, s * 0.5f, 0.3f, 64);
                ci.Polyline(orbit, A(a, 0.3f), 1f, true);
                for (int i = 0; i < 9; i++)
                {
                    int idx = (int)Mathf.PosMod(i * 7 + t * 4f, 64f);
                    var p = orbit[idx];
                    ci.Box(new Rect2(p - new Vector2(3, 2), new Vector2(6, 4)), a, true);
                    ci.DrawLine(p - new Vector2(8, 0), p + new Vector2(8, 0), A(new Color("#4a7ab8"), 0.8f), 2f, true);
                    if (N(i, (int)(t * 3f)) < 0.15f + 0.3f * (near + hit)) Glow(ci, p, 10f, b, 3, 0.25f);
                }
                for (int i = 0; i < 30; i++) ci.Circle(orbit[(int)Mathf.PosMod(N(seed, i) * 64f + t * 6f, 64f)] + P(N(seed, i + 5) * Mathf.Tau, 6f), 1f, A(Colors.White, 0.5f), true, -1f, true);
                break;
            }
            case CosmicKind.HyperDust:
            {
                // 빗금처럼 흐르는 모래빛 먼지 줄 · 아지랑이
                Glow(ci, c, s * 0.8f, a, 4, 0.05f);
                int n = (int)(40 + 160 * hit);
                for (int i = 0; i < n; i++)
                {
                    float u = Mathf.PosMod(N(seed, i) + t * (0.6f + hit), 1f);
                    var p0 = hit > 0f ? area.Position + new Vector2(N(seed, i + 3) * area.Size.X, N(seed, i + 9) * area.Size.Y) + dir * (u - 0.5f) * 200f
                                      : c + side * (N(seed, i + 3) - 0.5f) * s * 1.5f + dir * u * s;
                    ci.DrawLine(p0, p0 + dir * 14f, A(i % 3 == 0 ? b : a, 0.45f), 1f, true);
                }
                break;
            }
            case CosmicKind.RoguePlanet:
            {
                // 별을 가리는 검은 원반 · 푸른 테두리 · 작은 위성
                float R = s * 0.8f;
                ci.Circle(c, R * 1.06f, A(b, 0.25f), true, -1f, true);
                ci.Circle(c, R, a, true, -1f, true);
                ci.Arc(c, R * 1.01f, dir.Angle() - 1.6f, dir.Angle() + 1.6f, 32, A(b, 0.8f), 2f, true);
                for (int i = 0; i < 3; i++)
                {
                    var p = c + P(t * (0.2f + 0.1f * i) + i * 2f, R * (1.4f + 0.3f * i));
                    ci.Circle(p, R * 0.06f, new Color("#8a8f99"), true, -1f, true);
                    ci.Arc(p, R * 0.06f, dir.Angle() - 1.5f, dir.Angle() + 1.5f, 8, A(Colors.White, 0.6f), 1f, true);
                }
                break;
            }
            case CosmicKind.ReactorBlast:
            {
                // 먼 배 실루엣 → 초록 섬광 · 충격파 · 빛나는 잔해
                if (hit <= 0f)
                {
                    var hull = new[] { c + new Vector2(-s * 0.3f, -s * 0.05f), c + new Vector2(s * 0.25f, -s * 0.05f), c + new Vector2(s * 0.32f, 0f), c + new Vector2(s * 0.25f, s * 0.05f), c + new Vector2(-s * 0.3f, s * 0.05f) };
                    ci.Poly(hull, new Color("#5a6070"));
                    ci.Box(new Rect2(c + new Vector2(-s * 0.36f, -s * 0.09f), new Vector2(s * 0.1f, s * 0.04f)), new Color("#4a5060"), true);
                    ci.Box(new Rect2(c + new Vector2(-s * 0.36f, s * 0.05f), new Vector2(s * 0.1f, s * 0.04f)), new Color("#4a5060"), true);
                    if (Mathf.PosMod(t, 0.6f) < 0.3f) ci.Circle(c + new Vector2(-s * 0.05f, 0), s * 0.03f + 4f * near, A(a, 0.9f), true, -1f, true); // 경고등
                }
                else
                {
                    Glow(ci, c, s * (0.6f + hit), a, 7, 0.12f * hit);
                    Ring(ci, c, s * (0.5f + 1.5f * Mathf.PosMod(t * 0.25f, 1f)), A(a, 0.6f * hit), 3f, seed, 0.05f);
                    for (int i = 0; i < 20; i++) ci.Circle(c + P(N(seed, i) * Mathf.Tau, s * (0.3f + N(seed, i + 4)) * (0.6f + Mathf.PosMod(t * 0.1f, 1f))), 2f, A(a.Lightened(0.3f), 0.8f), true, -1f, true);
                }
                break;
            }
            case CosmicKind.StationCollapse:
            {
                // 바퀴살 정거장 — 부서지며 흩어진다 · 빨간 경고등
                float R = s * 0.5f;
                float rot = t * 0.15f;
                float spread = hit * (0.2f + 0.5f * Mathf.PosMod(t * 0.05f, 1f));
                for (int i = 0; i < 6; i++)
                {
                    float a0 = rot + i * Mathf.Tau / 6f;
                    var off = P(a0 + Mathf.Pi / 6f, R * spread * (0.5f + N(seed, i)));
                    ci.Arc(c + off, R, a0 + 0.05f, a0 + Mathf.Tau / 6f - 0.05f, 8, a, R * 0.12f, true);
                    ci.DrawLine(c + off * 0.5f, c + off + P(a0 + Mathf.Pi / 6f, R * 0.95f), A(a.Darkened(0.2f), 0.9f), 2f, true);
                    if (Mathf.PosMod(t + i * 0.3f, 1.2f) < 0.3f) ci.Circle(c + off + P(a0 + 0.5f, R), 2.5f, b, true, -1f, true);
                }
                ci.Circle(c, R * 0.18f, a.Lightened(0.1f), true, -1f, true);
                break;
            }
            case CosmicKind.AntimatterBreach:
            {
                // 하얀·분홍 점 → 별 모양 빛살 · 겹 고리
                float pl = 0.6f + 0.4f * Mathf.Sin(t * 12f);
                ci.Circle(c, s * 0.04f * (1f + pl * near), b, true, -1f, true);
                if (hit > 0f)
                {
                    Glow(ci, c, s * 1.6f * hit, Colors.White, 8, 0.1f * hit);
                    for (int i = 0; i < 8; i++) { var d = P(i * Mathf.Tau / 8f + 0.2f, s * (1.2f + 0.8f * (i % 2)) * hit); ci.DrawLine(c - d, c + d, A(i % 2 == 0 ? Colors.White : b, 0.6f * hit), 2f, true); }
                    for (int i = 0; i < 2; i++) Ring(ci, c, s * (0.6f + 0.9f * i) * (0.6f + Mathf.PosMod(t * 0.4f, 1f)), A(i == 0 ? Colors.White : b, 0.5f * hit), 2.5f, seed + i, 0.02f);
                }
                break;
            }
            case CosmicKind.FusionRunaway:
            {
                // 하늘을 가르는 긴 푸른 배기 불꽃 (배가 머리에)
                var head = c + side * (near - 0.5f) * s * 2f;
                float len = s * (1.5f + 2.5f * near) + diag * 0.4f * hit;
                for (int i = 0; i < 5; i++) ci.DrawLine(head, head - side * len + dir * Mathf.Sin(t * 3f + i) * 6f, A(i == 0 ? Colors.White : a, 0.5f / (i + 1) + 0.1f), 2f + 5f * i, true);
                ci.Poly(new[] { head + side * 10f, head - side * 8f + dir * 5f, head - side * 8f - dir * 5f }, new Color("#c8ccd6"));
                break;
            }
            case CosmicKind.MineField:
            {
                // 가시 돋은 기뢰 · 깜빡이는 빨간 불 · 터지는 섬광
                for (int i = 0; i < 14; i++)
                {
                    var p = c + P(N(seed, i) * Mathf.Tau, s * (0.2f + 1.1f * N(seed, i + 20))) + P(t * 0.3f + i, 3f);
                    float mr = 5f + 4f * N(seed, i + 6);
                    ci.Circle(p, mr, b, true, -1f, true);
                    for (int j = 0; j < 6; j++) ci.DrawLine(p + P(j * Mathf.Tau / 6f + i, mr), p + P(j * Mathf.Tau / 6f + i, mr * 1.6f), b.Lightened(0.2f), 1.5f, true);
                    if (Mathf.PosMod(t * 1.3f + N(seed, i), 1f) < 0.25f) ci.Circle(p, 2f, a, true, -1f, true);
                    if (hit > 0f && N(i, (int)(t * 2f)) < 0.08f * hit) Glow(ci, p, 26f, new Color("#ffb347"), 5, 0.25f);
                }
                break;
            }
            case CosmicKind.OrbitalEmp:
            {
                // 충전하는 궤도 포대 (판 · 접시 · 날개) → 퍼지는 청록 구
                ci.Box(new Rect2(c - new Vector2(s * 0.12f, s * 0.06f), new Vector2(s * 0.24f, s * 0.12f)), new Color("#59606e"), true);
                ci.Box(new Rect2(c + new Vector2(-s * 0.42f, -s * 0.04f), new Vector2(s * 0.26f, s * 0.08f)), new Color("#24467a"), true);
                ci.Box(new Rect2(c + new Vector2(s * 0.16f, -s * 0.04f), new Vector2(s * 0.26f, s * 0.08f)), new Color("#24467a"), true);
                ci.Arc(c + dir * s * 0.1f, s * 0.12f, dir.Angle() - 1.2f, dir.Angle() + 1.2f, 10, new Color("#c8ccd6"), 2.5f, true);
                Glow(ci, c + dir * s * 0.14f, s * (0.08f + 0.2f * near), a, 4, 0.1f + 0.2f * near * Mathf.Abs(Mathf.Sin(t * 6f)));
                if (hit > 0f) for (int i = 0; i < 3; i++) Ring(ci, c, s * 0.3f + diag * 0.5f * Mathf.PosMod(t * 0.5f + i / 3f, 1f), A(a, 0.4f * hit), 3f, seed + i, 0.01f);
                break;
            }
            case CosmicKind.Freighter:
            {
                // 커지는 상자꼴 화물선 · 컨테이너 줄 · 항해등 · 충돌 경로선
                float L = s * 0.9f;
                var fwd = dir;
                var nrm = side;
                var bow = c + fwd * L * 0.5f;
                ci.Poly(new[] { c - fwd * L * 0.5f + nrm * L * 0.12f, bow + nrm * L * 0.12f, bow + fwd * L * 0.08f, bow - nrm * L * 0.12f, c - fwd * L * 0.5f - nrm * L * 0.12f }, b);
                for (int i = 0; i < 6; i++)
                {
                    var p = c - fwd * L * 0.4f + fwd * L * 0.14f * i;
                    var col = new[] { new Color("#d1495b"), new Color("#edae49"), new Color("#00798c"), new Color("#30638e") }[i % 4];
                    ci.Poly(new[] { p + nrm * L * 0.1f, p + nrm * L * 0.1f + fwd * L * 0.12f, p - nrm * L * 0.1f + fwd * L * 0.12f, p - nrm * L * 0.1f }, col.Darkened(0.2f));
                }
                if (Mathf.PosMod(t, 1f) < 0.5f) { ci.Circle(bow + nrm * L * 0.12f, 2.5f, new Color("#ff4d4d"), true, -1f, true); ci.Circle(bow - nrm * L * 0.12f, 2.5f, new Color("#4dff88"), true, -1f, true); }
                ci.DrawDashedLine(bow, toward, A(a, 0.5f * near), 1.5f, 10f);
                break;
            }
            case CosmicKind.PirateFleet:
            {
                // 쐐기꼴 배 넷 (편대) · 빨간 엔진불 · 예광탄
                for (int i = 0; i < 4; i++)
                {
                    var p = c + side * (i - 1.5f) * s * 0.35f - dir * Mathf.Abs(i - 1.5f) * s * 0.15f;
                    float L = s * 0.18f;
                    ci.Poly(new[] { p + dir * L, p - dir * L * 0.6f + side * L * 0.5f, p - dir * L * 0.3f, p - dir * L * 0.6f - side * L * 0.5f }, new Color("#3a3f4a"));
                    ci.Circle(p - dir * L * 0.5f, 2.5f + Mathf.Sin(t * 9f + i), a, true, -1f, true);
                    if (hit > 0f && Mathf.PosMod(t * 2f + i * 0.37f, 1f) < 0.5f)
                    {
                        float u = Mathf.PosMod(t * 2f + i * 0.37f, 1f) * 2f;
                        var from = p + (toward - p) * u;
                        ci.DrawLine(from, from + (toward - p).Normalized() * 16f, A(b, 0.9f), 2f, true);
                    }
                }
                break;
            }
            case CosmicKind.DarkNebula:
            {
                // 별을 삼키는 검은 구름 · 보랏빛 가장자리 (덮인 곳의 별이 사라진다)
                float R = s * (0.8f + 1.2f * near) + diag * 0.5f * hit;
                for (int i = 0; i < 9; i++)
                {
                    var p = c + P(N(seed, i) * Mathf.Tau, R * 0.45f * N(seed, i + 10)) + dir * R * 0.3f * hit;
                    var blob = Blob(p, R * (0.45f + 0.2f * N(seed, i + 4)), seed + i, 14, 0.25f, t * 0.01f);
                    ci.Poly(blob, A(a, 0.9f));
                    ci.Polyline(blob.Append(blob[0]).ToArray(), A(b, 0.35f), 2f, true);
                }
                break;
            }
            case CosmicKind.CosmicRayShower:
            {
                // 화면을 긋는 짧고 밝은 입자 줄 · 반짝이는 눈
                Glow(ci, c, s * 0.5f, a, 4, 0.05f + 0.05f * near);
                int n = 6 + (int)(60 * hit);
                int bucket = (int)(t * 12f);
                for (int i = 0; i < n; i++)
                {
                    var p = hit > 0f ? area.Position + new Vector2(N(bucket, i) * area.Size.X, N(bucket, i + 300) * area.Size.Y) : c + P(N(bucket, i) * Mathf.Tau, s * N(bucket, i + 9));
                    var d = P(dir.Angle() + (N(bucket, i + 77) - 0.5f) * 0.4f, 6f + 18f * N(bucket, i + 33));
                    ci.DrawLine(p, p + d, A(i % 4 == 0 ? Colors.White : a, 0.8f), 1.2f, true);
                }
                break;
            }
            case CosmicKind.IonNebula:
            {
                // 푸르게 빛나는 실타래 · 번지는 번개
                for (int i = 0; i < 8; i++)
                {
                    var pts = new Vector2[20];
                    for (int j = 0; j < 20; j++)
                    {
                        float u = j / 19f;
                        pts[j] = c + side * (u - 0.5f) * s * 2.2f + dir * (Mathf.Sin(u * 5f + i + t * 0.3f) * s * 0.15f + (i - 4) * s * 0.07f);
                    }
                    ci.Polyline(pts, A(a, 0.1f), 8f, true);
                    ci.Polyline(pts, A(b, 0.35f), 1.2f, true);
                }
                int bucket = (int)(t * 6f);
                if (N(bucket, seed) < 0.3f + 0.6f * hit)
                {
                    var p0 = c + side * (N(bucket, 1) - 0.5f) * s * 2f;
                    Bolt(ci, p0, hit > 0f ? toward + P(N(bucket, 2) * Mathf.Tau, 60f) : p0 + dir * s * 0.6f, A(Colors.White, 0.85f), 2f, bucket);
                }
                break;
            }
            case CosmicKind.GravityWave:
            {
                // 번지는 동심 물결 · 흔들리는 별
                for (int i = 0; i < 7; i++)
                {
                    float u = Mathf.PosMod(t * 0.25f + i / 7f, 1f);
                    float rr = s * 0.2f + (s * 1.5f + diag * hit) * u;
                    var pts = new Vector2[65];
                    for (int j = 0; j <= 64; j++) { float ang = j * Mathf.Tau / 64f; pts[j] = c + P(ang, rr * (1f + 0.04f * Mathf.Sin(ang * 2f + t * 2f))); }
                    ci.Polyline(pts, A(i % 2 == 0 ? a : b, 0.35f * (1f - u)), 2f, true);
                }
                ci.Circle(c, s * 0.05f, b, true, -1f, true);
                ci.Circle(c + P(t * 5f, s * 0.08f), s * 0.03f, a, true, -1f, true);
                ci.Circle(c - P(t * 5f, s * 0.08f), s * 0.03f, a, true, -1f, true);
                break;
            }
        }
    }

    /// <summary>지나간 뒤 하늘에 남은 것.</summary>
    public static void Remnant(CanvasItem ci, CosmicRemnant rem, Vector2 c, float r, float t, Color a, Color b, int seed, float fade)
    {
        switch (rem)
        {
            case CosmicRemnant.Nebula:
                // 실처럼 갈라진 껍질 · 매듭 · 가운데 희미한 빛
                Glow(ci, c, r * 0.5f, a, 6, 0.04f * fade);
                for (int i = 0; i < 26; i++)
                {
                    float ang = N(seed, i) * Mathf.Tau;
                    float rr = r * (0.75f + 0.35f * N(seed, i + 40));
                    ci.Arc(c, rr, ang, ang + 0.4f + 0.5f * N(seed, i + 9), 10, A(i % 3 == 0 ? a : b, 0.35f * fade), 1.5f + 2f * N(seed, i + 2), true);
                }
                for (int i = 0; i < 10; i++) Glow(ci, c + P(N(seed, i + 70) * Mathf.Tau, r * (0.8f + 0.2f * N(seed, i + 80))), r * 0.06f, b, 3, 0.25f * fade);
                ci.Circle(c, 1.5f, A(Colors.White, 0.8f * fade), true, -1f, true);
                break;
            case CosmicRemnant.GlowCloud:
                for (int i = 0; i < 5; i++) ci.Poly(Blob(c + P(N(seed, i) * Mathf.Tau, r * 0.3f), r * (0.4f + 0.2f * N(seed, i + 3)), seed + i, 14, 0.2f, t * 0.005f), A(i % 2 == 0 ? a : b, 0.07f * fade));
                for (int i = 0; i < 12; i++) ci.Circle(c + P(N(seed, i + 20) * Mathf.Tau, r * N(seed, i + 30)), 1f, A(Colors.White, (0.4f + 0.4f * Mathf.Sin(t + i)) * fade), true, -1f, true);
                break;
            case CosmicRemnant.Ring:
                var ring = Ellipse(c, r, r * 0.3f, 0.3f, 64);
                for (int i = 0; i < 64; i += 2) ci.Circle(ring[i] + P(N(seed, i) * Mathf.Tau, 2f), 1.2f, A(i % 4 == 0 ? a : b, 0.6f * fade), true, -1f, true);
                break;
            case CosmicRemnant.Streak:
                var d = P(N(seed, 1) * Mathf.Tau, r);
                ci.DrawLine(c - d, c + d, A(a, 0.06f * fade), 14f, true);
                ci.DrawLine(c - d, c + d, A(b, 0.3f * fade), 1.5f, true);
                break;
            case CosmicRemnant.Wreck:
                for (int i = 0; i < 9; i++)
                {
                    var p = c + P(N(seed, i) * Mathf.Tau + t * 0.01f, r * 0.6f * N(seed, i + 5));
                    ci.Poly(Blob(p, 3f + 4f * N(seed, i + 8), seed + i, 5, 0.4f, t * 0.1f + i), A(new Color("#5a6070"), fade));
                    ci.Circle(p, 1.5f, A(a, (0.5f + 0.5f * Mathf.Sin(t * 2f + i)) * fade), true, -1f, true);
                }
                break;
            case CosmicRemnant.Bubble:
                ci.Arc(c, r, 0f, Mathf.Tau, 64, A(a, 0.2f * fade), 2f, true);
                ci.Arc(c, r * 0.94f, -1f, 1.4f, 24, A(b, 0.4f * fade), 3f, true);
                break;
            case CosmicRemnant.Scar:
                for (int i = 0; i < 3; i++) ci.Arc(c, r * (0.6f + 0.15f * i), -2.6f + 0.1f * i, -0.5f, 20, A(i == 1 ? b : a, (0.15f + 0.05f * Mathf.Sin(t + i)) * fade), 6f, true);
                break;
        }
    }
}

/// <summary>바깥 시점 설정 (user://cosmic.cfg — 기존 설정 파일과 따로).</summary>
public static class CosmicViewSettings
{
    private const string Path = "user://cosmic.cfg";
    private static bool _loaded;
    private static bool _outside = true;

    public static bool OutsideView
    {
        get { Load(); return _outside; }
        set
        {
            _outside = value;
            var cfg = new ConfigFile();
            cfg.SetValue("cosmic", "outside_view", value);
            cfg.Save(Path);
        }
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok) _outside = (bool)cfg.GetValue("cosmic", "outside_view", true);
    }
}

/// <summary>배경 층의 하늘: 다가오는 · 지나가는 대재난 · 남은 성운. 경보음 · 흔들림 · 바깥 시점도 여기서.</summary>
public partial class CosmicSky : Node2D
{
    public Main Main { get; set; } = null!;
    private float _time;
    private CosmicFlash _flash = null!;
    private CosmicSound _sound = null!;
    private readonly Dictionary<int, (CosmicPhase phase, int stages, int notes)> _seen = new();
    private float _outsideFor = -1f;
    private Vector2 _savedPos, _savedZoom, _setZoom;

    public override void _Ready()
    {
        var layer = new CanvasLayer { Name = "CosmicFront", Layer = 4 };
        _flash = new CosmicFlash { Name = "CosmicFlash", Sky = this };
        layer.AddChild(_flash);
        Main.CallDeferred(Node.MethodName.AddChild, layer);
        _sound = new CosmicSound { Name = "CosmicSound" };
        AddChild(_sound);
    }

    public World W => Main.Sim;
    public float Time => _time;

    /// <summary>화면 위 근원 자리 (배 가운데에서 본 각도 → 화면 테두리 안쪽 원).</summary>
    public Vector2 SourcePos(float angle, float dist = 0.36f)
    {
        var size = GetViewportRect().Size;
        return size / 2f + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Min(size.X, size.Y) * dist * new Vector2(size.X / Mathf.Min(size.X, size.Y) * 0.9f, 1f);
    }

    public static float Near(World w, CosmicEvent e)
    {
        if (e.Phase >= CosmicPhase.Impact) return 1f;
        float span = MathF.Max(1f, e.Arrive - e.Seen);
        return Mathf.Clamp(1f - (e.Arrive - w.Tick) / span, 0f, 1f);
    }

    public static float Hit(World w, CosmicEvent e) => e.Phase != CosmicPhase.Impact ? 0f
        : Mathf.Clamp(MathF.Max(MathF.Max(w.Cosmic.FxNow(e, CosmicFx.Radiation | CosmicFx.Emp | CosmicFx.Light | CosmicFx.Shock | CosmicFx.Strike), w.Cosmic.FxNow(e, CosmicFx.Debris | CosmicFx.Blind | CosmicFx.Plasma | CosmicFx.Hostile)),
            w.Cosmic.FxNow(e, CosmicFx.Heat | CosmicFx.Cold | CosmicFx.Tidal | CosmicFx.Quake | CosmicFx.Nav)) + 0.15f, 0f, 1f);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        QueueRedraw();
        _flash.QueueRedraw();
        if (Main.Replaying != null) return;
        var w = W;
        // 상태가 바뀐 순간: 소리 · 흔들림 · 바깥 시점
        foreach (var e in w.Cosmic.Events)
        {
            var now = (Phase: e.Phase, StagesStarted: e.StagesStarted, Notes: e.Notes.Count);
            if (!_seen.TryGetValue(e.Id, out var was)) { _seen[e.Id] = now; if (e.Known) _sound.Cue(CosmicSound.Kind.Forecast, e.Kind); continue; }
            if (now == was) continue;
            _seen[e.Id] = now;
            if (now.Phase == CosmicPhase.Brace && was.phase < CosmicPhase.Brace) _sound.Cue(CosmicSound.Kind.Alarm, e.Kind);
            if (now.Notes > was.notes) _sound.Cue(CosmicSound.Kind.Alarm, e.Kind);
            if (now.StagesStarted != was.stages)
            {
                _sound.Cue(CosmicSound.Kind.Stage, e.Kind);
                float shake = e.Spec.Has(CosmicFx.Strike) ? 26f : e.Spec.Has(CosmicFx.Shock) ? 18f : e.Spec.Has(CosmicFx.Quake) || e.Spec.Has(CosmicFx.Tidal) ? 10f : 4f;
                Main.Camera.Shake(shake);
                _flash.Kick(e);
                if (CosmicViewSettings.OutsideView && !Main.Following) StartOutside();
            }
        }
        if (w.Cosmic.ShakeNow > 0.2f) Main.Camera.Shake(6f * w.Cosmic.ShakeNow);
        if (_outsideFor >= 0f)
        {
            _outsideFor -= dt;
            if (_outsideFor < 0f && Main.Camera.Zoom.IsEqualApprox(_setZoom))
            {
                Main.Camera.Zoom = _savedZoom;
                Main.Camera.Position = _savedPos;
            }
        }
    }

    /// <summary>바깥 시점: 배 전체가 보이게 물러났다가 몇 초 뒤 돌아온다 (카메라는 보기만).</summary>
    private void StartOutside()
    {
        var cam = Main.Camera;
        if (_outsideFor < 0f) { _savedPos = cam.Position; _savedZoom = cam.Zoom; }
        var size = GetViewportRect().Size;
        cam.FitTo(Main.ShipView.Bounds.Grow(Main.ShipView.Bounds.Size.X * 0.35f), new Rect2(size * 0.1f, size * 0.8f));
        _setZoom = cam.Zoom;
        _outsideFor = 6f;
    }

    public override void _Draw()
    {
        var w = W;
        var size = GetViewportRect().Size;
        var area = new Rect2(Vector2.Zero, size);
        var center = size / 2f;
        float unit = Mathf.Min(size.X, size.Y);
        // 남은 것: 생긴 뒤 며칠에 걸쳐 짙어진다
        foreach (var m in w.Cosmic.Sky)
        {
            var spec = CosmicCatalog.Spec(m.Kind);
            float fade = Mathf.Clamp((w.Tick - m.Tick) / (float)(SimTime.TicksPerDay * 3), 0.25f, 1f);
            CosmicArt.Remnant(this, m.Remnant, SourcePos(m.Angle, m.Dist), unit * 0.16f * m.Size, _time, new Color(spec.Hex), new Color(spec.Hex2), m.EventId * 31 + 7, fade);
        }
        // 다가오는 · 지나가는 것 (알기 전에도 하늘엔 있다 — 사람이 못 볼 뿐)
        foreach (var e in w.Cosmic.Events)
        {
            if (e.Phase is CosmicPhase.Done or CosmicPhase.After || e.Ghost) continue;
            float near = Near(w, e);
            if (near < 0.05f && !e.Known) continue;
            float hit = Hit(w, e);
            var pos = SourcePos(e.Side, e.Kind is CosmicKind.SuperFlare or CosmicKind.RedGiantShell or CosmicKind.PlanetRing ? 0.55f : 0.38f);
            CosmicArt.Draw(this, e.Kind, pos, unit * 0.22f, _time, near, hit, area, center, e.Id * 97 + 13);
        }
        // 별빛 색: 본 사건 동안 하늘이 그 재난의 색으로 물든다
        foreach (var e in w.Cosmic.Events)
        {
            if (e.Phase != CosmicPhase.Impact || e.Ghost) continue;
            float hit = Hit(w, e);
            this.Box(area, CosmicArt.A(new Color(e.Spec.Hex), 0.05f * hit), true);
            if (e.Kind is CosmicKind.BinaryEclipse) this.Box(area, new Color(0.01f, 0.02f, 0.06f, 0.5f * hit), true); // 하늘이 어두워진다
        }
    }
}

/// <summary>앞 층: 섬광 · 지지직 · 붉은 안개 · 서리 · 우주선 · 충격파 테 (배 위에 겹친다).</summary>
public partial class CosmicFlash : Node2D
{
    public CosmicSky Sky { get; set; } = null!;
    private float _kick;
    private CosmicKind _kickKind;
    private float _kickAt = -10f;

    public void Kick(CosmicEvent e)
    {
        _kick = 1f;
        _kickKind = e.Kind;
        _kickAt = Sky.Time;
    }

    public override void _Draw()
    {
        var w = Sky.W;
        var size = GetViewportRect().Size;
        var area = new Rect2(Vector2.Zero, size);
        float t = Sky.Time;
        float since = t - _kickAt;
        // 단계가 시작되는 순간의 섬광 (재난 색) · 충격파 테가 화면을 가로지른다
        if (since < 2.5f && Main.Paused == false)
        {
            var spec = CosmicCatalog.Spec(_kickKind);
            float f = Mathf.Clamp(1f - since / 1.2f, 0f, 1f);
            if (spec.Has(CosmicFx.Light) || spec.Has(CosmicFx.Emp)) this.Box(area, CosmicArt.A(new Color(spec.Hex).Lerp(Colors.White, 0.6f), 0.75f * f * f), true);
            if (spec.Has(CosmicFx.Shock) || spec.Has(CosmicFx.Strike) || spec.Has(CosmicFx.Quake))
            {
                float rr = size.Length() * since / 1.6f;
                this.Arc(size / 2f, rr, 0f, Mathf.Tau, 96, CosmicArt.A(new Color(spec.Hex2), 0.5f * (1f - since / 2.5f)), 18f * (1f - since / 2.5f) + 2f, true);
            }
        }
        foreach (var e in w.Cosmic.Events)
        {
            if (e.Phase != CosmicPhase.Impact || e.Ghost) continue;
            var cs = w.Cosmic;
            float emp = cs.FxNow(e, CosmicFx.Emp);
            if (emp > 0f || w.Automation.Rebooting && e.Spec.Has(CosmicFx.Emp))
            {
                // 지지직: 가로 줄 잡음 · 색 어긋남
                int bucket = (int)(t * 20f);
                float k = Mathf.Max(emp, 0.3f);
                for (int i = 0; i < (int)(30 * k); i++)
                {
                    float y = CosmicArt.N(bucket, i) * size.Y;
                    float x = CosmicArt.N(bucket, i + 100) * size.X;
                    this.Box(new Rect2(x - 200f, y, 400f * CosmicArt.N(bucket, i + 200) + 40f, 1f + 3f * CosmicArt.N(bucket, i + 300)), new Color(0.8f, 1f, 1f, 0.15f * k), true);
                }
                if (CosmicArt.N(bucket, 7) < 0.2f * k) this.Box(area, new Color(0f, 1f, 0.9f, 0.05f), true);
            }
            if (e.Kind == CosmicKind.RedGiantShell)
            {
                float h = cs.FxNow(e, CosmicFx.Heat);
                this.Box(area, new Color(0.6f, 0.08f, 0.02f, 0.28f * h), true);
                for (int i = 0; i < 10; i++) this.Poly(CosmicArt.Blob(new Vector2(CosmicArt.N(i, 1) * size.X, CosmicArt.N(i, 2) * size.Y) + CosmicArt.P(t * 0.1f + i, 40f), size.Y * 0.25f, i, 12, 0.3f, t * 0.02f), new Color(0.9f, 0.2f, 0.05f, 0.05f * h));
            }
            if (e.Kind == CosmicKind.BinaryEclipse)
            {
                float c = cs.FxNow(e, CosmicFx.Cold);
                for (int i = 0; i < 60; i++) // 화면 가장자리 서리 결정
                {
                    float u = CosmicArt.N(i, 5);
                    var p = (i % 4) switch { 0 => new Vector2(u * size.X, 0f), 1 => new Vector2(u * size.X, size.Y), 2 => new Vector2(0f, u * size.Y), _ => new Vector2(size.X, u * size.Y) };
                    float len = (30f + 60f * CosmicArt.N(i, 6)) * c;
                    var inward = (size / 2f - p).Normalized();
                    for (int j = -1; j <= 1; j++) DrawLine(p, p + inward.Rotated(j * 0.5f) * len, new Color(0.8f, 0.9f, 1f, 0.35f * c), 1.5f, true);
                }
            }
            if (e.Kind == CosmicKind.DarkNebula)
            {
                float bl = cs.FxNow(e, CosmicFx.Blind);
                this.Box(area, new Color(0.02f, 0.01f, 0.04f, 0.35f * bl), true);
            }
            if (e.Kind == CosmicKind.CosmicRayShower)
            {
                float r = cs.FxNow(e, CosmicFx.Radiation);
                int bucket = (int)(t * 15f);
                for (int i = 0; i < (int)(25 * r); i++) this.Circle(new Vector2(CosmicArt.N(bucket, i) * size.X, CosmicArt.N(bucket, i + 50) * size.Y), 1.5f, new Color(0.7f, 1f, 0.85f, 0.9f), true, -1f, true);
            }
        }
    }

    private Main Main => Sky.Main;
}

/// <summary>대재난 소리 (파일 없이 만든다): 예보 삑 · 대비 경보 · 재난마다 다른 단계 소리.</summary>
public partial class CosmicSound : Node
{
    public enum Kind { Forecast, Alarm, Stage }
    private const int Rate = 22050;
    private readonly List<AudioStreamPlayer> _pool = new();
    private AudioStreamWav _beep = null!, _klaxon = null!, _rumble = null!, _crackle = null!, _tick = null!, _whoosh = null!, _drone = null!, _boom = null!;

    public override void _Ready()
    {
        _beep = Make(Gen(0.5f, (i, x) => (x < 0.12f || x > 0.25f && x < 0.37f ? 1f : 0f) * Mathf.Sin(i * Mathf.Tau * 1320f / Rate) * 0.35f));
        _klaxon = Make(Gen(1.6f, (i, x) => Mathf.Sign(Mathf.Sin(i * Mathf.Tau * ((int)(x * 2.5f) % 2 == 0 ? 520f : 690f) / Rate)) * 0.18f * (1f - x * 0.3f)));
        var rng = new RandomNumberGenerator { Seed = 1813 };
        float lp = 0f;
        _rumble = Make(Gen(2.4f, (i, x) => { lp += (rng.Randf() * 2f - 1f - lp) * 0.02f; return lp * 3.5f * (1f - x) * Mathf.Min(1f, x * 20f); }));
        _crackle = Make(Gen(1.4f, (i, x) => rng.Randf() < 0.02f ? (rng.Randf() * 2f - 1f) * 0.8f * (1f - x) : (rng.Randf() * 2f - 1f) * 0.05f));
        _tick = Make(Gen(1.6f, (i, x) => Mathf.PosMod(x, 0.33f) < 0.008f ? Mathf.Sin(i * 0.9f) * 0.7f : 0f));
        _whoosh = Make(Gen(1.8f, (i, x) => (rng.Randf() * 2f - 1f) * 0.3f * Mathf.Sin(x * Mathf.Pi) * (0.5f + 0.5f * Mathf.Sin(i * Mathf.Tau * (200f + 1600f * x) / Rate))));
        _drone = Make(Gen(3f, (i, x) => (Mathf.Sin(i * Mathf.Tau * 55f / Rate) + Mathf.Sin(i * Mathf.Tau * 58f / Rate)) * 0.22f * Mathf.Sin(x * Mathf.Pi)));
        _boom = Make(Gen(2f, (i, x) => { lp += (rng.Randf() * 2f - 1f - lp) * 0.05f; return (lp * 3f + Mathf.Sin(i * Mathf.Tau * 42f / Rate) * 0.6f) * Mathf.Exp(-x * 3f); }));
        for (int i = 0; i < 3; i++) { var p = new AudioStreamPlayer(); AddChild(p); _pool.Add(p); }
    }

    private static float[] Gen(float seconds, Func<int, float, float> f)
    {
        int n = (int)(Rate * seconds);
        var s = new float[n];
        for (int i = 0; i < n; i++) s[i] = f(i, i / (float)n);
        return s;
    }

    private static AudioStreamWav Make(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)Math.Clamp((int)(samples[i] * 32000f), short.MinValue, short.MaxValue);
            bytes[2 * i] = (byte)(v & 0xff);
            bytes[2 * i + 1] = (byte)((v >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = bytes };
    }

    public void Cue(Kind k, CosmicKind kind)
    {
        if (!Settings.Effects) return;
        var spec = CosmicCatalog.Spec(kind);
        AudioStreamWav s = k switch
        {
            Kind.Forecast => _beep,
            Kind.Alarm => _klaxon,
            _ => kind switch
            {
                CosmicKind.PulsarBeam => _tick,
                CosmicKind.MagnetarStorm or CosmicKind.OrbitalEmp or CosmicKind.IonNebula or CosmicKind.CoronalMass => _crackle,
                CosmicKind.GammaBurst or CosmicKind.CosmicRayShower or CosmicKind.FusionRunaway or CosmicKind.WolfRayetWind or CosmicKind.HyperDust => _whoosh,
                CosmicKind.BlackHoleTide or CosmicKind.NeutronStar or CosmicKind.DarkNebula or CosmicKind.GravityWave or CosmicKind.RoguePlanet or CosmicKind.BinaryEclipse => _drone,
                _ when spec.Has(CosmicFx.Strike) || spec.Has(CosmicFx.Shock) => _boom,
                _ => _rumble,
            },
        };
        var p = _pool.FirstOrDefault(x => !x.Playing) ?? _pool[0];
        p.Stream = s;
        p.VolumeDb = k == Kind.Forecast ? -12f : -6f;
        p.Play();
    }
}

// ─────────────────────────────── 배 위 (세계 좌표) ───────────────────────────────

public partial class ShipView
{
    private Vector2 ShipCenterPx() => Bounds.GetCenter();

    /// <summary>배 아래 (선체 바깥): 다가오는 소행성 · 화물선 · 자기력선 · 조석 화살 · 스치는 잔해 · 예광탄.</summary>
    private void PaintCosmicUnder(CanvasItem ci)
    {
        var w = _world;
        foreach (var e in w.Cosmic.Events)
        {
            if (e.Ghost || e.Phase is CosmicPhase.Done or CosmicPhase.After) continue;
            var dir = new Vector2(Mathf.Cos(e.Side), Mathf.Sin(e.Side));
            var side = new Vector2(-dir.Y, dir.X);
            var c = ShipCenterPx();
            float half = Bounds.Size.Length() * 0.5f;
            float hit = CosmicSky.Hit(w, e);
            // 큰 것이 다가온다: 마지막 한 시간 동안 배 쪽으로 (비키면 옆으로 샌다)
            if (e.Kind is CosmicKind.BigAsteroid or CosmicKind.Freighter && e.Phase <= CosmicPhase.Impact)
            {
                float left = (e.Arrive - w.Tick) / (float)SimTime.TicksPerHour;
                if (left < 1.2f && left > -0.1f)
                {
                    var target = e.TargetRoom >= 0 ? ToPx(w.Ship.Rooms[e.TargetRoom].Center) : c;
                    float u = Mathf.Clamp(left / 1.2f, 0f, 1f);
                    var p = target + dir * (half * 0.2f + half * 2.2f * u) + (e.Avoided || e.BurnAt >= 0 ? side * half * 0.9f * (1f - u) : Vector2.Zero);
                    ci.DrawDashedLine(p, target, CosmicArt.A(Palette.Danger, 0.5f), 2f, 12f);
                    if (e.Kind == CosmicKind.BigAsteroid)
                    {
                        float R = T * 3.5f;
                        var rock = CosmicArt.Blob(p, R, e.Id * 13, 16, 0.22f, _time * 0.2f);
                        ci.Poly(rock, new Color("#6b5d4d"));
                        for (int i = 0; i < 5; i++) ci.Circle(p + CosmicArt.P(CosmicArt.N(e.Id, i) * Mathf.Tau + _time * 0.2f, R * 0.55f * CosmicArt.N(e.Id, i + 5)), R * 0.15f, new Color("#4a4036"), true, -1f, true);
                        ci.Arc(p, R, (-dir).Angle() - 1.2f, (-dir).Angle() + 1.2f, 16, new Color("#ffcf8a"), 3f, true);
                    }
                    else
                    {
                        var fwd = -dir;
                        float L = T * 9f;
                        ci.Poly(new[] { p - fwd * L * 0.5f + side * T, p + fwd * L * 0.4f + side * T, p + fwd * L * 0.5f, p + fwd * L * 0.4f - side * T, p - fwd * L * 0.5f - side * T }, new Color("#4a4f5c"));
                        for (int i = 0; i < 6; i++) ci.Box(new Rect2(p - fwd * L * 0.4f + fwd * L * 0.13f * i - new Vector2(T * 0.5f, T * 0.5f), new Vector2(T, T)), new[] { new Color("#d1495b"), new Color("#edae49"), new Color("#00798c") }[i % 3], true);
                        if (Mathf.PosMod(_time, 1f) < 0.5f) ci.Circle(p + fwd * L * 0.5f, 5f, new Color("#ffb000"), true, -1f, true);
                    }
                }
            }
            if (hit <= 0.15f) continue;
            // 자기력선이 배를 감싼다
            if (e.Kind == CosmicKind.MagnetarStorm)
                for (int i = 1; i <= 4; i++)
                {
                    var pts = new Vector2[40];
                    for (int j = 0; j < 40; j++)
                    {
                        float th = j * Mathf.Tau / 39f;
                        pts[j] = c + new Vector2(Mathf.Cos(th) * half * (0.7f + 0.25f * i), Mathf.Sin(th) * half * (0.25f + 0.1f * i) * (1f + 0.1f * Mathf.Sin(_time * 3f + i + th * 2f))).Rotated(e.Side);
                    }
                    ci.Polyline(pts, CosmicArt.A(new Color(i % 2 == 0 ? "#c46cff" : "#ff4fd8"), 0.35f * hit), 2f, true);
                }
            // 조석: 배가 길게 당겨진다 (양 끝 화살)
            if (e.Spec.Has(CosmicFx.Tidal) && w.Cosmic.FxNow(e, CosmicFx.Tidal) > 0f)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                    for (int i = 0; i < 3; i++)
                    {
                        float pull = 0.5f + 0.5f * Mathf.Sin(_time * 1.5f + i);
                        var basePt = c + dir * sgn * (half * 0.9f + i * T * 1.4f + pull * T);
                        ci.DrawLine(basePt, basePt + dir * sgn * T * 1.6f, CosmicArt.A(new Color(e.Spec.Hex2), 0.6f * hit), 3f, true);
                        ci.Poly(new[] { basePt + dir * sgn * T * 2.2f, basePt + dir * sgn * T * 1.4f + side * T * 0.5f, basePt + dir * sgn * T * 1.4f - side * T * 0.5f }, CosmicArt.A(new Color(e.Spec.Hex2), 0.6f * hit));
                    }
            // 잔해 · 먼지 · 고리 알갱이가 배 곁을 스친다
            if (w.Cosmic.FxNow(e, CosmicFx.Debris | CosmicFx.Plasma) > 0f)
            {
                var col = new Color(e.Spec.Hex);
                for (int i = 0; i < 60; i++)
                {
                    float u = Mathf.PosMod(CosmicArt.N(e.Id, i) - _time * (0.2f + 0.3f * CosmicArt.N(e.Id, i + 60)), 1f);
                    var p = c + dir * (u - 0.5f) * half * 3f + side * (CosmicArt.N(e.Id, i + 120) - 0.5f) * half * 2.4f;
                    ci.DrawLine(p, p + dir * T * 0.8f, CosmicArt.A(col, 0.5f), 1.5f + 2f * CosmicArt.N(e.Id, i + 7), true);
                }
            }
            // 예광탄 · 기뢰 섬광
            if (w.Cosmic.FxNow(e, CosmicFx.Hostile) > 0f)
                for (int i = 0; i < 6; i++)
                {
                    float u = Mathf.PosMod(_time * 1.5f + i * 0.21f, 1f);
                    var from = c + dir * half * (1.6f - 1.2f * u) + side * (CosmicArt.N(e.Id, i) - 0.5f) * half;
                    ci.DrawLine(from, from - dir * T * 2f, new Color("#ffd166"), 2f, true);
                }
        }
    }

    private readonly Dictionary<int, (int stages, float at)> _cosmicFront = new();

    /// <summary>v19 고유 장면: 감마선 줄기와 그늘 · 암흑 시간의 손전등 · 해적을 막은 문 · 조석 균열 · 낙진을 뒤집어쓴 사람.</summary>
    private void PaintCosmicScenes(CanvasItem ci)
    {
        var w = _world;
        var cs = w.Cosmic;
        foreach (var e in cs.Events)
        {
            if (e.Ghost || e.Phase == CosmicPhase.Done) continue;
            switch (e.Kind)
            {
                case CosmicKind.GammaBurst when e.Phase is CosmicPhase.Brace or CosmicPhase.Impact:
                {
                    float pulse = 0.7f + 0.3f * Mathf.Sin(_time * 3f);
                    foreach (var room in w.Ship.Rooms)
                    {
                        if (room.Detached) continue;
                        float sh = cs.Shade(room, e);
                        var rect = RoomRect(room);
                        if (sh > 0.45f) ci.Box(rect, new Color(0.7f, 0.45f, 1f, (0.06f + 0.12f * sh) * pulse), true); // 줄기가 닿는 방
                        else if (sh < 0.15f && room.Type != RoomType.Corridor)
                        {
                            ci.Box(rect, new Color(0f, 0f, 0.05f, 0.22f), true);
                            Gfx.TextCentered(ci, Fonts.Bold, rect.GetCenter(), "그늘", 13, new Color("#b48cff"));
                        }
                    }
                    var c = ShipCenterPx();
                    float half = Bounds.Size.Length() * 0.5f;
                    var dir = new Vector2(Mathf.Cos(e.Side), Mathf.Sin(e.Side));
                    // 줄기가 들어오는 쪽: 굵은 보랏빛 화살
                    var from = c + dir * half * 1.15f;
                    ci.DrawLine(from, c + dir * half * 0.8f, new Color("#b48cff").WithAlpha(0.8f * pulse), 8f, true);
                    // 돌리는 중: 지금 향 → 돌릴 향으로 호
                    if (e.SceneChoice == 4 && !float.IsNaN(e.TurnTo))
                    {
                        float a0 = e.Side, a1 = e.TurnTo;
                        float d = Mathf.Wrap(a1 - a0, -Mathf.Pi, Mathf.Pi);
                        ci.Arc(c, half * 0.95f, a0, a0 + d, 24, new Color("#ffd166"), 3f, true);
                        var tip = c + new Vector2(Mathf.Cos(a0 + d), Mathf.Sin(a0 + d)) * half * 0.95f;
                        ci.Circle(tip, 6f, new Color("#ffd166"), true, -1f, true);
                    }
                    break;
                }
                case CosmicKind.MagnetarStorm when e.DarkUntil >= 0:
                {
                    // 암흑 시간: 배 전체가 어둡고 사람마다 손전등 빛
                    ci.Box(Bounds.Grow(T * 2f), new Color(0f, 0f, 0.03f, 0.5f), true);
                    foreach (var c in w.Crew)
                    {
                        if (c.Dead || c.Outside || c.Room == null) continue;
                        var p = CrewPx(c);
                        var f = new Vector2(c.Facing.X, c.Facing.Y);
                        if (f.LengthSquared() < 0.01f) f = Vector2.Right;
                        f = f.Normalized();
                        var side = new Vector2(-f.Y, f.X);
                        ci.Poly(new[] { p, p + f * T * 3.4f + side * T * 1.3f, p + f * T * 3.4f - side * T * 1.3f }, new Color(1f, 0.95f, 0.7f, 0.2f));
                        ci.Circle(p, T * 0.7f, new Color(1f, 0.95f, 0.7f, 0.14f));
                    }
                    break;
                }
                case CosmicKind.PirateFleet:
                    foreach (var t in e.Tasks.Where(t => t.Kind == BraceKind.Barricade && t.Done && t.RoomId >= 0 && t.RoomId < w.Ship.Rooms.Count))
                        foreach (var d in w.Ship.Rooms[t.RoomId].Doors.Where(d => !d.IsExternal))
                        {
                            var r = CellRect(d.Cell).Grow(-T * 0.08f);
                            ci.DrawLine(r.Position, r.End, new Color("#8a6a3a"), 4f, true);
                            ci.DrawLine(new Vector2(r.End.X, r.Position.Y), new Vector2(r.Position.X, r.End.Y), new Color("#8a6a3a"), 4f, true);
                        }
                    break;
                case CosmicKind.BlackHoleTide when e.Phase == CosmicPhase.Impact:
                    foreach (var room in w.Ship.Rooms)
                    {
                        if (room.Detached) continue;
                        foreach (var j in room.Joints)
                        {
                            if (j.Broken || j.Strength >= 0.6f) continue;
                            var p = CellRect(j.Cell).GetCenter();
                            float k = 1f - j.Strength / 0.6f;
                            var pts = new Vector2[6];
                            for (int i = 0; i < 6; i++) pts[i] = p + new Vector2((i - 2.5f) * T * 0.22f, (i % 2 == 0 ? -1f : 1f) * T * 0.18f * (0.5f + k));
                            ci.Polyline(pts, new Color(1f, 0.35f, 0.25f, 0.5f + 0.5f * k), 2f, true);
                        }
                    }
                    break;
            }
        }
        // 낙진을 뒤집어쓴 사람: 초록 반짝임 (제염 전까지)
        foreach (var (id, lv) in cs.Contaminated)
        {
            var c = w.Crew.FirstOrDefault(x => x.Id == id);
            if (c == null || c.Dead || c.Room == null) continue;
            var p = CrewPx(c);
            int b = (int)(_time * 8f);
            for (int i = 0; i < 6; i++)
            {
                var q = p + new Vector2(CosmicArt.N(b + id, i) - 0.5f, CosmicArt.N(b + id, i + 9) - 0.5f) * T * 1.3f;
                ci.Circle(q, 1.7f, new Color(0.55f, 1f, 0.3f, 0.85f * lv));
            }
        }
    }

    /// <summary>배 위: 방사선 반짝임 · 물벽 · 물자 · 묶음 · 꺼 둔 설비 · 봉쇄 · 충격파 앞머리 · 펄스 번개.</summary>
    private void PaintCosmicOver(CanvasItem ci)
    {
        var w = _world;
        var cs = w.Cosmic;
        if (cs.Events.Count == 0) return;
        PaintCosmicScenes(ci); // v19 고유 장면
        bool any = cs.Events.Any(e => e.Phase != CosmicPhase.Done);
        // 방마다
        foreach (var room in w.Ship.Rooms)
        {
            if (room.Detached) continue;
            var rect = new Rect2(room.MinX * T, room.MinY * T, (room.MaxX - room.MinX + 1) * T, (room.MaxY - room.MinY + 1) * T);
            float rad = cs.Radiation(room) * ((RoomCatalog.Tags(room.Kind) & RoomTag.Shielded) != 0 ? 0.15f : 1f);
            if (rad > 0.03f) // 방사선: 반짝이는 점 (센 곳일수록 많이 · 노랗게)
            {
                int n = Math.Min(40, (int)(rad * room.Cells.Count * 0.8f) + 1);
                int bucket = (int)(_time * 10f);
                for (int i = 0; i < n; i++)
                {
                    var cell = room.Cells[Math.Min(room.Cells.Count - 1, (int)(CosmicArt.N(bucket + room.Id * 977, i) * room.Cells.Count))];
                    var p = CellRect(cell).Position + new Vector2(CosmicArt.N(bucket, i + 11), CosmicArt.N(bucket, i + 23)) * T;
                    ci.DrawLine(p - new Vector2(2, 0), p + new Vector2(2, 0), new Color(0.85f, 1f, 0.4f, 0.8f), 1f);
                    ci.DrawLine(p - new Vector2(0, 2), p + new Vector2(0, 2), new Color(0.85f, 1f, 0.4f, 0.8f), 1f);
                }
            }
            float water = cs.Water(room);
            if (water > 0.02f) // 물벽: 방 안쪽 둘레에 물주머니 띠 (물결 · 반짝임)
            {
                float th = T * 0.28f * (0.5f + 0.5f * water);
                var inner = rect.Grow(-T * 0.08f);
                var col = new Color(0.3f, 0.6f, 1f, 0.35f + 0.2f * water);
                ci.Box(new Rect2(inner.Position, new Vector2(inner.Size.X, th)), col, true);
                ci.Box(new Rect2(inner.Position + new Vector2(0, inner.Size.Y - th), new Vector2(inner.Size.X, th)), col, true);
                ci.Box(new Rect2(inner.Position, new Vector2(th, inner.Size.Y)), col, true);
                ci.Box(new Rect2(inner.Position + new Vector2(inner.Size.X - th, 0), new Vector2(th, inner.Size.Y)), col, true);
                for (float x = inner.Position.X; x < inner.End.X; x += T * 0.5f)
                {
                    float y = inner.Position.Y + th * 0.5f + Mathf.Sin(x * 0.2f + _time * 2f) * th * 0.25f;
                    ci.DrawLine(new Vector2(x, y), new Vector2(x + T * 0.25f, y), new Color(0.8f, 0.95f, 1f, 0.7f), 1f, true);
                }
                for (int i = 0; i < 4; i++) ci.DrawLine(inner.Position + new Vector2(inner.Size.X * (0.2f + 0.2f * i), 0), inner.Position + new Vector2(inner.Size.X * (0.2f + 0.2f * i), th), new Color(0.1f, 0.25f, 0.5f, 0.8f), 1f); // 주머니 이음
            }
            if (cs.Supplied(room)) // 옮겨 둔 물자: 십자 상자 둘
                for (int i = 0; i < 2; i++)
                {
                    var bx = new Rect2(rect.End - new Vector2(T * (0.9f + 0.7f * i), T * 0.9f), new Vector2(T * 0.6f, T * 0.6f));
                    ci.Box(bx, new Color("#c9a66b"), true);
                    ci.Box(bx, new Color("#6b5636"), false, 1f);
                    ci.DrawLine(bx.Position + new Vector2(bx.Size.X / 2f, 3f), bx.Position + new Vector2(bx.Size.X / 2f, bx.Size.Y - 3f), i == 0 ? Palette.Danger : new Color("#5ec8e6"), 2f);
                    ci.DrawLine(bx.Position + new Vector2(3f, bx.Size.Y / 2f), bx.Position + new Vector2(bx.Size.X - 3f, bx.Size.Y / 2f), i == 0 ? Palette.Danger : new Color("#5ec8e6"), 2f);
                }
            if (any && cs.Stowed(room)) // 묶어 둔 방: 네 귀퉁이 띠 (X자 끈 · 버클)
                foreach (var corner in new[] { rect.Position, new Vector2(rect.End.X - T * 0.6f, rect.Position.Y), new Vector2(rect.Position.X, rect.End.Y - T * 0.6f), rect.End - new Vector2(T * 0.6f, T * 0.6f) })
                {
                    var r2 = new Rect2(corner + new Vector2(T * 0.1f, T * 0.1f), new Vector2(T * 0.4f, T * 0.4f));
                    ci.DrawLine(r2.Position, r2.End, new Color("#e0b84c"), 2f, true);
                    ci.DrawLine(new Vector2(r2.End.X, r2.Position.Y), new Vector2(r2.Position.X, r2.End.Y), new Color("#e0b84c"), 2f, true);
                    ci.Box(new Rect2(r2.GetCenter() - new Vector2(2, 2), new Vector2(4, 4)), new Color("#8d96a8"), true);
                }
            if (any && cs.Insulated(room)) // 보온: 벽을 따라 누빈 담요 무늬
                for (float x = rect.Position.X + T * 0.2f; x < rect.End.X - T * 0.2f; x += T * 0.4f)
                    ci.Arc(new Vector2(x, rect.Position.Y + T * 0.2f), T * 0.15f, 0f, Mathf.Pi, 6, new Color("#d98c5f"), 2f, true);
        }
        // 꺼 둔 설비: 어둡게 · 전원 표시(동그라미에 세로줄) · 다시 켤 차례면 깜빡
        foreach (int id in cs.SafedIds)
        {
            var f = w.Ship.Furniture.FirstOrDefault(x => x.Id == id);
            if (f == null || f.Room.Detached) continue;
            var r = FurnitureRect(f);
            ci.Box(r, new Color(0f, 0f, 0.02f, 0.55f), true);
            bool restart = cs.Events.Any(e => e.Tasks.Any(t => t.Kind == BraceKind.Restart && t.FurnitureId == id && !t.Done));
            var col = restart && Mathf.PosMod(_time, 1f) < 0.5f ? Palette.Warning : new Color("#9aa3b5");
            var cc = r.GetCenter();
            ci.Arc(cc, T * 0.25f, -Mathf.Pi * 0.35f, Mathf.Pi * 1.35f, 16, col, 2f, true);
            ci.DrawLine(cc - new Vector2(0, T * 0.32f), cc, col, 2f, true);
        }
        foreach (var e in cs.Events)
        {
            if (e.Phase == CosmicPhase.Done) continue;
            // 봉쇄 구획: 노랑·검정 빗금 테 + 글 · 비우는 중이면 빨간 점선과 나가는 화살
            if (e.TargetRoom >= 0 && (e.SealPlan || e.Sealed) && !e.Avoided)
            {
                var room = w.Ship.Rooms[e.TargetRoom];
                var rect = new Rect2(room.MinX * T, room.MinY * T, (room.MaxX - room.MinX + 1) * T, (room.MaxY - room.MinY + 1) * T);
                if (e.Sealed)
                {
                    for (float x = rect.Position.X; x < rect.End.X; x += T * 0.5f)
                    {
                        var col = ((int)((x - rect.Position.X) / (T * 0.5f)) % 2 == 0) ? new Color("#f5d547") : new Color("#1c1f26");
                        ci.DrawLine(new Vector2(x, rect.Position.Y), new Vector2(x + T * 0.5f, rect.Position.Y), col, 5f);
                        ci.DrawLine(new Vector2(x, rect.End.Y), new Vector2(x + T * 0.5f, rect.End.Y), col, 5f);
                    }
                    for (float y = rect.Position.Y; y < rect.End.Y; y += T * 0.5f)
                    {
                        var col = ((int)((y - rect.Position.Y) / (T * 0.5f)) % 2 == 0) ? new Color("#f5d547") : new Color("#1c1f26");
                        ci.DrawLine(new Vector2(rect.Position.X, y), new Vector2(rect.Position.X, y + T * 0.5f), col, 5f);
                        ci.DrawLine(new Vector2(rect.End.X, y), new Vector2(rect.End.X, y + T * 0.5f), col, 5f);
                    }
                    Gfx.TextCentered(ci, Fonts.Bold, rect.GetCenter(), "봉쇄", 16, new Color("#f5d547"));
                }
                else
                {
                    ci.Box(rect, CosmicArt.A(Palette.Danger, 0.5f + 0.3f * Mathf.Sin(_time * 5f)), false, 3f);
                    foreach (var d in room.Doors)
                    {
                        var dp = CellRect(d.Cell).GetCenter();
                        var outv = (dp - rect.GetCenter()).Normalized();
                        float k = Mathf.PosMod(_time, 1f);
                        ci.DrawLine(dp - outv * T * (1f - k), dp + outv * T * k, Palette.Danger, 3f, true);
                    }
                    Gfx.TextCentered(ci, Fonts.Bold, rect.GetCenter(), "비우는 중", 14, Palette.Danger);
                }
            }
            // 파편이 지나갈 줄의 방: 주황 빗금 (부딪히기 전까지 비워 둔다)
            if (e.SealPlan && !e.Avoided && w.Tick < e.Arrive + SimTime.Minutes(10))
                foreach (int rid in e.Evac)
                {
                    var room = w.Ship.Rooms[rid];
                    var rect = new Rect2(room.MinX * T, room.MinY * T, (room.MaxX - room.MinX + 1) * T, (room.MaxY - room.MinY + 1) * T);
                    for (float k = -rect.Size.Y; k < rect.Size.X; k += T * 0.7f)
                    {
                        var p0 = rect.Position + new Vector2(Mathf.Max(0f, k), Mathf.Max(0f, -k));
                        var p1 = rect.Position + new Vector2(Mathf.Min(rect.Size.X, k + rect.Size.Y), Mathf.Min(rect.Size.Y, rect.Size.X - k));
                        ci.DrawLine(p0, p1, new Color(1f, 0.55f, 0.2f, 0.18f), 3f, true);
                    }
                    ci.Box(rect, new Color(1f, 0.55f, 0.2f, 0.45f), false, 1.5f);
                }
            if (e.Phase != CosmicPhase.Impact) continue;
            var dir = new Vector2(Mathf.Cos(e.Side), Mathf.Sin(e.Side));
            var side = new Vector2(-dir.Y, dir.X);
            float half = Bounds.Size.Length() * 0.5f;
            var c = ShipCenterPx();
            // 충격파 앞머리: 단계가 바뀐 순간부터 몇 초 동안 배를 가로질러 쓸고 간다
            if (!_cosmicFront.TryGetValue(e.Id, out var fr) || fr.stages != e.StagesStarted) { _cosmicFront[e.Id] = (e.StagesStarted, _time); fr = (e.StagesStarted, _time); }
            float since = _time - fr.at;
            if (since < 3f && (e.Spec.Has(CosmicFx.Shock) || e.Spec.Has(CosmicFx.Light) || e.Spec.Has(CosmicFx.Radiation)))
            {
                var front = c + dir * half * (1.2f - since / 1.25f);
                var col = new Color(e.Spec.Hex2);
                for (int i = 0; i < 3; i++)
                {
                    var p = front + dir * i * T * 0.8f;
                    ci.DrawLine(p - side * half * 1.3f, p + side * half * 1.3f, CosmicArt.A(col, 0.45f / (i + 1)), 10f - 3f * i, true);
                }
            }
            // 펄스 번개: 제어부가 탄 설비 위로 지지직
            if (w.Cosmic.FxNow(e, CosmicFx.Emp) > 0f)
            {
                int bucket = (int)(_time * 12f);
                foreach (var f in w.Ship.Furniture.Where(f => f.Machine is Machine m && m.Has(FaultKind.ControlFault) && !f.Room.Detached).Take(12))
                {
                    var r = FurnitureRect(f);
                    var p0 = r.Position + new Vector2(CosmicArt.N(bucket, f.Id) * r.Size.X, 0f);
                    var p1 = r.Position + new Vector2(CosmicArt.N(bucket, f.Id + 5) * r.Size.X, r.Size.Y);
                    var pts = new Vector2[6];
                    for (int j = 0; j < 6; j++) pts[j] = p0.Lerp(p1, j / 5f) + new Vector2((CosmicArt.N(bucket + j, f.Id) - 0.5f) * T * 0.6f, 0f);
                    ci.Polyline(pts, new Color(0.6f, 1f, 1f, 0.9f), 1.5f, true);
                }
            }
        }
    }
}
