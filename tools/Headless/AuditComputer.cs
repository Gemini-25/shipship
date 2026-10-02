using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.23 점검 — 주컴퓨터 쪽 값 (지금 있는 공개 값만 · 리플렉션 없이).
// 주컴퓨터 담당이 API 를 바꾸면 이 파일만 고치면 된다. 못 재는 값은 보고서에 "측정 안 됨".
public static partial class Program
{
    private sealed class AuditCompProbe
    {
        private int _lastAct, _lastDecision, _gradeSum = -1;
        private readonly HashSet<string> _seen = new();

        public void Minute(World w, AComp c, Dictionary<string, int> hits, List<string> ex)
        {
            var a = w.Automation;
            if (!a.Present) return;
            c.Measured = true;
            if (!a.MainOnline || a.Rebooting) c.OfflineMin++;
            int gs = a.Core.GradeSum;
            if (_gradeSum >= 0 && gs < _gradeSum) c.GradeDrops++;
            _gradeSum = gs;
            foreach (var act in a.Book.Acts)
            {
                if (act.Id <= _lastAct) continue;
                _lastAct = act.Id;
                if (string.IsNullOrWhiteSpace(act.Request)) c.Remote++; else c.Asked++;
                AuditScanText(act.Observe, "컴퓨터 기록", hits, ex, _seen);
                AuditScanText(act.Judge, "컴퓨터 기록", hits, ex, _seen);
                AuditScanText(act.Act, "컴퓨터 기록", hits, ex, _seen);
                AuditScanText(act.Request, "컴퓨터 기록", hits, ex, _seen);
            }
            foreach (var d in a.Foresee.Timeline)
            {
                if (d.Id <= _lastDecision) continue;
                _lastDecision = d.Id;
                c.Decisions++;
                c.Options += d.Options.Count;
            }
        }

        public void End(World w, AComp c, Dictionary<string, int> hits, List<string> ex)
        {
            var a = w.Automation;
            if (!a.Present) { c.Note = "주컴퓨터 없음"; return; }
            c.Total = a.Book.Total; c.Right = a.Book.Right; c.Wrong = a.Book.Wrong; c.Held = a.Book.Held;
            c.Reboots = a.Reboots; c.Overheats = a.Overheats;
            foreach (var act in a.Book.Acts) AuditScanText(act.Result, "컴퓨터 기록", hits, ex, _seen);
        }
    }
}
