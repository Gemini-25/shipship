using System;
using System.Linq;
using ShipSim.Core;

// v22 끊긴 일 이어받기 (--resumetest): 작업 목록의 일을 하다 끊기면 진척이 일에 남고, 다음 사람이 그 자리부터 잇는다.
public static partial class Program
{
    private static int RunResumeTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"끊긴 일 이어받기 점검 (v22) · 시드 {seed}\n");
        try
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            NoRobots(w); // 같은 일을 로봇이 함께 채우지 않게 (사람끼리만 본다)
            Run(w, SimTime.Hours(2));
            var crew = w.Crew.Where(x => !x.Dead && !x.IsChild && !x.Outside).OrderBy(x => x.Id).ToList();
            var a = crew[0];
            var b = crew[1];
            for (int h = 0; h < 24 && !w.Board.Open.Any(); h++) Run(w, SimTime.Hours(1)); // 작업 목록에 일이 올라올 때까지
            var o = w.Board.Open.First();
            o.Progress = 0f;
            Job Make() => new Job(null, "시험 — 긴 손일", new Toil[] { new WorkToil(2f, Skill.Mechanics, null) }) { Order = o };

            // 1) 앞사람: 한 시간 하다 끊긴다
            o.Assignee = a;
            var ja = Make();
            Force(w, a, ja);
            for (int k = 0; k < SimTime.Hours(1); k++) { if (a.Job != ja && !o.Closed) { ja = Make(); Force(w, a, ja); } w.Step(); } // 끼니로 잠깐 끊겨도 다시 붙인다
            float mid = o.Progress;
            a.EndJob(w, ToilStatus.Interrupted);
            Check("남김 — 하다 끊기면 진척이 작업 목록의 일에 남는다", mid > 0.15f && mid < 0.95f && MathF.Abs(o.Progress - mid) < 1e-4f,
                $"{o.Title}: 한 시간 뒤 {mid * 100:0}% · 끊긴 뒤 {o.Progress * 100:0}%");

            // 2) 다음 사람: 그 자리부터 잇는다 (처음부터가 아니다)
            o.Assignee = b;
            var job = Make();
            Force(w, b, job);
            Run(w, 2);
            float start = job.Current?.Progress ?? -1f;
            Check("잇기 — 다음 사람이 그 자리부터 잇고 · 기록에 남는다", start >= mid - 0.01f && w.Log.Entries.Any(e => e.Text.Contains("된 데서 잇는다")),
                $"{b.Name} 시작 {start * 100:0}% (앞사람 {mid * 100:0}%) · {w.Log.Entries.LastOrDefault(e => e.Text.Contains("잇는다")).Text}");

            // 3) 끝나면 0으로 돌아간다 (되풀이하는 일이 공짜로 끝나지 않게)
            // (끼니 · 졸음으로 또 끊기면 다시 붙인다 — 손을 댄 시간만 센다)
            int worked = 0, breaks = 0;
            bool finished = false;
            for (int k = 0; k < SimTime.Hours(8); k++)
            {
                if (b.Job != job)
                {
                    if (o.Progress < 0.01f) { finished = true; break; }
                    breaks++;
                    job = Make();
                    Force(w, b, job);
                }
                if (job.Current is WorkToil) worked++;
                w.Step();
            }
            Check("끝 — 남은 만큼만 일하고 끝나며 · 끝나면 진척이 0으로 돌아간다", finished && worked < SimTime.Hours(2f),
                $"손댄 시간 {worked / (float)SimTime.TicksPerHour:0.0}시간 (처음부터면 최대 3.2시간) · 또 끊김 {breaks} · 진척 {o.Progress * 100:0}%");

            // 4) 끄면 예전처럼 처음부터
            WorkToil.AutoResume = false;
            o.Progress = 0.5f;
            var job2 = Make();
            Force(w, b, job2);
            Run(w, 2);
            Check("끄기 — 이어받기를 끄면 예전처럼 처음부터 한다", (job2.Current?.Progress ?? 1f) < 0.05f, $"{(job2.Current?.Progress ?? -1f) * 100:0}%");
        }
        catch (Exception e) { Console.WriteLine($"  ✘ 예외: {e}"); _fails++; }
        finally { WorkToil.AutoResume = true; }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
