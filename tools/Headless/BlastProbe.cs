using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// 도트 7단계: 사고가 실제로 나는지 — 게임 기본값(이야기꾼)으로 며칠 돌려 사고 · 폭발 · 불 · 사망을 센다.
//   days 시드 --blastprobe [--ships=Hanbit,Mirinae,Saeteo] [--persona=1] [--random=0]
public static partial class Program
{
    private static int RunBlastProbe(int days, int seed, string[] args)
    {
        string ships = args.FirstOrDefault(a => a.StartsWith("--ships="))?.Split('=')[1] ?? "Hanbit,Mirinae,Saeteo";
        float persona = float.TryParse(args.FirstOrDefault(a => a.StartsWith("--persona="))?.Split('=')[1], out var p) ? p : 1f;
        float random = float.TryParse(args.FirstOrDefault(a => a.StartsWith("--random="))?.Split('=')[1], out var r) ? r : 0f;
        Storyteller.PersonaValue = persona;
        Tuning.Apply("incident.days", random);
        int tb = 0, ti = 0, tf = 0, td = 0;
        foreach (var ship in ships.Split(','))
        {
            var w = World.CreateDefault(seed, 0, ship);
            long end = w.Tick + (long)days * SimTime.TicksPerDay;
            int fires = 0, prevFire = 0, maxFire = 0;
            while (w.Tick < end)
            {
                w.Step();
                int fc = w.Fire.Count;
                if (fc > prevFire) fires += fc - prevFire;
                prevFire = fc; maxFire = Math.Max(maxFire, fc);
            }
            int dead = w.Crew.Count(c => c.Dead);
            int inc = w.Hazards.RandomCount + w.Story.Fired;
            Console.WriteLine($"{ship}: 사고 {inc} · 폭발 {w.Blast.Stats.Detonations} · 불 붙음 {fires}(동시 최대 {maxFire}) · 사망 {dead}/{w.Crew.Count}");
            Console.WriteLine($"   {w.Blast.Stats}");
            tb += w.Blast.Stats.Detonations; ti += inc; tf += fires; td += dead;
        }
        Console.WriteLine($"합계 {days}일 · 사고 {ti} · 폭발 {tb} · 불 {tf} · 사망 {td}");
        return 0;
    }
}
