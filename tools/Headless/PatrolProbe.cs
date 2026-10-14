using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ShipSim.Core;

// 로봇 움직임 진단 (--patrolprobe [--ships=Hanbit,Mirinae] 일수): 1분마다 로봇을 보고
//  · 멈춤: 가려는 길(이동 단계)이 있는데 자리가 그대로인 시간 — 가장 길었던 때와 그 앞을 막은 것(문 · 사람 · 막힌 칸)
//  · 왕복: 한 시간 동안 많이 움직였는데 좁은 곳만 오간 때 · 같은 두 일을 번갈아 시작한 때
//  · 순찰: 순찰에 쓴 시간 · 같은 방만 도는지
public static partial class Program
{
    private sealed class BotTrack
    {
        public Vector2 Last;
        public int Frozen, MaxFrozen;
        public string FrozenAt = "";
        public readonly Queue<Vector2> Recent = new();
        public int Pace;           // 한 시간 창에서 오간 칸 수가 많고 퍼진 폭이 좁았던 분 수
        public string PaceAt = "";
        public float PaceTravel, PaceSpan;
        public readonly List<string> Doings = new();
        public int Flips;          // 같은 두 일을 번갈아 시작한 횟수
        public string FlipAt = "";
        public readonly List<string> Trail = new(); // 일이 바뀐 때 · 로봇이 든 까닭
        public int Minutes, Patrol;
        public readonly Dictionary<string, int> Kinds = new();
    }

    private static int RunPatrolProbe(int days, int seed, string[] args)
    {
        var ships = args.FirstOrDefault(a => a.StartsWith("--ships="))?[8..].Split(',') ?? new[] { "Hanbit", "Mirinae", "Bodeum", "Cheonma" };
        foreach (var ship in ships)
        {
            var w = World.CreateDefault(seed, 0, ship);
            long step = SimTime.Minutes(1), end = (long)days * SimTime.TicksPerDay;
            var tr = w.Robots.Robots.ToDictionary(r => r, r => new BotTrack { Last = r.Position });
            while (w.Tick < end)
            {
                Run(w, step);
                foreach (var r in w.Robots.Robots)
                {
                    if (!tr.TryGetValue(r, out var t)) tr[r] = t = new BotTrack { Last = r.Position };
                    t.Minutes++;
                    string kind = r.Fault != null ? "고장" : r.Disabled ? "꺼 둠" : r.AtDock ? "충전대" : r.Doing.Split(new[] { " — ", " (" }, StringSplitOptions.None)[0];
                    if (r.Doing.StartsWith("순찰")) { kind = "순찰"; t.Patrol++; }
                    t.Kinds[kind] = t.Kinds.GetValueOrDefault(kind) + 1;
                    float moved = (r.Position - t.Last).Length();
                    t.Last = r.Position;
                    // 멈춤
                    bool wantsToMove = r.State == RobotState.Active && r.Fault == null && !r.Disabled && r.Steps is { } st && r.StepIndex < st.Count && st[r.StepIndex].Moving && r.Path != null;
                    if (wantsToMove && moved < 0.05f)
                    {
                        t.Frozen++;
                        if (t.Frozen > t.MaxFrozen)
                        {
                            t.MaxFrozen = t.Frozen;
                            string ahead = "";
                            if (r.Path != null && r.PathIndex < r.Path.Count)
                            {
                                var next = r.Path[r.PathIndex];
                                var door = w.Ship.DoorAt(next);
                                var who = w.Crew.FirstOrDefault(c => !c.Dead && c.Cell == next);
                                ahead = $"앞 칸 {next}" + (door != null ? $" 문(열림 {door.Openness:0.00} · 잠김 {door.Locked})" : "") + (who != null ? $" 사람 {who.Name}({who.ActivityLabel})" : "")
                                        + (Locomotion.Blocked(w.Ship, next) ? " 막힘" : "");
                            }
                            t.FrozenAt = $"{SimTime.Clock(w.Tick)} {r.Room?.Name ?? "?"} 「{r.Doing}」 {ahead}";
                        }
                    }
                    else t.Frozen = 0;
                    // 왕복 (한 시간 창)
                    t.Recent.Enqueue(r.Position);
                    if (t.Recent.Count > 60) t.Recent.Dequeue();
                    if (t.Recent.Count == 60 && r.State == RobotState.Active)
                    {
                        var pts = t.Recent.ToArray();
                        float travel = 0f;
                        for (int i = 1; i < pts.Length; i++) travel += (pts[i] - pts[i - 1]).Length();
                        float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                        float span = MathF.Max(maxX - minX, maxY - minY);
                        if (travel > 60f && span < 8f)
                        {
                            t.Pace++;
                            if (travel > t.PaceTravel) { t.PaceTravel = travel; t.PaceSpan = span; t.PaceAt = $"{SimTime.Clock(w.Tick)} {r.Room?.Name ?? "?"} 「{r.Doing}」"; }
                        }
                    }
                    // 같은 두 일을 번갈아
                    if (r.State == RobotState.Active && (t.Doings.Count == 0 || t.Doings[^1] != r.Doing))
                    {
                        t.Trail.Add($"{SimTime.Clock(w.Tick)} 「{r.Doing}」 배터리 {r.Battery * 100:0}% · {r.Mind.Why}");
                        if (t.Trail.Count > 8) t.Trail.RemoveAt(0);
                        t.Doings.Add(r.Doing);
                        if (t.Doings.Count > 6) t.Doings.RemoveAt(0);
                        var d = t.Doings;
                        if (d.Count >= 5 && d[^1] == d[^3] && d[^3] == d[^5] && d[^2] == d[^4] && d[^1] != d[^2])
                        {
                            t.Flips++;
                            t.FlipAt = $"{SimTime.Clock(w.Tick)} 「{d[^2]}」 ↔ 「{d[^1]}」\n          " + string.Join("\n          ", t.Trail);
                        }
                    }
                }
            }
            Console.WriteLine($"■ {ship} · {days}일 · 로봇 {w.Robots.Robots.Count}");
            int frozenBots = 0, pacingBots = 0, flipBots = 0;
            foreach (var (r, t) in tr.OrderByDescending(kv => kv.Value.MaxFrozen + kv.Value.Pace + kv.Value.Flips * 10))
            {
                bool bad = t.MaxFrozen >= 20 || t.Pace >= 30 || t.Flips >= 3;
                if (t.MaxFrozen >= 20) frozenBots++;
                if (t.Pace >= 30) pacingBots++;
                if (t.Flips >= 3) flipBots++;
                if (!bad) continue;
                var top = string.Join(" · ", t.Kinds.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} {kv.Value * 100 / Math.Max(1, t.Minutes)}%"));
                Console.WriteLine($"  {r.Name}[{r.Kind}] {top}");
                if (t.MaxFrozen >= 20) Console.WriteLine($"      멈춤 가장 길게 {t.MaxFrozen}분 — {t.FrozenAt}");
                if (t.Pace >= 30) Console.WriteLine($"      좁은 곳 왕복 {t.Pace}분 (한 시간에 {t.PaceTravel:0}칸 · 폭 {t.PaceSpan:0.0}칸) — {t.PaceAt}");
                if (t.Flips >= 3) Console.WriteLine($"      두 일 번갈아 {t.Flips}번 — {t.FlipAt}");
            }
            int faultMin = tr.Values.Sum(t => t.Kinds.GetValueOrDefault("고장"));
            Console.WriteLine($"  고장 {w.Robots.Robots.Sum(r => r.Breakdowns)}번 · 스스로 고침 {w.Robots.Robots.Sum(r => r.SelfRepairsTotal)}번 · 지금 고장 {w.Robots.Robots.Count(r => r.Fault != null)}대 · 고장으로 쉰 시간 {faultMin / 60f:0}시간(로봇 합)");
            Console.WriteLine($"  → 멈춤(20분↑) {frozenBots}대 · 좁은 곳 왕복(30분↑) {pacingBots}대 · 두 일 번갈아(3번↑) {flipBots}대 · 순찰 비중 {tr.Values.Sum(t => t.Patrol) * 100 / Math.Max(1, tr.Values.Sum(t => t.Minutes))}%");
        }
        return 0;
    }
}
