using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.18 규모별 대응 계획 (주 컴퓨터 · 사람) · 소집 · 점호 · 이야기꾼 완급.
//  주 컴퓨터: 규모를 판정 → 방송 문구 · 소집 인원 · 제안을 다섯 칸 기록(ComputerLog)에 남기고, 방침이 물으라면 제안 카드(Asks)를 낸다.
//    판정은 30분 뒤 채점된다 (그 규모 안에서 막았나, 더 번졌나) — 틀리면 컴퓨터가 배운다.
//  컴퓨터가 멎었으면: 지휘하는 사람이 5분 늦게 판정하고 외친다 (방송이 없으니 깨어 있는 사람만 듣는다).
//  배 전체: 일 맡은 몇 명 말고는 모인다 (점호) — 방송을 들었고 컴퓨터를 믿는 사람 · 외침을 들은 사람만.

public sealed partial class ScaleSystem
{
    // ─────────────────────────────── 판정 · 계획 ───────────────────────────────

    private int Able() => _w.Crew.Count(c => c.CanAct && !c.IsChild && !c.Outside);

    private void Plan(ScaleCase k, string why)
    {
        var w = _w;
        var s = k.Now;
        var a = w.Automation;
        bool computer = a.Present && a.MainOnline;
        var prep = Prepare(k, s, computer, out var lesson); // 지난번 크게 번진 종류면 한 칸 높여 부른다 (ScaleLearn.cs)
        int able = Able();
        int n = ScaleTable.Muster(prep, able);
        // 일 맡을 사람: 배 전체 · 우주급이면 몇 명만 일에 붙고 나머지는 모인다
        int workers = s >= IncidentScale.Ship ? Math.Clamp(able / 3, 3, 8) : n;
        PickWorkers(k, Math.Min(workers, able));
        k.Called = n;
        k.StageCalled[(int)s] = Math.Max(k.StageCalled[(int)s], n);
        k.Planned = prep;
        if (k.Steps.Count == 1) k.Guess = prep;
        Plans++;
        var room = k.RoomId >= 0 && k.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[k.RoomId] : null;
        string names = string.Join("·", k.Workers.Take(4).Select(id => w.Crew[id].Name));
        string where = PlaceText(k);
        k.Suggest = Suggestion(k);
        if (s >= IncidentScale.Ship && k.CosmicId < 0) k.MusterRoom = MusterRoomFor(k)?.Id ?? -1;
        k.Broadcast = s switch
        {
            IncidentScale.Personal => $"{k.Name} — 개인 사고. " + (names.Length > 0 ? $"곁의 {Ko.IGa(names)} 돕는다" : "혼자 추스른다"),
            IncidentScale.Room => (k.Name.Contains(where) ? k.Name : $"{where} {k.Name}") + " — 방 규모. " + (names.Length > 0 ? $"당직 {Ko.IGa(names)} 맡는다" : "당직이 맡는다"),
            IncidentScale.System => $"{k.Name} — " + (k.Steps.Count > 1 ? "계통으로 번졌다" : "계통 사고") + $" ({where}). {n}명 소집 · 그쪽 작업 우선",
            IncidentScale.Ship => $"{k.Name} — 배 전체 사고. 전원 소집 · 일상 중단" + (k.MusterRoom >= 0 ? $" · {Ko.EuRo(w.Ship.Rooms[k.MusterRoom].Name)} 모여 점호" : ""),
            _ => $"{k.Name} — 우주급 재난. 대피 · " + (CosmicAvoid(k) ? "항로 변경 검토" : "피할 수 없다 — 대피소에서 버틴다"),
        };
        if (lesson.Length > 0) k.Broadcast += $" · {lesson} ({n}명)";
        if (!computer)
        {
            // 컴퓨터가 없다: 사람이 판정한다 (늦다)
            if (s >= IncidentScale.Room && k.JudgeDue < 0) k.JudgeDue = w.Tick + SimTime.Minutes(5);
            k.Plan = $"{ScaleTable.Label(s)} — 컴퓨터가 없어 사람이 판정한다 (5분 늦게)";
            return;
        }
        k.JudgedBy = "주 컴퓨터";
        k.Plan = $"{ScaleTable.Label(s)} · {ScaleTable.Response(prep)} · 소집 {n}명" + (lesson.Length > 0 ? $" · 교훈: {lesson}" : "") + (k.Suggest.Length > 0 ? $" · 제안: {k.Suggest}" : "");
        // 방송: 개인은 손목 단말로만 · 방은 안내 · 계통부터 경보
        int priority = prep switch { IncidentScale.Personal => 0, IncidentScale.Room => 1, _ => 2 };
        string order = s == IncidentScale.Ship && k.MusterRoom >= 0 ? "muster" : "";
        if (priority > 0 && (prep >= IncidentScale.System || Notable(k)))
        {
            var b = a.Speak.Announce(a.Voice.Style(k.Broadcast), room, priority, order);
            if (b != null) { k.BroadcastId = b.Id; Broadcasts++; }
        }
        bool ask = s >= IncidentScale.Ship && a.Asks.Needed("muster"); // 방침이 모두 물으라면 소집도 제안 카드로
        if (s == IncidentScale.Ship && k.MusterRoom >= 0 && !ask) { k.MusterAt = w.Tick; k.MusterDone = false; }
        if (s >= IncidentScale.Room && (prep >= IncidentScale.System || Notable(k)))
        {
            var planned = prep;
            var judged = s;
            var kk = k;
            a.Book.Add(s >= IncidentScale.System ? ActKind.Broadcast : ActKind.Advice, room,
                $"{k.Name} · 번진 방 {k.Rooms.Count}" + (why.Length > 0 ? $" · {why}" : ""),
                $"규모 {ScaleTable.Label(s)} — {ScaleTable.Examples(s)}급" + (lesson.Length > 0 ? $" · {lesson}" : ""),
                $"{(k.BroadcastId >= 0 ? "방송" : "단말 알림")} · {n}명 소집" + (prep >= IncidentScale.Ship ? " · 일상 중단" : prep == IncidentScale.System ? " · 작업 우선" : ""),
                k.Suggest.Length > 0 ? k.Suggest : ScaleTable.Response(s),
                $"scale:{k.Id}:{(int)s}", 0, 30f,
                (world, act) => kk.Peak > planned ? (-1, $"규모를 낮게 봤다 — {ScaleTable.Label(kk.Peak)}까지 번졌다")
                    : !kk.Open || kk.Now < judged ? (1, $"판정한 규모({ScaleTable.Name(planned)}) 안에서 막았다")
                    : (2, $"아직 {ScaleTable.Label(kk.Now)}"));
        }
        // 제안: 방침이 위험한 조치는 물으라고 하면 카드로 (전원 소집 · 일상 중단 · 항로 변경)
        if (ask && a.Asks.Pending($"scale:{k.Id}") == null)
        {
            Proposals++;
            var kk = k;
            a.Asks.Propose($"scale:{k.Id}", "muster", room, s == IncidentScale.Cosmic ? "우주급 — 일상 중단 · 대피 · 항로 변경" : "배 전체 — 전원 소집 · 일상 중단",
                $"{k.Name} ({ScaleTable.Label(s)})", k.Suggest.Length > 0 ? k.Suggest : "모두 모여 점호 · 일 맡은 사람만 현장", 5f, null,
                (world, p) => { if (kk.MusterRoom >= 0) { kk.MusterAt = world.Tick; kk.MusterDone = false; } },
                (world, p) => !kk.Open || kk.Mustered.Count > 0 ? (1, "모였다") : null);
        }
        // 일에 붙을 사람에게 한마디 (규모를 느낀다)
        if (k.Workers.Count > 0 && s >= IncidentScale.Room)
        {
            var lead = w.Crew[k.Workers[0]];
            if (lead.CanAct && lead.IsAwake)
                lead.Say(w, s switch
                {
                    IncidentScale.Room => $"{(room?.Name ?? "거기")} 내가 볼게.",
                    IncidentScale.System => "번졌어 — 여럿이 붙어야 해!",
                    IncidentScale.Ship => "배 전체야! 다들 모여!",
                    _ => "하늘이… 다들 대피해!",
                });
        }
    }

    /// <summary>방에 남을 만한 사건인가 (방 규모 방송은 이것만 — 금방 끝나는 작은 고장은 단말로).</summary>
    private bool Notable(ScaleCase k)
    {
        if (k.Root < 0) return k.CrewId < 0;
        var inc = _w.Causes.IncidentOf(k.Root);
        return inc != null && (inc.OpenCount > 0 || inc.Nodes.Count >= 2 || inc.ByObserver);
    }

    private string PlaceText(ScaleCase k)
    {
        var w = _w;
        var names = k.Rooms.Where(r => r >= 0 && r < w.Ship.Rooms.Count).Select(r => w.Ship.Rooms[r].Name).Distinct().ToList();
        if (names.Count == 0) return k.RoomId >= 0 && k.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[k.RoomId].Name : "배";
        return names.Count <= 2 ? string.Join("·", names) : $"{names[0]} 외 {names.Count - 1}곳";
    }

    private bool CosmicAvoid(ScaleCase k) => _w.Cosmic.Events.FirstOrDefault(e => e.Id == k.CosmicId) is CosmicEvent e && e.Spec.Avoid;

    /// <summary>규모마다 컴퓨터가 내놓는 제안 한 줄.</summary>
    private string Suggestion(ScaleCase k)
    {
        var w = _w;
        switch (k.Now)
        {
            case IncidentScale.Personal: return "의무 담당은 대기";
            case IncidentScale.Room: return k.Skill switch { Skill.Electrical => "전기 담당이 분전함부터", Skill.Mechanics => "배관 담당이 밸브부터", Skill.Medicine => "의무 담당이 곁으로", _ => "당직이 현장부터" };
            case IncidentScale.System:
                return w.Net.Rings.Count(r => r.kind == NetKind.Power) == 0 && k.KindsSeen.Contains(CauseKind.Outage) ? "보조 간선을 깔자 (한 곳이 끊기면 줄줄이 꺼진다)"
                    : "비번 중 솜씨 맞는 사람을 부른다 · 급하지 않은 정비는 미룬다";
            case IncidentScale.Ship:
                return !w.Power.ReactorOnline ? "원자로 재기동 조 · 배터리 아끼기 (절전)" : "현장 조 몇 명만 · 나머지는 점호 뒤 대기";
            default:
            {
                var e = w.Cosmic.Events.FirstOrDefault(x => x.Id == k.CosmicId);
                if (e == null) return "대피소로";
                if (e.AvoidPlan == 1) return $"항로 변경 중 — 추진제 {e.Spec.AvoidFuel:0}";
                return e.Spec.Avoid ? $"항로 변경으로 피하자 (추진제 {e.Spec.AvoidFuel:0} 필요)" : "피할 수 없다 — 대피소 · 물벽 · 설비 끄기";
            }
        }
    }

    /// <summary>컴퓨터가 없을 때: 지휘하는 사람이 5분 늦게 판정하고 외친다.</summary>
    private void HumanJudge(ScaleCase k)
    {
        var w = _w;
        k.JudgeDue = -1;
        if (!k.Open) return;
        var boss = w.Command.Commander ?? w.Command.Captain ?? w.Crew.Where(c => c.CanAct && !c.IsChild && c.IsAwake).OrderBy(c => c.Id).FirstOrDefault();
        if (boss == null || !boss.CanAct) return;
        HumanJudged++;
        k.JudgedBy = boss.Name;
        k.Planned = k.Now;
        k.ShoutAt = w.Tick;
        if (k.Now >= IncidentScale.Ship && k.CosmicId < 0) { k.MusterRoom = MusterRoomFor(k)?.Id ?? -1; if (k.MusterRoom >= 0) { k.MusterAt = w.Tick; k.MusterDone = false; } }
        k.Plan = $"{ScaleTable.Label(k.Now)} — {boss.Name} 판정 (컴퓨터 없이)";
        boss.Say(w, k.Now >= IncidentScale.Ship ? "배 전체야! 다 모여!" : k.Now == IncidentScale.System ? "번졌다 — 손 비는 사람 다 와!" : "내가 맡는다.");
        w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(boss.Name)} 사고 규모를 판정했다 — {k.Name} {ScaleTable.Label(k.Now)} (주 컴퓨터 없이 · 5분 늦게)", boss.Id);
    }

    /// <summary>부를 사람: 당직(근무 중) · 깨어 있음 · 솜씨 · 가까움 순. 개인 사고는 같은 방의 곁의 사람 하나.</summary>
    private void PickWorkers(ScaleCase k, int n)
    {
        var w = _w;
        var room = k.RoomId >= 0 && k.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[k.RoomId] : null;
        var at = room?.Center ?? (k.CrewId >= 0 ? w.Crew[k.CrewId].Position : System.Numerics.Vector2.Zero);
        var keep = new List<int>(k.Workers.Where(id => id < w.Crew.Count && w.Crew[id].CanAct));
        var pool = new List<(float score, int id)>();
        foreach (var c in w.Crew)
        {
            if (!c.CanAct || c.IsChild || c.Outside || c.CarriedBy != null || c.Id == k.CrewId || keep.Contains(c.Id)) continue;
            if (k.Now == IncidentScale.Personal && (c.Room == null || room == null || c.Room != room)) continue; // 곁의 사람만
            float d = MathF.Abs(c.Position.X - at.X) + MathF.Abs(c.Position.Y - at.Y);
            float score = (ChoresActivity.OnShiftStatic(c, w) ? 2f : 0f) + (c.IsAwake ? 1f : -0.6f) + 2f * c.SkillLevel(k.Skill) - d / 30f - c.Vitals.Injury;
            pool.Add((score, c.Id));
        }
        pool.Sort((x, y) => y.score != x.score ? y.score.CompareTo(x.score) : x.id.CompareTo(y.id));
        foreach (var (_, id) in pool) { if (keep.Count >= n) break; keep.Add(id); }
        k.Workers.Clear();
        k.Workers.AddRange(keep.Take(Math.Max(n, 0)));
    }

    /// <summary>실제로 그 사건에 붙은 사람: 그 방 · 그 사람의 일 · 점호 · 대재난 대비 · 배 전체의 생존 사슬 일.</summary>
    private int CountResponders(ScaleCase k)
    {
        var w = _w;
        int n = 0;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Job is not Job j) continue;
            if (j.Activity is MusterActivity && j.TargetRoom != null && j.TargetRoom.Id == k.MusterRoom) { n++; continue; }
            if (k.CosmicId >= 0 && j.Activity is CosmicEvacuateActivity or CosmicShelterActivity or CosmicBraceActivity or CosmicWarnActivity) { n++; continue; }
            if (j.Order is not WorkOrder o) continue;
            if (k.CrewId >= 0 && o.Target.Crew?.Id == k.CrewId) { n++; continue; }
            if (o.Target.CurrentRoom is Room r && k.Rooms.Contains(r.Id)) { n++; continue; }
            if (o.Kind is WorkKind.RepairNet && w.Net.Links.FirstOrDefault(l => l.Id == o.Circuit) is NetLink l && l.Node >= 0 && _byRoot.TryGetValue(w.Causes.Node(l.Node).Incident, out var lk) && lk == k) { n++; continue; }
            if (k.Now >= IncidentScale.Ship && Crisis.Relevance(w, o) >= 0.9f) n++;
        }
        return n;
    }

    // ─────────────────────────────── 소집 · 점호 ───────────────────────────────

    /// <summary>모일 곳: 함교 → 식당 → 휴게실 → 가장 큰 방 (사건 난 방 · 새는 방 · 못 가는 방은 빼고).</summary>
    private Room? MusterRoomFor(ScaleCase k)
    {
        var w = _w;
        bool Ok(Room r) => !r.Detached && !r.OffLimits && !r.Leaking && !k.Rooms.Contains(r.Id) && r.Type != RoomType.Corridor && w.Fire.CountIn(r) == 0;
        // 방사선 사고는 대피소에 모인다 (선반 · 물벽 뒤)
        if (k.Key is nameof(HazardKind.SolarStorm) or nameof(HazardKind.RadiationBurst) && Facilities.Best(w.Ship, "shelter", Ok).room is Room shelter) return shelter;
        foreach (var t in new[] { RoomType.Bridge, RoomType.Mess, RoomType.Lounge })
            if (w.Ship.RoomsOf(t).FirstOrDefault(Ok) is Room r) return r;
        return w.Ship.LiveRooms.Where(Ok).OrderByDescending(r => r.Cells.Count).ThenBy(r => r.Id).FirstOrDefault();
    }

    /// <summary>이 사람이 지금 따를 소집 (들었고 믿거나, 외침을 들었다 · 일 맡은 사람은 빼고).</summary>
    public ScaleCase? MusterCall(CrewMember c)
    {
        if (_open.Count == 0) return null;
        var w = _w;
        if (c.Dead || c.Down || c.Outside || c.Away || c.CarriedBy != null) return null;
        foreach (var k in _open)
        {
            if (!k.Hot || k.MusterRoom < 0 || k.MusterAt < 0 || k.MusterDone || w.Tick - k.MusterAt > SimTime.Minutes(45)) continue;
            if (k.Mustered.Contains(c.Id) || k.Workers.Contains(c.Id)) continue;
            if (k.Told.Contains(c.Id)) return k;
        }
        return null;
    }

    /// <summary>점호에 섰다.</summary>
    internal void Present(CrewMember c, ScaleCase k)
    {
        var w = _w;
        if (k.Mustered.Contains(c.Id)) return;
        k.Mustered.Add(c.Id);
        if (k.Mustered.Count == 1) Musters++;
        if (c.IsChild) w.Brain2.Emotions.Feel(c, Feeling.Fear, -0.05f, "어른들 곁에 모였다");
    }

    /// <summary>
    /// 소집이 퍼진다: 방송을 들었고 컴퓨터를 믿는 사람 · 외침을 들은 깨어 있는 사람, 그리고 1분마다 그 사람과 같은 방에 있는 깨어 있는 사람
    /// (정전으로 스피커가 죽은 방에는 말로 전해진다 — 컴퓨터를 못 믿는 사람도 동료 말은 듣는다).
    /// 점호가 끝났나: 부를 사람이 다 모였거나 30분이 지났다 — 빠진 사람을 적는다.
    /// </summary>
    private void MusterCheck()
    {
        var w = _w;
        foreach (var k in _open)
        {
            if (k.MusterAt < 0 || k.MusterDone) continue;
            var a = w.Automation;
            var fresh = new List<int>();
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Down || c.Away || c.Outside || k.Told.Contains(c.Id)) continue;
                bool heard = k.BroadcastId >= 0 && a.Speak.Heard(c, k.BroadcastId) && a.Trusts.Obeys(c);
                bool shout = k.ShoutAt >= 0 && c.IsAwake;
                CrewMember? teller = null;
                if (!heard && !shout && c.IsAwake && c.Room != null)
                    foreach (var o in w.Crew) if (o != c && !o.Dead && o.Room == c.Room && k.Told.Contains(o.Id) && o.IsAwake) { teller = o; break; }
                if (!heard && !shout && teller == null) continue;
                fresh.Add(c.Id);
                if (teller != null)
                {
                    WordOfMouth++;
                    if (WordOfMouth % 3 == 1) teller.Say(w, $"{c.Name}, 방송 못 들었어? 다들 {Ko.EuRo(k.MusterRoom >= 0 ? w.Ship.Rooms[k.MusterRoom].Name : "함교")} 모이래!");
                }
            }
            k.Told.AddRange(fresh);
            if (k.Mustered.Count == 0) continue;
            var expected = w.Crew.Where(c => !c.Dead && !c.Away && !c.Outside && !k.Workers.Contains(c.Id)).Select(c => c.Id).ToList();
            bool all = expected.All(k.Mustered.Contains);
            if (!all && w.Tick - k.MusterAt < SimTime.Minutes(30)) continue;
            k.MusterDone = true;
            var missing = expected.Where(id => !k.Mustered.Contains(id)).Select(id => w.Crew[id].Name).ToList();
            string room = k.MusterRoom >= 0 ? w.Ship.Rooms[k.MusterRoom].Name : "?";
            w.Log.Add(w.Tick, LogKind.Ship, $"점호 — {room}에 {k.Mustered.Count}명 · 현장 {k.Workers.Count}명" + (missing.Count > 0 ? $" · 안 온 사람 {string.Join("·", missing.Take(4))}" + (missing.Count > 4 ? $" 외 {missing.Count - 4}" : "") : " · 모두 왔다"));
            // 컴퓨터 말대로 모였더니 무사했다: 믿음이 조금 오른다
            if (k.BroadcastId >= 0)
                foreach (int id in k.Mustered) w.Automation.Trusts.Change(w.Crew[id], 0.01f, $"{k.Name} — 방송대로 모였다", quiet: true);
            // 셋에 하나 넘게 안 왔다 (잠 · 해당 조만 소집): 다음 정기 회의에 비상 소집 방침을 다시 올린다
            if (missing.Count * 3 >= Math.Max(3, expected.Count) && w.Policies["muster"] == 0)
                w.Meetings.QueueReview("muster", 1, $"{k.Name} 점호에 {missing.Count}명이 안 왔다 — 배 전체 사고엔 모두 깨우자");
        }
    }

    // ─────────────────────────────── 이야기꾼 완급 ───────────────────────────────

    /// <summary>큰 것 뒤 숨 돌릴 틈 (시간): 느긋 36 · 쉬움 24 · 보통 16 · 어려움 10 · 가혹 6, 자비형은 더 · 앙갚음형은 덜.</summary>
    public static float RestHours(StoryPersona p, int level)
    {
        float h = new[] { 36f, 24f, 16f, 10f, 6f }[Math.Clamp(level, 1, 5) - 1];
        return p switch { StoryPersona.Merciful => h * 1.5f, StoryPersona.Vengeful => h * 0.4f, StoryPersona.Burst => h * 0.6f, _ => h };
    }

    private bool BigHot() => _open.Any(k => k.Hot && k.Now >= IncidentScale.Ship);
    private bool RecentBig(long within) => BigHot() || LastBigEnd >= 0 && _w.Tick - LastBigEnd < within;
    private bool RecentSystem(long within) => Cases.Count > 0 && Cases.Skip(Math.Max(0, Cases.Count - 30)).Any(k => k.Peak == IncidentScale.System && _w.Tick - k.Changed < within);

    /// <summary>너무 조용하다: 하루 넘게 방 이상 사건이 없었다 (항해 첫날은 빼고).</summary>
    public bool Quiet()
    {
        var w = _w;
        if (w.Tick - w.StartTickOf < SimTime.TicksPerDay) return false;
        long window = (long)(SimTime.TicksPerDay * MathF.Max(1f, 1.5f * Storyteller.GapDays));
        for (int i = Cases.Count - 1; i >= 0; i--)
        {
            var k = Cases[i];
            if (w.Tick - k.Start > window) break;
            if (k.Peak >= IncidentScale.Room) return false;
        }
        return true;
    }

    /// <summary>이야기꾼 무게 배율: 큰 것 뒤엔 작은 것 위주, 계통 뒤엔 배 전체를 미루고, 너무 조용하면 작은 것부터.</summary>
    public float PaceMul(string key)
    {
        var s = ScaleTable.OfKey(key);
        long rest = SimTime.Hours(RestHours(Storyteller.Persona, Storyteller.Level) * 2f);
        float m = 1f;
        if (RecentBig(rest)) m *= s >= IncidentScale.Ship ? 0.05f : s == IncidentScale.System ? 0.25f : 1.4f;
        else if (RecentSystem(rest / 2)) m *= s >= IncidentScale.Ship ? 0.5f : 1f;
        if (Quiet()) m *= s <= IncidentScale.Room ? 1.6f : s >= IncidentScale.Ship ? 0.5f : 0.9f;
        return m;
    }

    /// <summary>이야기꾼이 쉬어 갈까: 배 전체 · 우주급이 아직 뜨겁거나, 가라앉은 지 얼마 안 됐다.</summary>
    public bool Breather(StoryPersona p, out string why)
    {
        var w = _w;
        why = "";
        if (BigHot()) { why = "큰 사고가 아직 진행 중 — 숨 돌릴 틈"; }
        else if (LastBigEnd >= 0)
        {
            float h = RestHours(p, Storyteller.Level);
            float since = (w.Tick - LastBigEnd) / (float)SimTime.TicksPerHour;
            if (since < h) why = $"큰 사고 뒤 숨 돌릴 틈 ({h - since:0}시간 남음)";
        }
        if (why.Length == 0) return false;
        Rests++;
        PaceNote = why;
        return true;
    }

    // ─────────────────────────────── 지문 ───────────────────────────────

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Cases.Count); I(Escalations); I(Plans); I(Broadcasts); I(HumanJudged); I(Musters); I(Rests); I(Fears); I(Serial);
        foreach (var k in _open) { I(k.Id); I((int)k.Now); I((int)k.Peak); I(k.Workers.Count); I(k.Mustered.Count); I(k.Rooms.Count); }
        for (int i = 0; i < 5; i++) I(ByPeak[i]);
        HashLearn(I);
    }
}

/// <summary>v16.18 전원 소집: 배 전체 사고 방송(또는 외침)을 들은 사람이 모일 곳으로 가서 점호에 선다 — 일 맡은 사람은 현장으로.</summary>
public sealed class MusterActivity : Activity
{
    public override string Id => "muster";
    public override string Label => "소집 · 점호";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var k = w.Scale.MusterCall(c);
        if (k == null) return (0f, "—");
        var room = w.Ship.Rooms[k.MusterRoom];
        float urge = c.IsChild ? 1.0f : 0.84f + 0.14f * c.Traits.Diligence;
        if (c.Room == room && c.Job?.Activity is MusterActivity) return (urge + 0.1f, "점호를 기다린다");
        return (urge, $"{k.Name} — 전원 소집 ({room.Name})");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var k = w.Scale.MusterCall(c);
        if (k == null) return null;
        var room = w.Ship.Rooms[k.MusterRoom];
        Cell? best = null;
        int bestCost = int.MaxValue;
        foreach (var cell in room.Cells)
        {
            int d = dist.Get(cell);
            if (d < 0 || d >= bestCost || !w.Ship.IsOpenFloor(cell) || w.IsSpotTaken(cell, c)) continue;
            best = cell;
            bestCost = d;
        }
        if (best is not Cell target) return null;
        var kk = k;
        return new Job(this, "점호", new List<Toil>
        {
            new GotoToil(target),
            new WaitToil(SimTime.Minutes(3), Pose.Standing),
            new DoToil((cm, world) => { world.Scale.Present(cm, kk); return true; }),
            new WaitToil(SimTime.Minutes(6), Pose.Standing),
        })
        {
            TargetRoom = room,
            Urgent = true,
            LogText = $"{kk.Name} — {Ko.EuRo(room.Name)} 모인다 (전원 소집)",
            LogKind = LogKind.Warning,
        };
    }
}
