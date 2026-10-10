using Godot;
using ShipSim.Core;
using FA = ShipSim.View.FixtureArt;

namespace ShipSim.View;

/// <summary>
/// 압축-마 새 사고 30이 남긴 흔적 (HazardSignSystem.Traces) — 사고마다 모양이 다르고, 시간이 지나면 옅어지고, 치우면 사라진다.
/// 그리고 무르익은 원인의 전조(Signs): 그 방에 작게 보이는 낌새 (꾸르륵 거품 · 들썩이는 뚜껑 · 높은 소리 결 · 뜨는 머리카락 …).
/// 읽기만 한다 (결정론).
/// </summary>
public partial class ShipView
{
    private void PaintIncidentTraces(CanvasItem ci)
    {
        var signs = _world.Signs;
        long now = _world.Tick;
        foreach (var tr in signs.Traces)
        {
            float a = tr.Left(now);
            if (a <= 0.02f) continue;
            var cr = CellRect(tr.At);
            if (!_fixView.Intersects(cr.Grow(T))) continue;
            DrawTrace(ci, tr.Kind, cr.GetCenter(), Mathf.Clamp(a * 1.4f, 0f, 1f), tr.Id);
        }
        foreach (var s in signs.Signs)
        {
            if (s.Done || s.RoomId < 0 || s.RoomId >= _world.Ship.Rooms.Count) continue;
            var room = _world.Ship.Rooms[s.RoomId];
            var c = RoomRect(room).GetCenter();
            if (!_fixView.Intersects(new Rect2(c - new Vector2(T, T), new Vector2(T * 2f, T * 2f)))) continue;
            DrawOmen(ci, s.Kind, c + new Vector2(T * 0.6f, -T * 0.6f), s.Id);
        }
    }

    /// <summary>사고마다 다른 흔적 (a: 남은 진하기).</summary>
    private void DrawTrace(CanvasItem ci, HazardKind k, Vector2 c, float a, int id)
    {
        float h(int i) => FA.Hash(id, i, 1900);
        switch (k)
        {
            case HazardKind.CatScratch: // 할퀸 자국 셋 + 털 뭉치
                for (int i = 0; i < 3; i++) ci.DrawLine(c + new Vector2(-4f + i * 3f, -5f), c + new Vector2(-2f + i * 3f, 4f), new Color("#c84a3a").WithAlpha(0.6f * a), 0.8f, true);
                ci.Circle(c + new Vector2(7f, 5f), 2f, new Color("#d08a3a").WithAlpha(0.5f * a));
                break;
            case HazardKind.StaticZap: // 콘솔 모서리의 탄 점 + 번개 꺾임
                ci.Circle(c, 2.2f, new Color("#1a1a1a").WithAlpha(0.6f * a));
                ci.Polyline(new[] { c + new Vector2(-5f, -6f), c + new Vector2(-1f, -2f), c + new Vector2(-3f, 0f), c }, new Color("#9ad0ff").WithAlpha(0.7f * a), 0.7f, true);
                break;
            case HazardKind.HeatExhaustion: // 벗어 던진 장갑 + 빈 물병
                ci.Box(new Rect2(c + new Vector2(-6f, -2f), new Vector2(4f, 5f)), FA.Copper.WithAlpha(0.7f * a));
                ci.DrawLine(c + new Vector2(2f, 3f), c + new Vector2(8f, 0f), new Color("#c8e8ff").WithAlpha(0.7f * a), 2.4f, true);
                break;
            case HazardKind.SpaceSick: // 토한 자국 + 구겨진 봉지
                for (int i = 0; i < 5; i++) ci.Circle(c + new Vector2((h(i) - 0.5f) * 10f, (h(i + 5) - 0.5f) * 8f), 1.4f + h(i + 9) * 1.6f, new Color("#a8b048").WithAlpha(0.55f * a));
                ci.Poly(new[] { c + new Vector2(6f, -6f), c + new Vector2(10f, -5f), c + new Vector2(9f, -1f), c + new Vector2(5f, -2f) }, new Color("#e8e2d4").WithAlpha(0.8f * a));
                break;
            case HazardKind.HatchFall: // 열린 바닥 뚜껑 + 넘어진 고깔
                ci.Box(new Rect2(c - new Vector2(5f, 5f), new Vector2(10f, 10f)), new Color("#0a0c10").WithAlpha(0.8f * a));
                ci.Box(new Rect2(c - new Vector2(5f, 5f), new Vector2(10f, 10f)), FA.WarnYellow.WithAlpha(0.7f * a), false, 1f);
                ci.Poly(new[] { c + new Vector2(7f, 6f), c + new Vector2(13f, 4f), c + new Vector2(13f, 8f) }, new Color("#ff7a2a").WithAlpha(0.9f * a));
                break;
            case HazardKind.ScaldSpill: // 국 얼룩 + 김
                ci.Circle(c, 6f, new Color("#c88a3a").WithAlpha(0.35f * a));
                ci.Circle(c + new Vector2(3f, 2f), 3f, new Color("#e0a050").WithAlpha(0.4f * a));
                for (int i = 0; i < 2; i++) ci.Arc(c + new Vector2(-2f + i * 4f, -6f), 2f, Mathf.Pi, Mathf.Tau, 6, FA.SteamWhite.WithAlpha(0.5f * a), 0.6f, true);
                break;
            case HazardKind.PotFire: // 새카맣게 탄 냄비 + 그을음 부채
                ci.Circle(c + new Vector2(0f, 2f), 7f, new Color("#1a1410").WithAlpha(0.4f * a));
                ci.Circle(c, 3.6f, new Color("#2a2420").WithAlpha(0.9f * a));
                ci.DrawLine(c + new Vector2(3.6f, 0f), c + new Vector2(8f, -1f), new Color("#2a2420").WithAlpha(0.9f * a), 1.2f, true);
                break;
            case HazardKind.FermentBurst: // 깨진 조각 + 붉은 국물
                ci.Circle(c + new Vector2(1f, 1f), 6f, new Color("#a83a2a").WithAlpha(0.4f * a));
                for (int i = 0; i < 4; i++) { var p = c + Vector2.FromAngle(h(i) * Mathf.Tau) * (3f + h(i + 4) * 5f); ci.Poly(new[] { p, p + new Vector2(2f, 0.5f), p + new Vector2(0.6f, 2f) }, new Color("#8a4f2c").WithAlpha(0.9f * a)); }
                break;
            case HazardKind.HeaterOverload: // 그을린 난로 + 녹은 전선
                FA.Box(ci, new Rect2(c - new Vector2(4f, 4f), new Vector2(8f, 8f)), new Color("#3a3a3a").WithAlpha(0.8f * a), 1.5f);
                for (int i = 0; i < 3; i++) ci.DrawLine(c + new Vector2(-3f + i * 3f, -3f), c + new Vector2(-3f + i * 3f, 3f), FA.Ember.WithAlpha(0.4f * a), 0.8f, true);
                ci.Polyline(new[] { c + new Vector2(4f, 2f), c + new Vector2(7f, 5f), c + new Vector2(9f, 4f), c + new Vector2(12f, 7f) }, new Color("#1a1a1a").WithAlpha(0.8f * a), 1.2f, true);
                break;
            case HazardKind.PumpShock: // 물웅덩이 속 그을린 전선
                ci.Circle(c, 7f, FA.Water.WithAlpha(0.3f * a));
                ci.Polyline(new[] { c + new Vector2(-8f, -3f), c + new Vector2(-2f, 1f), c + new Vector2(3f, -1f) }, new Color("#1a1a1a").WithAlpha(0.9f * a), 1.2f, true);
                FA.Dot(ci, c + new Vector2(3f, -1f), 1.4f, new Color("#3a2a1a").WithAlpha(0.9f * a));
                break;
            case HazardKind.DrainBackflow: // 배수구 둘레 갈색 고리
                FA.Ring(ci, c, 5f, new Color("#6a4a2a").WithAlpha(0.6f * a), 2.2f, 20);
                FA.Ring(ci, c, 8f, new Color("#6a4a2a").WithAlpha(0.25f * a), 1.4f, 24);
                break;
            case HazardKind.WeevilSwarm: // 선반 위 바구미 줄
                for (int i = 0; i < 9; i++) FA.Dot(ci, c + new Vector2(-8f + i * 2f, Mathf.Sin(i * 1.3f) * 1.5f), 0.6f, new Color("#2a1a0a").WithAlpha(0.9f * a));
                ci.DrawLine(c + new Vector2(-9f, 3f), c + new Vector2(9f, 3f), new Color("#e8d8a8").WithAlpha(0.4f * a), 1.2f, true);
                break;
            case HazardKind.PlantTopple: // 깨진 화분 + 흙 부채
                ci.Poly(new[] { c, c + new Vector2(10f, -4f), c + new Vector2(10f, 6f) }, FA.Soil.WithAlpha(0.6f * a));
                ci.Arc(c, 3f, 0.4f, 3.6f, 8, new Color("#a8583a").WithAlpha(0.9f * a), 1.6f, true);
                FA.Dot(ci, c + new Vector2(4f, 1f), 1.2f, FA.Leaf.WithAlpha(0.8f * a));
                break;
            case HazardKind.MoonshineFire: // 터진 통 + 그을린 관
                ci.Circle(c, 7f, new Color("#1a1410").WithAlpha(0.35f * a));
                FA.Box(ci, new Rect2(c - new Vector2(3f, 4f), new Vector2(6f, 8f)), new Color("#5a3a20").WithAlpha(0.9f * a), 2f);
                for (int i = 0; i < 3; i++) FA.Ring(ci, c + new Vector2(6f, -4f + i * 2.4f), 1.4f, new Color("#4a2a1a").WithAlpha(0.8f * a), 0.6f, 8);
                break;
            case HazardKind.PartitionFall: // 넘어진 판 + 빠진 볼트
                ci.Poly(new[] { c + new Vector2(-10f, -2f), c + new Vector2(8f, -6f), c + new Vector2(10f, -2f), c + new Vector2(-8f, 2f) }, new Color("#7a8290").WithAlpha(0.8f * a));
                FA.Dot(ci, c + new Vector2(4f, 6f), 0.9f, FA.Chrome.WithAlpha(a));
                FA.Dot(ci, c + new Vector2(-5f, 7f), 0.9f, FA.Chrome.WithAlpha(a));
                break;
            case HazardKind.LockedIn: // 문틈의 지렛대 자국
                for (int i = 0; i < 4; i++) ci.DrawLine(c + new Vector2(-3f + i * 1.6f, -6f), c + new Vector2(-2f + i * 1.6f, 6f), new Color("#c8d0dc").WithAlpha(0.6f * a), 0.6f, true);
                ci.DrawLine(c + new Vector2(6f, 4f), c + new Vector2(10f, -8f), new Color("#c0302a").WithAlpha(0.8f * a), 1.4f, true);
                break;
            case HazardKind.BearingWhine: // 설비 밑 기름 방울
                for (int i = 0; i < 4; i++) ci.Circle(c + new Vector2(-6f + i * 4f, 8f + h(i) * 2f), 1.2f + h(i + 4), new Color("#3a3020").WithAlpha(0.6f * a));
                ci.DrawLine(c + new Vector2(-8f, 11f), c + new Vector2(8f, 11.5f), new Color("#3a3020").WithAlpha(0.3f * a), 1.6f, true); // 번진 기름 줄
                FA.Dot(ci, c + new Vector2(9f, 6f), 1.2f, new Color("#8a1a1a").WithAlpha(0.7f * a)); // 기름 깡통 뚜껑
                break;
            case HazardKind.MeetingBrawl: // 넘어진 의자 + 흩어진 종이
                ci.Box(new Rect2(c + new Vector2(-6f, -3f), new Vector2(7f, 3f)), new Color("#4a5260").WithAlpha(0.8f * a));
                ci.DrawLine(c + new Vector2(-6f, 0f), c + new Vector2(-8f, 4f), new Color("#4a5260").WithAlpha(0.8f * a), 1f, true);
                for (int i = 0; i < 3; i++) ci.Box(new Rect2(c + new Vector2(2f + h(i) * 6f, -4f + h(i + 3) * 8f), new Vector2(3f, 4f)), new Color("#ece6d6").WithAlpha(0.8f * a));
                break;
            case HazardKind.GravityHiccup: // 흩어진 작은 것 + 벽에 찍힌 자국
                for (int i = 0; i < 6; i++) FA.Dot(ci, c + new Vector2((h(i) - 0.5f) * 18f, (h(i + 6) - 0.5f) * 14f), 0.9f, new Color("#c8c0b0").WithAlpha(0.8f * a));
                FA.Ring(ci, c + new Vector2(9f, -7f), 2f, new Color("#5a5a5a").WithAlpha(0.6f * a), 0.8f, 10);
                break;
            case HazardKind.ManeuverJolt: // 바닥에 미끄러진 자국 (평행선 둘)
                ci.DrawLine(c + new Vector2(-10f, -2f), c + new Vector2(8f, -4f), new Color("#2a2a2a").WithAlpha(0.5f * a), 1.4f, true);
                ci.DrawLine(c + new Vector2(-10f, 2f), c + new Vector2(8f, 0f), new Color("#2a2a2a").WithAlpha(0.5f * a), 1.4f, true);
                break;
            case HazardKind.RebootGlitch: // 다시 켜짐 화면 (줄무늬 · 커서)
                FA.Box(ci, new Rect2(c - new Vector2(6f, 4f), new Vector2(12f, 8f)), new Color("#06101a").WithAlpha(0.9f * a), 1f);
                for (int i = 0; i < 3; i++) ci.DrawLine(c + new Vector2(-5f, -2.5f + i * 2f), c + new Vector2(-5f + h(i) * 9f, -2.5f + i * 2f), FA.Good.WithAlpha(0.7f * a), 0.6f);
                break;
            case HazardKind.BlackboxGap: // 빈 기록 띠
                ci.Box(new Rect2(c - new Vector2(9f, 1.5f), new Vector2(18f, 3f)), new Color("#e8641e").WithAlpha(0.6f * a));
                ci.Box(new Rect2(c - new Vector2(3f, 1.5f), new Vector2(6f, 3f)), new Color("#101010").WithAlpha(0.9f * a));
                break;
            case HazardKind.GreywaterJam: // 고인 물 (얕은 타원 둘)
                ci.Circle(c, 6f, new Color("#7a8a7a").WithAlpha(0.35f * a));
                ci.Arc(c, 6f, 0f, Mathf.Tau, 18, new Color("#5a6a5a").WithAlpha(0.5f * a), 0.8f, true);
                FA.Dot(ci, c + new Vector2(2f, -1f), 0.8f, Colors.White.WithAlpha(0.4f * a));
                break;
            case HazardKind.CatLost: // 구석의 밥그릇과 쪽지
                ci.Arc(c, 3f, 0f, Mathf.Pi, 8, new Color("#c0392b").WithAlpha(0.9f * a), 1.6f, true);
                ci.Box(new Rect2(c + new Vector2(5f, -6f), new Vector2(5f, 6f)), new Color("#f8e070").WithAlpha(0.9f * a));
                break;
            case HazardKind.GravityFailure: // 떠다니다 부딪힌 자국 여럿 (별 모양)
                for (int i = 0; i < 4; i++)
                {
                    var p = c + new Vector2((h(i) - 0.5f) * 20f, (h(i + 4) - 0.5f) * 16f);
                    for (int r = 0; r < 4; r++) ci.DrawLine(p, p + Vector2.FromAngle(r * Mathf.Pi * 0.5f + 0.4f) * 2.4f, new Color("#4a4a4a").WithAlpha(0.6f * a), 0.6f, true);
                }
                ci.Box(new Rect2(c + new Vector2(5f, 5f), new Vector2(3f, 3.6f)), new Color("#e8e2d4").WithAlpha(0.8f * a)); // 떠다니다 내려앉은 컵
                break;
            case HazardKind.DockSealFail: // 서리 낀 씰 고리
                FA.Ring(ci, c, 6f, new Color("#dff4ff").WithAlpha(0.7f * a), 1.8f, 20);
                for (int i = 0; i < 8; i++) FA.Dot(ci, c + Vector2.FromAngle(i * Mathf.Tau / 8f + h(i)) * 7.5f, 0.7f, Colors.White.WithAlpha(0.7f * a));
                break;
            case HazardKind.WreckDrift: // 화면의 잔해 궤적
                FA.Box(ci, new Rect2(c - new Vector2(8f, 5f), new Vector2(16f, 10f)), new Color("#06140c").WithAlpha(0.85f * a), 1f);
                for (int i = 0; i < 3; i++) ci.DrawLine(c + new Vector2(-7f, -4f + i * 3f), c + new Vector2(7f, -2f + i * 2.4f), new Color("#ffb347").WithAlpha(0.6f * a), 0.5f);
                break;
            case HazardKind.GammaFlash: // 창에 남은 하얀 줄
                for (int i = 0; i < 4; i++) ci.DrawLine(c + new Vector2(-8f + i * 4f, -8f), c + new Vector2(-4f + i * 4f, 8f), Colors.White.WithAlpha(0.45f * a), 0.8f, true);
                ci.Circle(c + new Vector2(2f, -2f), 3f, new Color("#fff8e0").WithAlpha(0.25f * a)); // 하얗게 탄 자리
                break;
            case HazardKind.TidalPull: // 바닥의 당김 금 (길게 갈라진 선)
                ci.Polyline(new[] { c + new Vector2(-12f, 0f), c + new Vector2(-5f, 1f), c + new Vector2(0f, -1f), c + new Vector2(6f, 1.5f), c + new Vector2(12f, 0f) }, new Color("#1a1a1a").WithAlpha(0.6f * a), 0.8f, true);
                ci.DrawLine(c + new Vector2(0f, -1f), c + new Vector2(2f, -5f), new Color("#1a1a1a").WithAlpha(0.5f * a), 0.5f, true);
                break;
            case HazardKind.MagnetarPulse: // 벽에 들러붙은 공구 뭉치
                for (int i = 0; i < 4; i++) { float an = -0.6f + i * 0.4f; ci.DrawLine(c, c + Vector2.FromAngle(an) * 8f, FA.Chrome.WithAlpha(0.9f * a), 1.2f, true); }
                FA.Dot(ci, c, 2f, FA.Steel3.WithAlpha(a));
                break;
        }
    }

    /// <summary>전조: 그 방 한구석의 작은 낌새 (사고마다 다른 기호 · 깜빡인다).</summary>
    private void DrawOmen(CanvasItem ci, HazardKind k, Vector2 c, int id)
    {
        float pu = 0.35f + 0.35f * FA.Pulse(_time + id, 2.2f);
        switch (k)
        {
            case HazardKind.DrainBackflow: case HazardKind.GreywaterJam: // 꾸르륵 거품
                for (int i = 0; i < 3; i++) { float q = Mathf.PosMod(_time * 0.8f + i / 3f, 1f); FA.Ring(ci, c + new Vector2(-2f + i * 2f, -q * 5f), 0.8f + q, new Color("#8a7a5a").WithAlpha(pu * (1f - q)), 0.5f, 8); }
                break;
            case HazardKind.FermentBurst: // 들썩이는 뚜껑
                ci.Arc(c + new Vector2(0f, Mathf.Sin(_time * 14f) * 0.8f), 3f, Mathf.Pi, Mathf.Tau, 8, new Color("#ffd27a").WithAlpha(pu), 1f, true);
                break;
            case HazardKind.BearingWhine: // 높은 소리 결
                for (int i = 0; i < 3; i++) ci.Arc(c, 2f + i * 2f, -0.6f, 0.6f, 6, new Color("#ffb347").WithAlpha(pu * (1f - i * 0.25f)), 0.6f, true);
                break;
            case HazardKind.StaticZap: // 뜨는 머리카락 · 작은 불꽃
                for (int i = 0; i < 3; i++) ci.DrawLine(c, c + Vector2.FromAngle(-Mathf.Pi * 0.5f + (i - 1) * 0.4f) * 4f, new Color("#9ad0ff").WithAlpha(pu), 0.5f, true);
                break;
            case HazardKind.HeatExhaustion: // 아지랑이
                for (int i = 0; i < 2; i++) ci.Polyline(new[] { c + new Vector2(i * 3f, 3f), c + new Vector2(i * 3f + 1f, 0f), c + new Vector2(i * 3f, -3f) }, FA.Ember.WithAlpha(pu), 0.6f, true);
                break;
            case HazardKind.PotFire: case HazardKind.MoonshineFire: // 가는 연기 한 줄
                ci.Polyline(new[] { c + new Vector2(0f, 3f), c + new Vector2(1f, 0f), c + new Vector2(-1f, -3f), c + new Vector2(0f, -6f) }, FA.SmokeGrey.WithAlpha(pu), 1f, true);
                break;
            case HazardKind.WeevilSwarm: // 가루 · 작은 점
                for (int i = 0; i < 4; i++) FA.Dot(ci, c + new Vector2(i * 1.6f - 2.4f, Mathf.Sin(_time * 3f + i) * 0.6f), 0.5f, new Color("#3a2a1a").WithAlpha(pu + 0.2f));
                break;
            case HazardKind.CatScratch: case HazardKind.CatLost: // 부푼 꼬리
                ci.Arc(c, 3f, 0.2f, 2.6f, 8, new Color("#d08a3a").WithAlpha(pu + 0.2f), 1.8f, true);
                break;
            case HazardKind.MeetingBrawl: // 날 선 말 (톱니)
                ci.Polyline(new[] { c + new Vector2(-4f, 0f), c + new Vector2(-2f, -2f), c, c + new Vector2(2f, -2f), c + new Vector2(4f, 0f) }, FA.Danger.WithAlpha(pu), 0.8f, true);
                break;
            case HazardKind.PlantTopple: case HazardKind.SpaceSick: // 기우뚱
                ci.DrawLine(c + new Vector2(-3f, 3f), c + new Vector2(3f, -3f), FA.Leaf.WithAlpha(pu + 0.2f), 1f, true);
                FA.Dot(ci, c + new Vector2(3f, -3f), 1f, FA.LeafLight.WithAlpha(pu + 0.2f));
                break;
            default: // 그 밖의 낌새: 작은 물음 고리
                FA.Ring(ci, c, 2.4f, FA.Amber.WithAlpha(pu), 0.8f, 12);
                FA.Dot(ci, c + new Vector2(0f, 3.6f), 0.6f, FA.Amber.WithAlpha(pu));
                break;
        }
    }
}
