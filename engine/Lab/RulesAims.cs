using ShipEngine;

namespace Lab;

/// <summary>⑧ 바퀴 3 — 인생 목표: 배를 지킨다 · 솜씨 · 귀향 · 짝 (docs/ENGINE.md ⑧ 바퀴 3).</summary>
public static partial class Program
{
    private static void AimRules()
    {
        if (!Do("인생 목표")) return;
        Console.WriteLine("\n[⑧ 인생 목표]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        float FireConf = float.Parse(Environment.GetEnvironmentVariable("AIM_CONF") ?? "0.7"), AimHunger = float.Parse(Environment.GetEnvironmentVariable("AIM_HUNGER") ?? "0.6");
        // ── 배를 지킨다: 같은 사람 · 같은 소문(옆 방에 불이 났다더라) · 둘 다 배고프다 — '배를 지킨다'가 목표인 이는 끼니를 미루고 불로 가고 · 아닌 이는 먹으러 간다 ──
        {
            int went(Aim aim)
            {
                int n = 0;
                for (ulong seed = 1; seed <= 8; seed++)
                {
                    var s = Worlds.Full(lay, seed);
                    var m = s.Get<Mind>();
                    int p = m.AddPerson(s, At(15, 3));
                    m.SetPersonality(p, 0.5f, 0.5f, 0.5f, 0.5f, 0.55f);
                    m[p].C.SkFire = 0.3f; m[p].C.Aim = (byte)aim; m[p].C.Hunger = AimHunger; m[p].C.Thirst = AimHunger;
                    s.Run(2);
                    m.Learn(m[p], s.Tick, BeliefKind.Fire, At(4, 1), -1, FireConf, BeliefSource.Told);
                    bool w = false;
                    for (int k = 0; k < Time.Minutes(2) && !w; k++) { s.Step(); w = m[p].C.Goal == GoalKind.FightFire; }
                    if (w) n++;
                }
                return n;
            }
            int guard = went(Aim.Guard), none = went(Aim.None);
            if (Debug) Console.WriteLine($"    확신 {FireConf} 배고픔 {AimHunger}: 배를 지킨다 {guard} · 없음 {none}");
            Check("배를 지킨다: 같은 사람 · 같은 소문(옆 방 불) · 둘 다 배고프다 — 목표가 '배를 지킨다'면 끼니를 미루고 불로 간다 · 아닌 이는 먹으러 간다 (불 끄기 · 구하기 · 막기 1.4배)",
                guard >= none + 3, $"시드 여덟 중 불로 감: 배를 지킨다 {guard} · 목표 없음 {none}");
        }

        // ── 솜씨: 맡은 일의 으뜸 솜씨가 1.5배 빨리 는다 · 0.8을 넘으면 '오래 바라던 것을 이뤘다' ──
        {
            (float gain, string story) Fix(Aim aim, float start)
            {
                var s = Worlds.Full(lay, 3);
                var m = s.Get<Mind>(); var net = s.Get<Network>();
                int role = Array.IndexOf(m.RoleNames, "정비사");
                var dev = net.Devices.First(d => d.Has(Part.Electric) && d.Def.Role is not ("battery" or "reactor" or "panel"));
                int p = m.AddPerson(s, dev.Cell, role);
                m[p].C.Aim = (byte)aim; m[p].C.SkRepair = start; m[p].C.SkSeal = 0.5f;
                m[p].C.Holding = (byte)m.ItemIndex("수리 꾸러미");
                net.Broken[dev.Id * 4 + (int)Part.Electric] = 1;
                for (int k = 0; k < Time.Hours(1) && net.Broken[dev.Id * 4] != 0; k++) s.Step();
                s.Run(30);
                string story = s.Facts.Since(0).Where(x => x.Kind == FactKind.Aim && x.Actor == p).Select(x => s.Causes.Text(x.Cause)).FirstOrDefault() ?? "";
                return (m[p].C.SkRepair - start, story);
            }
            var master = Fix(Aim.Master, 0.5f); var plain = Fix(Aim.None, 0.5f); var reach = Fix(Aim.Master, 0.985f);
            Check("솜씨: '솜씨'가 목표인 정비사는 고칠 때 고치기 솜씨가 1.5배 는다 · 으뜸 솜씨가 끝(0.99)에 닿는 순간 '오래 바라던 것을 이뤘다'",
                Math.Abs(master.gain - 1.5f * plain.gain) < 1e-4 && plain.gain > 0 && reach.story.Contains("이뤘다"),
                $"솜씨 늘음: 솜씨 목표 {master.gain:0.0000} · 목표 없음 {plain.gain:0.0000} · 0.985에서 \"{reach.story}\"");
        }

        // ── 귀향: 정해진 점화를 놓치면 '귀향'이 목표인 이만 무너진다 (슬픔 · 긴장) · 도착 점화에 이룬다 ──
        {
            var s = Worlds.Full(lay, 4);
            var m = s.Get<Mind>();
            int home = m.AddPerson(s, At(15, 3)), other = m.AddPerson(s, At(16, 3));
            m[home].C.Aim = (byte)Aim.Home; m[other].C.Aim = (byte)Aim.None;
            s.Run(30);
            float h0 = m[home].C.Sad, o0 = m[other].C.Sad;
            s.Facts.Emit(s.Tick, FactKind.Burn, -1, 0, s.Causes.Add(CauseKind.Route, "도착 점화를 놓쳤다 — 엔진이 멈춰 있었다", s.Tick), other: -(int)BurnKind.Arrive);
            s.Run(30);
            float h1 = m[home].C.Sad, o1 = m[other].C.Sad;
            s.Facts.Emit(s.Tick, FactKind.Burn, -1, 0.05f, s.Causes.Add(CauseKind.Route, "도착 점화", s.Tick), other: (int)BurnKind.Arrive);
            s.Run(30);
            var aims = s.Facts.Since(0).Where(x => x.Kind == FactKind.Aim && x.Actor == home).Select(x => (x.Size, s.Causes.Story(x.Cause))).ToList();
            Check("귀향: 점화를 놓치면 '귀향'이 목표인 이만 무너진다 (원인: 점화를 놓쳤다 → 무너졌다) · 도착 점화에 이룬다",
                h1 - h0 > 0.25f && Math.Abs(o1 - o0) < 0.05f && aims.Any(x => x.Size < 0 && x.Item2.Contains("놓쳤다")) && aims.Any(x => x.Size > 0),
                $"슬픔 귀향 {h0:0.00}→{h1:0.00} · 목표 없음 {o0:0.00}→{o1:0.00} · {string.Join(" / ", aims.Select(x => $"{x.Size:+0;-0} {x.Item2}"))}");
        }

        // ── 짝: 짝을 맺으면 이루고 · 헤어지면 잃는다 (슬픔) ──
        {
            var s = Worlds.Full(lay, 5);
            var m = s.Get<Mind>(); var so = s.Get<Society>();
            int a = m.AddPerson(s, At(15, 3)), b = m.AddPerson(s, At(16, 3));
            m[a].C.Aim = (byte)Aim.Love;
            s.Run(30);
            so.Partner[a] = b; so.Partner[b] = a;
            s.Run(30);
            bool met = m[a].C.AimMet == 1;
            float sad0 = m[a].C.Sad;
            so.Partner[a] = -1; so.Partner[b] = -1;
            s.Run(30);
            var aims = s.Facts.Since(0).Where(x => x.Kind == FactKind.Aim && x.Actor == a).Select(x => $"{x.Size:+0;-0} {s.Causes.Text(x.Cause)}").ToList();
            Check("짝: '짝'이 목표인 이는 짝을 맺으면 이루고 · 헤어지면 잃는다 (슬픔 +0.35)",
                met && m[a].C.AimMet == 0 && m[a].C.Sad - sad0 > 0.3f && aims.Count == 2,
                $"{string.Join(" / ", aims)} · 슬픔 {sad0:0.00}→{m[a].C.Sad:0.00}");
        }

        // ── 고르기: 성격에서 목표를 고른다 — 여러 시드의 사람 60명이 다섯 갈래로 퍼진다 · 성실한 이는 '솜씨'가 많다 ──
        {
            var count = new int[6]; int consMaster = 0, cons = 0;
            for (ulong seed = 1; seed <= 10; seed++)
            {
                var s = Worlds.Full(lay, seed);
                var m = s.Get<Mind>();
                for (int i = 0; i < 6; i++) { int p = m.AddPerson(s, At(12 + i, 3)); count[m[p].C.Aim]++; if (m[p].C.Consc > 0.65f) { cons++; if (m[p].C.Aim == (byte)Aim.Master) consMaster++; } }
            }
            Check("고르기: 성격에서 인생 목표를 고른다 — 60명이 다섯 갈래로 모두 퍼지고 · 성실한 이(0.65 넘게)는 '솜씨'가 평균보다 많다",
                count.Skip(1).All(n => n > 0) && cons > 0 && consMaster / (double)cons > count[(int)Aim.Master] / 60.0,
                $"{string.Join(" · ", Enumerable.Range(1, 5).Select(i => $"{Mind.AimName((Aim)i)} {count[i]}"))} · 성실한 {cons}명 중 솜씨 {consMaster}");
        }
    }
}
