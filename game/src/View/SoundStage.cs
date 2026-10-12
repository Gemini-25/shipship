using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Art;
using ShipSim.Core;

namespace ShipSim.View;

// 도트 8단계: 연출 장부(ShipSim.Art.Staging)대로 — 사고가 처음 나면 그 갈래의 시작 소리 한 번 + 카메라(살짝 흔들 · 흔들 · 흔들고 잠깐 느리게).
// 경보 사이렌 · 폭발음 · 충돌음은 따로 울린다 (겹치면 시작 소리는 작게).
public partial class SoundSystem
{
    private int[]? _seenHazards;
    private readonly Dictionary<StageSound, AudioStreamWav> _stageWav = new();

    private void ProcessStaging(World w, bool quiet)
    {
        var count = w.Hazards.Count;
        if (_seenHazards == null || _seenHazards.Length != count.Length) { _seenHazards = (int[])count.Clone(); return; }
        for (int i = 0; i < count.Length; i++)
        {
            if (count[i] <= _seenHazards[i]) { _seenHazards[i] = count[i]; continue; } // 되감기 · 불러오기면 맞춰 둔다
            _seenHazards[i] = count[i];
            var cue = Staging.Of((HazardKind)i);
            if (quiet) continue;
            if (Settings.Effects && cue.Sound != StageSound.None)
            {
                if (!_stageWav.TryGetValue(cue.Sound, out var wav)) _stageWav[cue.Sound] = wav = Make(StageSounds.Make(cue.Sound));
                Play(wav, cue.Sound is StageSound.Siren ? -9f : -7f);
            }
            if (ShipView.BlastShake)
                switch (cue.Cam)
                {
                    case StageCam.Nudge: _main.Camera.Shake(3f); break;
                    case StageCam.Shake: _main.Camera.Shake(8f); break;
                    case StageCam.Slow: _main.Camera.Shake(6f); _main.Impact(600); break;
                }
        }
    }
}
