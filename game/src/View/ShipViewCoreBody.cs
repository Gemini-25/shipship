using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.20 주컴퓨터 몸 (배 화면 — 읽기만): 랙 옆에 붙은 것들이 상태마다 다르게 보인다.
///  · 비상 전지(왼쪽 아래): 셀 넷 · 채운 만큼 초록, 전기가 끊겨 쓰는 중이면 주황으로 숨쉬며 줄어든다 · 바닥나면 빈 셀에 붉은 점.
///  · 예비 연산기(왼쪽 위 작은 판): 평소엔 초록 심장 박동 하나 · 본체가 멎고 붙잡으면 주황 불 셋이 차례로 · 재부팅 단계마다 켜진 불 수가 는다.
///  · 느리게 돎(달아오름): 칼날 반을 어둡게 덮고 · 랙 위에 느린 물결 표시.
///  · 제 연산 줄임(절전): 칼날 위를 푸른 막이 덮고 위에 초승달.
/// </summary>
public partial class ShipView
{
    private void PaintCoreBody(CanvasItem ci, Furniture f, Rect2 r, float t)
    {
        var a = _world.Automation;
        if (!a.Present) return;
        var core = a.Core;
        bool close = Zoom > 1.1f;
        // 느리게 돎: 칼날 반을 어둡게 (번갈아) · 위에 느린 물결
        if (core.SafeMode && a.MainOnline)
        {
            float half = r.Size.X * 0.5f;
            for (int k = 0; k < 2; k++)
            {
                var rack = new Rect2(r.Position.X + 6 + k * half, r.Position.Y + 6, half - 9, r.Size.Y - 12);
                for (int row = 0; row < 5; row++)
                    if ((row + k) % 2 == 0) ci.Box(new Rect2(rack.Position.X, rack.Position.Y + row * rack.Size.Y / 5f, rack.Size.X, rack.Size.Y / 5f), new Color(0.02f, 0.03f, 0.05f, 0.55f));
            }
            var wpts = new Vector2[9];
            for (int i = 0; i < 9; i++) wpts[i] = new Vector2(r.Position.X + 6 + i * (r.Size.X - 12) / 8f, r.Position.Y - 4f + Mathf.Sin(t * 1.2f + i * 0.9f) * 1.5f);
            ci.Polyline(wpts, new Color("#ff9a6b").WithAlpha(0.7f), 1f, true);
        }
        // 제 연산 줄임: 푸른 막 + 초승달
        if (core.SelfSaving)
        {
            ci.Box(r.Grow(-5f), new Color(0.2f, 0.35f, 0.6f, 0.18f));
            var mc = new Vector2(r.End.X - 10f, r.Position.Y + 9f);
            ci.Circle(mc, 3.2f, new Color("#9fc8ff").WithAlpha(0.85f), true, -1f, true);
            ci.Circle(mc + new Vector2(1.4f, -0.9f), 2.8f, new Color("#0c121c"), true, -1f, true);
        }
        // 비상 전지 (왼쪽 아래 바깥): 셀 넷
        float ups = core.Ups < 0 ? core.UpsCapacity : core.Ups;
        float frac = Mathf.Clamp(ups / Mathf.Max(1f, core.UpsCapacity), 0f, 1f);
        var pack = new Rect2(r.Position.X - 9f, r.End.Y - 16f, 7f, 14f);
        ci.Box(pack, new Color("#0c1118"));
        ci.Box(pack, new Color("#3a4558"), false, 0.8f);
        ci.Box(new Rect2(pack.Position.X + 2.2f, pack.Position.Y - 1.4f, 2.6f, 1.4f), new Color("#3a4558"));
        for (int k = 0; k < 4; k++)
        {
            var cell = new Rect2(pack.Position.X + 1.2f, pack.End.Y - 1.2f - (k + 1) * 3f, pack.Size.X - 2.4f, 2.4f);
            bool lit = frac > k / 4f + 0.01f;
            var col = core.OnUps ? new Color("#ffb347").WithAlpha(lit ? 0.55f + 0.4f * Mathf.Sin(t * 4f - k) : 0.1f) : new Color("#6ee7b7").WithAlpha(lit ? 0.75f : 0.1f);
            ci.Box(cell, col);
        }
        if (core.OnUps && frac < 0.05f) ci.Circle(pack.GetCenter(), 1.4f, Palette.Danger.WithAlpha(Mathf.Sin(t * 8f) > 0 ? 1f : 0.3f), true, -1f, true);
        if (core.OnUps && close) Gfx.TextCentered(ci, Fonts.Bold, new Vector2(pack.GetCenter().X, pack.Position.Y - 6f), $"{ups:0}분", 7, new Color("#ffb347"));
        // 예비 연산기 (왼쪽 위 바깥 작은 판)
        var bb = new Rect2(r.Position.X - 9f, r.Position.Y + 3f, 7f, 11f);
        ci.Box(bb, new Color("#0c1118"));
        ci.Box(bb, core.BackupCore ? new Color("#ffb347").WithAlpha(0.8f) : new Color("#3a4558"), false, 0.8f);
        if (core.BackupCore)
        {
            int lit = a.Rebooting ? Mathf.Clamp(core.RebootStage, 1, 3) : 3;
            for (int k = 0; k < 3; k++)
            {
                bool on = k < lit && Mathf.PosMod(t * 2f - k * 0.3f, 1f) < 0.7f;
                ci.Circle(new Vector2(bb.GetCenter().X, bb.Position.Y + 2.5f + k * 3f), 0.9f, new Color("#ffb347").WithAlpha(on ? 1f : 0.2f), true, -1f, true);
            }
            if (close) Gfx.TextCentered(ci, Fonts.Bold, new Vector2(bb.GetCenter().X - 10f, bb.GetCenter().Y + 3f), "예비", 7, new Color("#ffb347"));
        }
        else
        {
            float beat = Mathf.PosMod(t * 1.1f, 1f);
            ci.Circle(bb.GetCenter(), 0.8f + (beat < 0.12f ? 0.6f : 0f), new Color("#6ee7b7").WithAlpha(beat < 0.12f ? 0.9f : 0.35f), true, -1f, true);
        }
    }
}
