using System;
using System.Threading.Tasks;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v19 상태 저장: F5 · 자동 저장은 세계를 통째로 찍어 둔다 (.snap) — 시드 + 관찰자 기록(.txt)도 함께 남긴다.
/// 불러오기는 찍어 둔 상태를 먼저 쓰고 (그 자리에서 바로), 판이 바뀌어 맞지 않으면 기록을 처음부터 다시 돌린다.
/// 찍는 동안(1초 남짓) 세계만 멈추고 화면은 그대로 그린다 — 프레임마다 몇 ms씩 나눠 찍고, 압축 · 파일 쓰기는 뒤에서.
/// </summary>
public partial class Main
{
    public static string SnapPath => System.IO.Path.ChangeExtension(SavePath, ".snap");
    public static string AutoPath => System.IO.Path.Combine(OS.GetUserDataDir(), "shipsim-auto.snap");
    public static string AutoTextPath => System.IO.Path.ChangeExtension(AutoPath, ".txt");

    /// <summary>다음 장면에서 되살릴 상태 (F9 · ⇧F9 → 장면을 다시 띄운다).</summary>
    private static byte[]? _pendingSnap;

    /// <summary>찍는 중 (세계를 돌리지 않는다).</summary>
    public bool Saving => _saver != null;
    private StateSave.Saver? _saver;
    private string? _saverPath, _saverText;
    private bool _saverAuto;
    private long _saverTick;
    private int _saverCmds;
    private Task? _saveWrite;
    private ulong _lastAutoMsec;
    private long _lastAutoTick;

    /// <summary>자동 저장 간격 (실제 시간, 기본 5분 · 명령줄 --autosave=초) — 세계가 10분 넘게 흘렀을 때만.</summary>
    private static ulong _autoEveryMsec = 5 * 60 * 1000;

    /// <summary>찍기 시작: 세계를 멈추고 프레임마다 나눠 찍는다.</summary>
    private void BeginSave(string path, string textPath, bool auto)
    {
        if (_saver != null || Replaying != null) return;
        if (_saveWrite is { IsCompleted: false }) return; // 앞 저장을 아직 쓰는 중
        _saverText = textPath;
        _saverPath = path;
        _saverAuto = auto;
        _saverTick = Sim.Tick;
        _saverCmds = Sim.Commands.Count;
        _saverReplay = Core.SaveGame.Write(Sim);
        _saver = StateSave.Begin(Sim);
        if (!auto) ShowNotice("저장하는 중…");
    }

    private string? _saverReplay;
    private static volatile string? _saveError; // 뒤에서 파일을 쓰다 실패한 까닭 (다음 프레임에 알린다)

    /// <summary>_Process 앞에서: 찍는 중이면 이번 프레임 몫만 찍고 true (세계를 돌리지 않는다).</summary>
    private bool StepSave()
    {
        if (_saver == null) return false;
        // 찍는 사이 관찰자가 무언가 했으면 (기록이 늘었으면) 이 찍기는 버리고 다음에
        if (Sim.Tick != _saverTick || Sim.Commands.Count != _saverCmds) { _saver = null; return false; }
        if (!_saver.Step(6.0)) return true;
        var saver = _saver;
        string path = _saverPath!, textPath = _saverText!, text = _saverReplay!;
        bool auto = _saverAuto;
        string when = $"{Sim.Day}일차 {Sim.Clock}";
        _saver = null;
        _saveWrite = Task.Run(() =>
        {
            try
            {
                byte[] bytes = saver.Pack();
                // 다 쓴 뒤에 바꿔 넣는다 (쓰다 끊겨도 앞 저장은 남는다) · 자동 저장은 하나 앞 것도 남긴다
                WriteAtomic(path, bytes, auto);
                WriteAtomic(textPath, System.Text.Encoding.UTF8.GetBytes(text), auto);
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { _saveError = e.Message; }
        });
        if (!auto) ShowNotice($"저장했다 — {when} · 관찰자 기록 {Sim.Commands.Count}줄 · 항해 번호 {Sim.Seed} (F9로 그 자리에서 이어 간다)");
        GD.Print($"{(auto ? "autosaved" : "saved")}: {path} ({saver.Objects} objects · {saver.Ms:0}ms)");
        return true;
    }

    private static void WriteAtomic(string path, byte[] bytes, bool keepOne)
    {
        string tmp = path + ".tmp";
        System.IO.File.WriteAllBytes(tmp, bytes);
        if (keepOne && System.IO.File.Exists(path)) System.IO.File.Copy(path, path + ".1", overwrite: true);
        System.IO.File.Move(tmp, path, overwrite: true);
    }

    /// <summary>자동 저장: 실제 시간 5분마다 (세계가 10분 넘게 흘렀을 때). 화면 찍기 · 영상 · 성능 재기 실행에서는 하지 않는다.</summary>
    private void AutoSaveTick()
    {
        if (_saveError is string err) { _saveError = null; ShowNotice($"저장 파일을 쓰지 못했다 — {err}"); }
        if (_saver != null || Replaying != null || _noAutoSave) return;
        ulong now = Time.GetTicksMsec();
        if (_lastAutoMsec == 0) { _lastAutoMsec = now; _lastAutoTick = Sim.Tick; return; }
        if (now - _lastAutoMsec < _autoEveryMsec || Sim.Tick - _lastAutoTick < SimTime.Minutes(10)) return; // 멈춰 둔 동안은 같은 상태를 또 찍지 않는다
        _lastAutoMsec = now;
        _lastAutoTick = Sim.Tick;
        BeginSave(AutoPath, AutoTextPath, auto: true);
    }

    private bool _noAutoSave;

    /// <summary>⇧F9: 자동 저장을 불러온다.</summary>
    public void LoadAutoSave()
    {
        if (!System.IO.File.Exists(AutoPath)) { ShowNotice("자동 저장이 아직 없다 (5분쯤 돌리면 저절로 저장된다)"); return; }
        LoadGame(AutoTextPath, AutoPath);
    }

    /// <summary>장면을 띄울 때: 찍어 둔 상태가 있으면 되살린다. 맞지 않으면(판이 바뀌었다) null과 까닭 — 그때는 기록을 다시 돌린다.</summary>
    private static World? TrySnapshot(out string? why)
    {
        why = null;
        if (_pendingSnap == null) return null;
        var bytes = _pendingSnap;
        _pendingSnap = null;
        // 판이 바뀌었거나 파일이 망가졌으면 어떤 예외든 기록으로 다시 돌린다 (메모리가 모자란 것만 그대로)
        try { return StateSave.Read(bytes); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            why = e is StateSave.MismatchException or FormatException ? e.Message : $"상태 저장을 읽지 못했다 ({e.GetType().Name})";
            return null;
        }
    }
}
