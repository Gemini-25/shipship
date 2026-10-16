using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.6 음악 — 저작권 없는 짧은 소리를 코드로 만든다 (파일 추가 없음).
// ① 상황 음악 네 가락(평시 · 추모 · 긴장 · 위기)을 배의 분위기(Core/MusicSystem.Mood)에 맞춰 천천히 바꿔 튼다.
// ② 배 안에서 트는 음악: 카메라가 보는 방에서 들리는 만큼 — 그 방이면 또렷하게, 벽 · 문 너머면 먹먹한 판(낮은 소리만 남긴 것)으로.
//    크기 · 먹먹함은 Core 소리 전파(Hearing)가 정한 값을 그대로 쓴다.

public partial class SoundSystem
{
    private readonly AudioStreamPlayer[] _mood = new AudioStreamPlayer[4];
    private readonly Dictionary<int, (AudioStreamPlayer clear, AudioStreamPlayer muffled)> _cabin = new();
    private bool _musicInit;

    private void InitMusic()
    {
        _musicInit = true;
        _mood[(int)MusicMood.Calm] = Loop(MoodLoop(MusicMood.Calm));
        _mood[(int)MusicMood.Mourning] = Loop(MoodLoop(MusicMood.Mourning));
        _mood[(int)MusicMood.Tension] = Loop(MoodLoop(MusicMood.Tension));
        _mood[(int)MusicMood.Crisis] = Loop(MoodLoop(MusicMood.Crisis));
    }

    private (AudioStreamPlayer, AudioStreamPlayer) Cabin(int genre)
    {
        if (_cabin.TryGetValue(genre, out var got)) return got;
        var s = GenreLoop(genre);
        var pair = (Loop(s), Loop(Muffle(s)));
        _cabin[genre] = pair;
        return pair;
    }

    private static void FadeTo(AudioStreamPlayer p, float target, double delta, float speed = 0.35f)
    {
        float now = p.Playing ? Mathf.DbToLinear(p.VolumeDb) : 0f;
        float next = Mathf.MoveToward(now, target, (float)delta * speed);
        if (next <= 0.001f) { if (p.Playing) p.Stop(); return; }
        if (!p.Playing) p.Play();
        p.VolumeDb = Mathf.LinearToDb(next);
    }

    /// <summary>음악 (Sound._Process에서 부른다).</summary>
    private void ProcessMusic(World w, double delta, bool quiet)
    {
        if (!_musicInit) InitMusic();
        bool on = !quiet && !_main.Paused;
        var mood = w.Music.Mood;
        for (int i = 0; i < 4; i++)
            FadeTo(_mood[i], on && i == (int)mood ? 0.22f * Settings.MusicVolume : 0f, delta, i == (int)MusicMood.Crisis ? 0.8f : 0.25f);
        // 배 안 음악: 카메라가 보는 방에서 들리는 곡 하나
        CabinTune? best = null;
        float level = 0f;
        bool muffled = false;
        var cam = _main.Camera.GetScreenCenterPosition();
        var cell = new Cell(Mathf.FloorToInt(cam.X / ShipView.T), Mathf.FloorToInt(cam.Y / ShipView.T));
        if (on && w.Music.Playing.Count > 0 && w.Ship.RoomAt(cell) is Room focus)
        {
            foreach (var h in w.Hearing.In(focus))
            {
                if (h.Src >= w.Hearing.Sources.Count) continue;
                var s = w.Hearing.Sources[h.Src];
                if (s.Kind != ShipSim.Core.Noise.Music || h.Level <= level) continue;
                foreach (var t in w.Music.Playing)
                    if (t.Owner == s.Owner) { best = t; level = h.Level; muffled = h.Muffled || s.Room != focus.Id; }
            }
        }
        float zoomGain = Mathf.Clamp(_main.Camera.Zoom.X * 0.9f, 0.25f, 1f);
        foreach (var (genre, (clear, muf)) in _cabin)
        {
            bool mine = best != null && best.Genre == genre;
            float v = mine ? Mathf.Clamp(level * 1.4f, 0f, 1f) * Settings.CabinVolume * 0.5f * zoomGain : 0f;
            FadeTo(clear, mine && !muffled ? v : 0f, delta, 0.6f);
            FadeTo(muf, mine && muffled ? v * 1.4f : 0f, delta, 0.6f);
        }
        if (best != null && !_cabin.ContainsKey(best.Genre)) Cabin(best.Genre);
    }

    // ── 가락 만들기 ──

    private static float Note(int semi) => 220f * Mathf.Pow(2f, semi / 12f); // 0 = A3

    /// <summary>상황 음악 한 바퀴 (약 9초 · 끝과 처음이 이어진다).</summary>
    private static float[] MoodLoop(MusicMood m)
    {
        const float sec = 9.6f;
        int n = (int)(Rate * sec);
        var o = new float[n];
        int[][] chords;
        float bpm;
        switch (m)
        {
            case MusicMood.Calm: chords = new[] { new[] { -9, -5, -2, 2 }, new[] { -12, -5, -0, 3 }, new[] { -7, -3, 0, 4 }, new[] { -10, -5, -1, 2 } }; bpm = 50f; break;
            case MusicMood.Mourning: chords = new[] { new[] { -7, -4, 0 }, new[] { -11, -7, -4 }, new[] { -9, -5, -2 }, new[] { -7, -4, 0 } }; bpm = 40f; break;
            case MusicMood.Tension: chords = new[] { new[] { -19, -12, -6 }, new[] { -19, -12, -5 }, new[] { -18, -12, -6 }, new[] { -19, -13, -6 } }; bpm = 75f; break;
            default: chords = new[] { new[] { -19, -12, -7 }, new[] { -18, -12, -6 }, new[] { -19, -12, -7 }, new[] { -17, -11, -6 } }; bpm = 125f; break;
        }
        float bar = sec / chords.Length;
        float beat = 60f / bpm;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            int ci = Math.Min(chords.Length - 1, (int)(t / bar));
            float tb = t - ci * bar;
            float env = Mathf.Min(1f, tb / 0.8f) * Mathf.Min(1f, (bar - tb) / 0.8f);
            float v = 0f;
            foreach (var s in chords[ci])
            {
                float f = Note(s);
                v += Mathf.Sin(Mathf.Tau * f * t) * 0.5f + Mathf.Sin(Mathf.Tau * f * 2.003f * t) * 0.12f;
            }
            v *= env / chords[ci].Length;
            float bt = Mathf.PosMod(t, beat);
            switch (m)
            {
                case MusicMood.Calm: // 느린 아르페지오
                {
                    int k = (int)(t / (beat * 0.5f)) % chords[ci].Length;
                    float at = Mathf.PosMod(t, beat * 0.5f);
                    v += Mathf.Sin(Mathf.Tau * Note(chords[ci][k] + 12) * t) * Mathf.Exp(-at * 3f) * 0.18f;
                    break;
                }
                case MusicMood.Mourning: // 긴 종소리
                    v += Mathf.Sin(Mathf.Tau * Note(chords[ci][0] + 24) * t) * Mathf.Exp(-tb * 1.2f) * 0.16f;
                    break;
                case MusicMood.Tension: // 심장 고동
                    v += Mathf.Sin(Mathf.Tau * 55f * t) * (Mathf.Exp(-bt * 14f) + 0.6f * Mathf.Exp(-Mathf.Max(0f, bt - 0.22f) * 14f) * (bt > 0.22f ? 1f : 0f)) * 0.5f;
                    break;
                default: // 빠른 맥박 · 떨리는 높은 소리
                    v += Mathf.Sign(Mathf.Sin(Mathf.Tau * 55f * t)) * Mathf.Exp(-bt * 10f) * 0.22f;
                    v += Mathf.Sin(Mathf.Tau * Note(chords[ci][2] + 24) * t) * (0.5f + 0.5f * Mathf.Sin(Mathf.Tau * 7f * t)) * 0.06f;
                    break;
            }
            o[i] = v * 0.55f;
        }
        return o;
    }

    /// <summary>배 안 노래 한 바퀴: 갈래마다 박자 · 음색 · 음계가 다르다.</summary>
    private static float[] GenreLoop(int genre)
    {
        const float sec = 7.2f;
        int n = (int)(Rate * sec);
        var o = new float[n];
        int[] scale = genre switch { 4 => new[] { 0, 2, 4, 7, 9, 12 }, 3 => new[] { 0, 3, 5, 7, 10, 12 }, 2 => new[] { 0, 4, 7, 10, 14, 9 }, _ => new[] { 0, 2, 4, 5, 7, 9, 11, 12 } };
        float bpm = genre switch { 0 => 72f, 1 => 96f, 2 => 112f, 3 => 128f, 4 => 100f, _ => 60f };
        float step = 60f / bpm / (genre == 3 ? 4f : 2f);
        int seq = (int)(sec / step);
        var notes = new int[seq];
        uint h = (uint)(genre * 7919 + 13);
        for (int k = 0; k < seq; k++) { h = h * 1664525u + 1013904223u; notes[k] = scale[(int)(h >> 24) % scale.Length] + (genre == 5 ? -12 : 0); }
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            int k = Math.Min(seq - 1, (int)(t / step));
            float at = t - k * step;
            float f = Note(notes[k] + 3);
            float v = genre switch
            {
                0 => (Mathf.Sin(Mathf.Tau * f * t) + 0.3f * Mathf.Sin(Mathf.Tau * f * 2f * t) + 0.1f * Mathf.Sin(Mathf.Tau * f * 3f * t)) * Mathf.Exp(-at * 4f), // 피아노
                1 => (Mathf.Abs(Mathf.PosMod(f * t, 1f) * 4f - 2f) - 1f) * Mathf.Min(1f, at * 30f) * Mathf.Exp(-at * 2f) * (k % 3 == 0 ? 1f : 0.6f), // 옛 노래 (세 박자)
                2 => Mathf.Sin(Mathf.Tau * f * t) * Mathf.Exp(-at * 3f) * 0.7f + Mathf.Sin(Mathf.Tau * Note(notes[k] - 21) * t) * Mathf.Exp(-at * 6f) * ((k % 2 == 0) ? 0.8f : 0.4f), // 재즈 (걷는 베이스)
                3 => Mathf.Sign(Mathf.Sin(Mathf.Tau * f * t)) * 0.35f * Mathf.Exp(-at * 9f), // 전자음
                4 => Mathf.Sin(Mathf.Tau * f * t + Mathf.Sin(Mathf.Tau * f * 2f * t) * Mathf.Exp(-at * 6f) * 2f) * Mathf.Exp(-at * 5f), // 민요 (뜯는 줄)
                _ => (Mathf.Sin(Mathf.Tau * f * t * (1f + 0.004f * Mathf.Sin(Mathf.Tau * 5f * t))) + 0.5f * Mathf.Sin(Mathf.Tau * f * 1.5f * t) + 0.3f * Mathf.Sin(Mathf.Tau * f * 2f * t)) * 0.45f * Mathf.Min(1f, at * 4f), // 합창
            };
            o[i] = v * 0.4f * Mathf.Min(1f, Mathf.Min(t, sec - t) * 20f);
        }
        return o;
    }

    /// <summary>벽 너머 판: 높은 소리를 깎고(한 극 저역 통과 두 번) 조금 작게.</summary>
    private static float[] Muffle(float[] s)
    {
        var o = new float[s.Length];
        float a = 0.06f, y1 = 0f, y2 = 0f;
        for (int i = 0; i < s.Length; i++) { y1 += a * (s[i] - y1); y2 += a * (y1 - y2); o[i] = y2 * 1.6f; }
        return o;
    }
}
