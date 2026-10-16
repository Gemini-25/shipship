using System;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// 상태 저장 점검 (--snaptest): 세계를 몇 시간 돌린 뒤 통째로 찍고 → 되살려 → 원래 세계와 나란히 돌린다.
/// · 되살린 그 자리에서 지문이 같고, 다시 찍은 바이트도 같아야 한다 (빠진 상태가 없다)
/// · 그 뒤 1분마다 지문이 같아야 한다 (같은 역사가 이어진다) · 끝에 찍은 바이트도 같아야 한다
/// --snapfile=경로 : 찍은 것을 파일로 남긴다 · --snapload=경로 : 다른 프로세스에서 찍은 것을 되살려 이어 돌리며 지문을 찍는다
/// </summary>
public static partial class Program
{
    private static readonly string[] SnapShips = { "Mirinae", "Hanbit", "Busitdol", "Saeteo" };

    private static int RunSnapTest(int seed, int hours, int after, string? only, string? file)
    {
        Console.WriteLine($"상태 저장 점검 · 시드 {seed} · {hours}시간 돌리고 찍은 뒤 {after}시간 나란히\n");
        int bad = 0;
        foreach (var ship in only != null ? new[] { only } : SnapShips)
        {
            var a = World.CreateDefault(seed, 0, ship);
            var rng = new Rng(seed ^ 0x51a9);
            for (int h = 0; h < hours; h++)
            {
                if (h % 5 == 2) Scenarios.RandomIncident(a, rng); // 사고 한가운데서도 찍힌다
                Run(a, SimTime.Hours(1));
            }
            Run(a, 777); // 시 · 분 경계가 아닌 틱
            SaveGame.StateHash(a); // 지문 계산이 늦게 만드는 것(기술 그물 난수)을 먼저 — 찍은 뒤 지문을 보면 원본만 달라진다
            var bytes = StateSave.Write(a, out var raw1);
            var ws = StateSave.LastWrite;
            if (file != null) System.IO.File.WriteAllBytes(file, bytes);
            World b;
            try { b = StateSave.Read(bytes); }
            catch (Exception e)
            {
                bad++;
                Console.WriteLine($"  ✘ {ship}: 되살리지 못했다 — {e.GetType().Name}: {e.Message}\n{e.StackTrace?.Split('\n').Take(6).Aggregate((x, y) => x + "\n" + y)}");
                continue;
            }
            var rs = StateSave.LastRead;
            bool hash0 = SaveGame.StateHash(a) == SaveGame.StateHash(b);
            StateSave.Write(b, out var raw2);
            var warm = StateSave.LastWrite;
            int diff0 = FirstDiff(raw1, raw2);
            if (diff0 >= 0) Console.WriteLine($"    다시 찍은 바이트가 다른 곳: {StateSave.Describe(a, diff0)} ↔ {StateSave.Describe(b, diff0)}\n      {Bytes(raw1, diff0)}\n      {Bytes(raw2, diff0)}");
            long diverged = -1;
            long end = b.Tick + SimTime.Hours(after), start = b.Tick;
            while (a.Tick < end)
            {
                for (int k = 0; k < SimTime.Minutes(1); k++) { a.Step(); b.Step(); }
                if (diverged < 0 && SaveGame.StateHash(a) != SaveGame.StateHash(b)) diverged = a.Tick;
                if (file != null && (a.Tick - start) % SimTime.Hours(1) == 0) Console.WriteLine($"+{(a.Tick - start) / SimTime.Hours(1)}h {SaveGame.StateHash(a):x8}");
            }
            StateSave.Write(a, out var ra);
            StateSave.Write(b, out var rb);
            int diffEnd = FirstDiff(ra, rb);
            if (diffEnd >= 0) Console.WriteLine($"    끝에 찍은 바이트가 다른 곳: {StateSave.Describe(a, diffEnd)} ↔ {StateSave.Describe(b, diffEnd)}\n      {Bytes(ra, diffEnd)}\n      {Bytes(rb, diffEnd)}");
            bool ok = hash0 && diff0 < 0 && diverged < 0 && diffEnd < 0;
            if (!ok) bad++;
            Console.WriteLine($"  {(ok ? "✔" : "✘")} {ship}: {a.Day}일차 · 객체 {ws.objects:N0} · 원본 {ws.rawBytes / 1024:N0}KB → 압축 {ws.packedBytes / 1024:N0}KB · 찍기 {ws.ms:0}ms · 되살리기 {rs.ms:0}ms"
                + $" · 지문 {(hash0 ? "같음" : "다름")} · 다시 찍은 바이트 {(diff0 < 0 ? "같음" : $"다름@{diff0}")}"
                + $" · {after}시간 나란히 {(diverged < 0 ? "같음" : $"갈라짐 {SimTime.Clock(diverged)}")} · 끝 바이트 {(diffEnd < 0 ? "같음" : $"다름@{diffEnd}")}"
                + $" · 두 번째 찍기 {warm.ms:0}ms" + (ws.dropped > 0 ? $" · 떼어 낸 화면 참조 {ws.dropped}" : ""));
        }
        Console.WriteLine(bad == 0 ? "\n✔ 상태 저장 — 되살린 세계가 같은 역사를 잇는다" : $"\n✘ {bad}척 어긋남");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>다른 프로세스에서 찍은 상태를 되살려 이어 돌리며 1시간마다 지문을 찍는다 (같은 시드를 처음부터 돌린 --dethash 출력과 견준다).</summary>
    private static int RunSnapLoad(string path, int hours)
    {
        var b = StateSave.Read(System.IO.File.ReadAllBytes(path));
        Console.WriteLine($"되살림 {b.Ship.Name} {b.Day}일차 {b.Clock} · 틱 {b.Tick} · 지문 {SaveGame.StateHash(b):x8} · {StateSave.LastRead.ms:0}ms");
        for (int h = 1; h <= hours; h++)
        {
            Run(b, SimTime.Hours(1));
            Console.WriteLine($"+{h}h {SaveGame.StateHash(b):x8}");
        }
        return 0;
    }

    private static string Bytes(byte[] x, int at) => string.Join(" ", Enumerable.Range(Math.Max(0, at - 12), 28).Where(i => i < x.Length).Select(i => (i == at ? "[" : "") + x[i].ToString("x2") + (i == at ? "]" : "")));

    private static int FirstDiff(byte[] x, byte[] y)
    {
        int n = Math.Min(x.Length, y.Length);
        for (int i = 0; i < n; i++) if (x[i] != y[i]) return i;
        return x.Length == y.Length ? -1 : n;
    }
}
