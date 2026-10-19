using ShipEngine;
using ShipView;
using ShipSim.Art;

namespace Lab;

/// <summary>마이크 · 고개 (소리 단계 S4 — docs/SOUND.md 결정 13 '나눔'): 주컴퓨터는 방마다 마이크로 큰 소리를 듣고 로봇을 보내 살핀다 · 사람은 소리 난 쪽으로 고개를 돌린다.</summary>
public static partial class Program
{
    private static void MicRules()
    {
        if (!Do("마이크")) return;
        Console.WriteLine("\n[마이크 — 주컴퓨터가 큰 소리를 듣고 살핀다 · 사람이 고개를 돌린다]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 빈 방의 쾅: 주컴퓨터가 마이크로 듣고 '큰 소리' 사건을 올려 로봇을 그 자리로 보낸다 → 로봇이 닿으면 끝 / 주컴퓨터가 멈춰 있으면 아무도 모른다 ──
        (long heard, bool raised, bool reached, bool closed, string why) Run(bool computerOk)
        {
            var s = Worlds.Full(lay, 21);
            var m = s.Get<Mind>(); var cm = s.Get<ShipMind>(); var net = s.Get<Network>();
            int r = m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
            if (!computerOk) { int dev = net.Devices.First(d => d.Type == "MainComputer").Id; net.Age(dev, Part.Electric, 1.0); }
            s.Run(12);   // 센서를 한 번 넘게 읽은 뒤 (읽기 전엔 어느 방도 보이지 않는다 — 마이크도 듣지 못한다)
            int src = At(17, 3);
            int cause = s.Causes.Add(CauseKind.Setup, "빈 방 선반에서 공구 상자가 떨어졌다 (시험 차리기)", s.Tick);
            s.Get<Sound>().Emit(s, src, 85, SoundKind.Bang, cause);
            bool raised = false, reached = false, closed = false; string why = "-";
            for (int k = 0; k < Time.Minutes(12); k++)
            {
                s.Step();
                foreach (var x in cm.Incidents)
                    if (x.Kind == IncidentKind.Heard)
                    {
                        if (!raised && x.Active != 0) { raised = true; why = x.Cause != 0 ? s.Causes.Story(x.Cause) : "-"; }
                        if (raised && x.Active == 0) closed = true;
                    }
                reached |= m.Dist(m[r].C.Cell, src) <= 2;
                if (Debug && k % 4 == 0 && k < 40) Console.WriteLine($"    {k} 전지 {m[r].C.Battery:0.00} 맡김 {m[r].C.AssignedGoal}:{m[r].C.AssignedTarget} ~{m[r].C.AssignedUntil - s.Tick} 피함 {m[r].Avoid.Count} 로봇 {m.Thought(r)} 칸 {m[r].C.Cell} 거리 {m.Dist(m[r].C.Cell, src)} · 사건 {string.Join(",", cm.Incidents.Where(x => x.Active != 0).Select(x => x.Kind))}");
                if (Debug && closed) Console.WriteLine($"    {k} 끝남 · 에이전트 {string.Join(" / ", m.Agents.Select(o => $"{o.C.Kind}#{o.Id} 칸 {o.C.Cell} 거리 {m.Dist(o.C.Cell, src)} 깸 {o.C.Up}"))} · 소리 칸 {src}");
                if (closed) break;
            }
            return (cm.MicHeard, raised, reached, closed, why);
        }
        var ok = Run(true); var off = Run(false);
        Check("마이크: 빈 방의 쾅(85dB)을 주컴퓨터가 듣고 '큰 소리' 사건을 올려 로봇이 그 자리 2칸 곁까지 가 둘러보고 사건이 끝난다 (원인: 떨어진 상자)",
            ok.heard > 0 && ok.raised && ok.reached && ok.closed && ok.why.Contains("공구 상자"),
            $"들음 {ok.heard} · 사건 {ok.raised} · 로봇 닿음 {ok.reached} · 끝남 {ok.closed} · 원인 \"{ok.why}\"");
        Check("마이크: 주컴퓨터가 멈춰 있으면 빈 방의 쾅을 아무도 살피지 않는다", off.heard == 0 && !off.raised,
            $"들음 {off.heard} · 사건 {off.raised}");

        // ── 아는 일의 소리: 이미 불이 난 방의 쾅은 새 사건이 아니다 (불의 소리다) ──
        {
            var s = Worlds.Full(lay, 22);
            var cm = s.Get<ShipMind>(); var ch = s.Get<Chemistry>();
            s.Run(5);
            ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Setup, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick));
            bool fire = false;
            for (int k = 0; k < Time.Minutes(3) && !fire; k++) { s.Step(); fire = cm.Incidents.Any(x => x.Active != 0 && x.Kind == IncidentKind.Fire); }
            long h0 = cm.MicHeard;
            s.Get<Sound>().Emit(s, At(17, 3), 85, SoundKind.Bang, 0);
            s.Run(3);
            Check("마이크: 이미 불이 난 방에서 난 쾅은 새 사건으로 올리지 않는다 (그 일의 소리다)", fire && cm.MicHeard == h0 && !cm.Incidents.Any(x => x.Active != 0 && x.Kind == IncidentKind.Heard),
                $"불 사건 {fire} · 새로 들음 {cm.MicHeard - h0}");
        }

        // ── 잠결의 비명: 자는 이 곁에서 난 비명(악몽 60dB)은 로봇을 보내지 않는다 · 깨어 있는 이 곁의 비명(85dB — 몸싸움)은 보낸다 ──
        {
            (long heard, long cries) Cry(bool asleep)
            {
                var s = Worlds.Full(lay, 24);
                var m = s.Get<Mind>(); var cm = s.Get<ShipMind>(); var life = s.Get<Life>();
                int p = m.AddPerson(s, At(17, 3));
                s.Run(12);
                if (asleep) { life[m[p].C.Body].Asleep = 1; m[p].C.Sleepy = 0.9f; }
                s.Get<Sound>().Emit(s, m[p].C.Cell, asleep ? 60 : 85, SoundKind.Scream, 0, p);
                s.Step();
                return (cm.MicHeard, cm.MicSleepCries);
            }
            var night = Cry(true); var fight = Cry(false);
            Check("마이크: 자는 이 곁의 비명(악몽)은 잠결의 소리로 보고 로봇을 보내지 않는다 · 깨어 있는 이의 비명은 살핀다 (두 방 마이크에 잡혀도 한 번)",
                night.heard == 0 && night.cries > 0 && fight.heard == 1, $"잠결: 사건 {night.heard} · 잠결로 봄 {night.cries} / 깨어: 사건 {fight.heard}");
        }

        // ── 고개: 깨어 선 이가 동쪽에서 난 소리(렌치 75dB — 이 방은 기계 소리 58dB가 가린다)를 들으면 두 틱 그 쪽을 보고 · 그 뒤엔 다시 앞을 본다 · 제 발소리 · 코골이에는 돌아보지 않는다 ──
        {
            var s = Worlds.Full(lay, 23);
            var m = s.Get<Mind>();
            int p = m.AddPerson(s, At(3, 3));
            s.Run(3);
            var a = m[p];
            int src = At(7, 3);
            long t0 = s.Tick;
            s.Get<Sound>().Emit(s, src, 75, SoundKind.Tool, 0);
            s.Step();
            if (Debug) { var sn = s.Get<Sound>(); Console.WriteLine($"    사람 칸 {a.C.Cell} 들린 것 {string.Join(",", sn.At(a.C.Cell).Select(h => $"{h.Kind}:{h.Db:0}"))} 바탕 {sn.Ambient(a.C.Cell):0} · 깨어 {a.C.Up} · 들은 데까지 {a.C.HeardUpTo}/{sn.LastEvent}"); }
            bool walking() => a.PathIdx < a.Path.Count;
            bool looked = a.C.LookAt == src && a.C.LookUntil >= s.Tick;
            var face1 = walking() ? DotFacing.E : Painter.FacingOf(lay, a, s.Tick);
            s.Run(4);
            bool after = walking() || Painter.FacingOf(lay, a, s.Tick) != DotFacing.E || a.C.LookAt != src;
            long g0 = m.Glances;
            s.Get<Sound>().Emit(s, src, 75, SoundKind.Snore, 0);
            s.Step();
            Check("고개: 깨어 선 이는 동쪽에서 난 렌치 소리에 그 쪽을 보고(그림도 동쪽) · 몇 틱 뒤엔 고개를 돌린다 · 코골이에는 돌아보지 않는다",
                looked && face1 == DotFacing.E && after && m.Glances == g0,
                $"본 칸 {a.C.LookAt}(소리 {src}) · 그림 {face1} · 뒤 {after} · 코골이에 돌아봄 {m.Glances - g0}");
        }
    }
}
