using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.15 결정 장부 — 몇 주 뒤 결과가 돌아온다.
//  적는 것: 무엇을 · 누가 정했나 · 고른 쪽에 선 사람과 반대한 사람 · 언제 결과가 돌아올지 (기항지에서 돌아오는 것은 그날 기항지에 닿아야).
//  돌아오는 것: 구조 신호를 외면한 캡슐의 생존자를 기항지에서 만난다 · 넘겨준 사람의 소식 · 밀항자의 지난 일 · 덮어 둔 실수가 검사에서 읽힌다 ·
//   실험의 결과 · 컴퓨터가 맞았나 · 소문(기항지 값) · 죄책감 · 꿈 · 일기 · 연대기.
//  돌아온 결과가 다시 바꾼다: 가치관(동정 · 규칙 쪽으로) · 정한 사람에 대한 마음 · 컴퓨터 성격 · 회의 안건(구조 방침을 바꾸자).

public sealed class Verdict
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public int Dilemma { get; init; } = -1;
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Choice { get; init; } = "";
    public bool ChoseA { get; init; }
    public int By { get; init; } = -2;
    public int Subject { get; init; } = -1;
    public float[] Vec { get; init; } = new float[4];
    public List<int> For { get; } = new();
    public List<int> Against { get; } = new();
    public int Extra { get; init; }
    public long ReturnAt { get; set; } = -1;
    public bool AtPort { get; init; }
    public bool Returned { get; set; }
    public long ReturnedAt { get; set; } = -1;
    public string ReturnText { get; set; } = "";
    /// <summary>돌아온 결과가 정한 쪽에 좋았나(+1) 나빴나(−1).</summary>
    public int Turn { get; set; }
}

public sealed partial class ValueSystem
{
    public List<Verdict> Ledger { get; } = new();
    private int _nextVerdict = 1;
    /// <summary>돌아온 결과가 남긴 물건 (그림: 식당 벽의 쪽지 · 사진).</summary>
    public List<(long tick, int room, int kind, string text)> Keepsakes { get; } = new();

    private Verdict Book(Dilemma d, List<int> forIds, List<int> against)
    {
        var w = _w;
        var spec = d.Spec;
        var v = new Verdict
        {
            Id = _nextVerdict++, Tick = w.Tick, Dilemma = d.Id, Key = spec.Key, Title = spec.Name, Choice = d.Choice, ChoseA = d.ChoseA == true, By = d.By,
            Subject = d.Subject, Vec = d.ChoseA == true ? spec.Vec : spec.Vec.Select(x => -x).ToArray(), Extra = d.Extra, AtPort = spec.AtPort,
        };
        v.For.AddRange(forIds); v.Against.AddRange(against);
        if (spec.RetMin > 0) v.ReturnAt = w.Tick + SimTime.TicksPerDay * R.Range(spec.RetMin, spec.RetMax + 1);
        d.Verdict = v.Id;
        Ledger.Add(v);
        if (Ledger.Count > 60) Ledger.RemoveAt(Ledger.FindIndex(x => x.Returned || x.ReturnAt < 0) is int i && i >= 0 ? i : 0);
        return v;
    }

    /// <summary>두 시간마다: 돌아올 때가 된 결정 (기항지에서 돌아오는 것은 기항지에서).</summary>
    private void LedgerTick()
    {
        var w = _w;
        bool port = w.Voyage.Current.Kind == LegKind.Port;
        foreach (var v in Ledger)
        {
            if (v.Returned || v.ReturnAt < 0 || w.Tick < v.ReturnAt) continue;
            if (v.AtPort && !port) continue;
            Return(v);
            return; // 한 번에 하나씩 (같은 날 몰려오지 않게)
        }
    }

    /// <summary>시험 · 이야기: 이 결정의 결과를 지금 돌려받는다.</summary>
    public void ReturnNow(Verdict v) { if (!v.Returned) Return(v); }

    private void Return(Verdict v)
    {
        var w = _w;
        v.Returned = true;
        v.ReturnedAt = w.Tick;
        Stats.Returns++;
        var sj = P(v.Subject);
        var dec = P(v.By);
        var a = w.Automation;
        int days = (int)((w.Tick - v.Tick) / SimTime.TicksPerDay);
        string ago = days >= 14 ? $"{days / 7}주 전" : days >= 2 ? $"{days}일 전" : "며칠 전";
        string place = w.Voyage.Current.Kind == LegKind.Port ? w.Voyage.Current.Name : "";
        Room? mess = w.Ship.Rooms.Where(r => !r.Detached && r.Type is RoomType.Mess or RoomType.Galley or RoomType.Lounge).OrderBy(r => r.Type == RoomType.Mess ? 0 : 1).ThenBy(r => r.Id).FirstOrDefault();
        string text;
        int turn;
        switch (v.Key)
        {
            case "distress" when !v.ChoseA:
            {
                int n = Math.Max(1, v.Extra);
                int alive = Math.Max(1, n - 1 - (int)(R.Float() * 2f));
                string lost = n - alive > 0 ? $"{n - alive}명은 그 사이 숨졌다고 했다" : "모두 살았지만 사흘을 떠돌았다고 했다";
                text = $"{place} 부두에서 {ago} 신호를 보냈던 탈출 캡슐의 생존자 {alive}명을 만났다 — 우리 배 이름을 알아봤다. 지나가던 화물선이 사흘 만에 건졌고, {lost}";
                turn = -1;
                Keepsakes.Add((w.Tick, mess?.Id ?? -1, 0, $"캡슐 생존자의 말 — \"그날 신호 끊긴 소리를 들었소?\""));
                // 배 안 분위기: 외면에 찬성한 사람은 죄책감 · 반대한 사람은 정한 사람에게 화 · 다들 무겁다
                foreach (var c in w.Crew.Where(Adult))
                {
                    var o = Of(c);
                    bool sided = v.For.Contains(c.Id) || c.Id == v.By;
                    c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f);
                    w.Brain2.Emotions.Feel(c, Feeling.Sadness, 0.12f, "캡슐 생존자를 만났다");
                    w.After.Impress(c, DreamKind.Alarm, sided ? 0.5f : 0.25f, "끊긴 구조 신호", -1, w.Sensors.CommsRoom?.Id ?? -1, $"ledger:{v.Id}");
                    if (sided)
                    {
                        o.Conscience = MathF.Min(1f, o.Conscience + 0.4f);
                        o.ConscienceWhy = "그때 구조 신호를 외면한";
                        w.Brain2.Emotions.Feel(c, Feeling.Shame, 0.25f, "구조 신호를 외면했다");
                        Shift(c, Axis.Mercy, 0.15f, "외면한 캡슐의 생존자를 만났다");
                        Life.Diary(w, c, Persona.Say(c, $"그 캡슐 사람들을 만났다. {lost}. 그날 나는 그냥 가자고 했다."));
                    }
                    else if (v.Against.Contains(c.Id))
                    {
                        if (v.By >= 0 && dec != null && c != dec) { o.Captain = Math.Clamp(o.Captain - 0.3f, -1f, 1f); MindSystem.Anger(c, 0.12f); w.Relations.Remember(c, dec, RelationReason.IgnoredMyWarning, "구조 신호를 외면하자고 정했다 — 생존자를 만났다"); }
                        Shift(c, Axis.Mercy, 0.06f, "그때 건지자고 한 게 맞았다");
                        Life.Diary(w, c, Persona.Say(c, "그 캡슐 사람들을 만났다. 그때 돌아가자고 했었다."));
                    }
                    else Shift(c, Axis.Mercy, 0.05f, "외면한 캡슐 이야기를 들었다");
                }
                if (dec != null) { Of(dec).Conscience = MathF.Min(1f, Of(dec).Conscience + 0.5f); Of(dec).ConscienceWhy = "그때 구조 신호를 외면한"; }
                if (v.By >= 0) w.Command.Trust = MathF.Max(0.05f, w.Command.Trust - 0.1f);
                w.Voyage.Credits = MathF.Max(0f, w.Voyage.Credits - 5f); // 소문 — 이 부두에서 값을 덜 쳐준다
                // 주 컴퓨터: 그날 기록을 다시 연다 · 사람 쪽으로 기운다 · 구조 방침을 다시 보자고 한다
                if (a.Present && a.CoreOnline)
                {
                    a.Character.Nudge(0f, 0.15f, "외면한 캡슐의 생존자 이야기");
                    w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {a.Manner.Speak($"{ago} 구조 신호 기록을 다시 열었습니다 — 다음 신호에는 건지는 쪽의 비용을 먼저 셈하겠습니다")}");
                }
                if (w.Policies["rescue"] != 0 && !MotionSystem.Off)
                {
                    var lead = w.Crew.Where(c => Adult(c) && c.CanAct).OrderByDescending(c => Of(c).Conscience + Of(c).V[3]).ThenBy(c => c.Id).FirstOrDefault();
                    if (lead != null && !w.Motions.Open.Any(m => m.Policy == "rescue"))
                        w.Motions.Propose(lead, MotionKind.RuleChange, SittingKind.Regular, "구조 신호는 무조건 받자", "외면한 캡슐의 생존자를 만났다", "rescue", 0);
                }
                break;
            }
            case "distress":
                text = $"{place} 부두에서 {ago} 건진 캡슐 사람들의 식구를 만났다 — 고맙다며 짐을 얹어 줬다";
                turn = 1;
                w.Voyage.Credits += 8f;
                Keepsakes.Add((w.Tick, mess?.Id ?? -1, 1, "건진 사람들의 식구가 보낸 쪽지 — \"고맙습니다\""));
                foreach (var c in w.Crew.Where(Adult)) { w.Brain2.Emotions.Feel(c, Feeling.Pride, 0.12f, "건진 사람들의 식구를 만났다"); Of(c).Loyalty = MathF.Min(1f, Of(c).Loyalty + 0.05f); }
                break;
            case "pirate" when v.ChoseA:
            {
                bool lived = R.Chance(0.5f);
                text = lived ? $"{ago} 무장한 배에 넘긴 {Ko.IGa(sj?.Name ?? "그 사람")} 살아 있다는 소식 — 몸값을 치르고 풀려났지만 우리 배 이야기는 하지 않았다"
                             : $"{ago} 무장한 배에 넘긴 {sj?.Name ?? "그 사람"}의 소식 — 끝내 돌아오지 못했다고 한다";
                turn = lived ? 0 : -1;
                foreach (var c in w.Crew.Where(Adult))
                {
                    bool sided = v.For.Contains(c.Id) || c.Id == v.By;
                    if (sided && !lived) { Of(c).Conscience = MathF.Min(1f, Of(c).Conscience + 0.4f); Of(c).ConscienceWhy = $"{Ko.EulReul(sj?.Name ?? "그 사람")} 넘긴"; Shift(c, Axis.Mercy, 0.12f, "넘긴 사람이 돌아오지 못했다"); }
                    if (sj != null && c.AffinityTo(sj) > 0.3f) { w.Brain2.Emotions.Feel(c, Feeling.Sadness, 0.2f, $"{sj.Name} 소식", sj); w.After.Impress(c, DreamKind.Loss, 0.4f, $"{sj.Name}", sj.Id, -1, $"ledger:{v.Id}"); }
                }
                break;
            }
            case "stowaway" when sj != null && !sj.Away:
            {
                bool good = R.Chance(0.6f);
                text = good ? $"{ago} 화물칸에서 나온 {sj.Name}의 고향에서 편지가 왔다 — 쫓기던 게 아니라 일자리를 찾아 나선 길이었다"
                            : $"{ago} 화물칸에서 나온 {Ko.EulReul(sj.Name)} 찾는 사람이 기항지에 있었다 — 빚을 지고 달아났다고 한다";
                turn = good ? 1 : -1;
                foreach (var c in w.Crew.Where(c => Adult(c) && c != sj))
                {
                    bool sided = v.For.Contains(c.Id);
                    if (good && sided) { c.ChangeAffinity(sj, 0.06f); sj.ChangeAffinity(c, 0.06f); }
                    if (!good && sided) Shift(c, Axis.Rule, 0.06f, "밀항자를 감쌌는데 빚쟁이가 찾아왔다");
                    if (!good && !sided) c.ChangeAffinity(sj, -0.05f);
                }
                Life.Diary(w, sj, Persona.Say(sj, good ? "고향 편지를 받았다. 이 배가 내 집이 됐다." : "나를 찾는 사람이 있다. 다들 나를 다시 본다."));
                break;
            }
            case "cover_up" when v.ChoseA:
                text = $"{place} 정비 검사에서 {ago} 덮어 둔 실수가 기록에서 다시 읽혔다 — 벌금을 물었다";
                turn = -1;
                w.Voyage.Credits = MathF.Max(0f, w.Voyage.Credits - 6f);
                if (dec != null && v.By >= 0) w.Command.Trust = MathF.Max(0.05f, w.Command.Trust - 0.06f);
                foreach (var c in w.Crew.Where(Adult))
                    if (Of(c).V[2] > 0.2f && c.Id != v.By) { Of(c).Captain = Math.Clamp(Of(c).Captain - 0.2f, -1f, 1f); Shift(c, Axis.Rule, 0.04f, "덮어 둔 실수가 드러났다"); }
                if (sj != null) w.Brain2.Emotions.Feel(sj, Feeling.Shame, 0.25f, "덮어 둔 실수가 드러났다");
                break;
            case "experiment" when v.ChoseA:
            {
                bool ok = R.Chance(0.55f);
                text = ok ? $"{ago} 해 본 실험이 들어맞았다 — 기항지에 시험 결과를 팔 수 있다" : $"{ago} 해 본 실험의 뒤탈 — 배선이 그을렸다";
                turn = ok ? 1 : -1;
                if (ok) w.Voyage.Credits += 10f; else Hazards.Apply(w, HazardKind.PowerSurge, default, -1);
                foreach (var id in v.For) if (P(id) is CrewMember c && !c.Dead) Shift(c, Axis.Safety, ok ? -0.05f : 0.08f, ok ? "해 보길 잘했다" : "괜히 손댔다");
                break;
            }
            case "trust_machine":
            {
                bool right = R.Chance(0.5f + 0.2f * (a.Present ? a.Character.Caution : 0f));
                bool followed = v.ChoseA;
                text = right ? $"{ago} 갈렸던 판단 — 컴퓨터 셈이 맞았다" : $"{ago} 갈렸던 판단 — 현장 사람들 말이 맞았다";
                turn = right == followed ? 1 : -1;
                foreach (var c in w.Crew.Where(Adult)) if (a.Present) a.Trusts.Change(c, right ? 0.05f : -0.05f, text, quiet: true);
                if (a.Present) a.Character.Nudge(right ? -0.05f : 0.1f, 0f, right ? "판단이 맞았다" : "판단이 틀렸다 — 더 조심하겠다");
                break;
            }
            case "quarantine" when v.ChoseA && sj != null && !sj.Dead:
                text = $"{ago} 의무실에 따로 뒀던 {sj.Name} — 병은 더 번지지 않았다. 그 사람은 아직 그때 이야기를 한다";
                turn = 1;
                foreach (var id in v.For) if (P(id) is CrewMember c && c != sj && !c.Dead && R.Chance(0.5f)) w.Relations.Remember(sj, c, RelationReason.BlamedMe, "나를 혼자 가두자고 했다");
                break;
            case "left_behind" when !v.ChoseA && sj != null:
                text = sj.Dead ? $"{ago} 밖에 두고 온 {sj.Name} — 아직 꿈에 나온다" : $"{ago} 밖에 두고 온 {Ko.EunNeun(sj.Name)} 스스로 기어 들어왔다 — 그 일을 잊지 않았다";
                turn = -1;
                foreach (var c in w.Crew.Where(Adult)) if (v.For.Contains(c.Id) || c.Id == v.By) { Of(c).Conscience = MathF.Min(1f, Of(c).Conscience + 0.3f); Of(c).ConscienceWhy = $"{Ko.EulReul(sj.Name)} 밖에 두고 온"; w.After.Impress(c, DreamKind.Breach, 0.4f, sj.Name, sj.Id, -1, $"ledger:{v.Id}"); }
                break;
            case "refugees" when v.ChoseA:
                text = $"{place} 부두에 {ago} 태운 가족 이야기가 퍼졌다 — 이 배라면 믿을 만하다고 한다";
                turn = 1; w.Voyage.Credits += 6f;
                Keepsakes.Add((w.Tick, mess?.Id ?? -1, 1, "아이가 그린 배 그림"));
                break;
            case "refugees":
                text = $"{place} 부두에서 {ago} 태워 주지 않은 가족 이야기를 들었다 — 다음 배를 기다리다 아이가 앓았다고 한다";
                turn = -1;
                foreach (var id in v.For) if (P(id) is CrewMember c && !c.Dead) { Of(c).Conscience = MathF.Min(1f, Of(c).Conscience + 0.25f); Of(c).ConscienceWhy = "그 가족을 태우지 않은"; Shift(c, Axis.Mercy, 0.08f, "태우지 않은 가족 이야기"); }
                break;
            case "share_water":
                text = v.ChoseA ? $"{place} 부두에서 {ago} 물을 나눠 준 배를 만났다 — 값을 쳐서 갚았다" : $"{place} 부두에서 {ago} 물을 청했던 배 이야기를 들었다 — 우리를 '물 한 통 아까운 배'라 부른다";
                turn = v.ChoseA ? 1 : -1;
                w.Voyage.Credits = MathF.Max(0f, w.Voyage.Credits + (v.ChoseA ? 8f : -4f));
                break;
            case "smuggler_report":
                text = v.ChoseA ? $"{place}에서 {ago} 알린 밀수선 일로 포상금을 받았다 — 그 배 사람들이 우리 배를 노려본다" : $"{place}에서 {ago} 본 밀수선이 붙잡혔다는 소식 — 우리도 봤다는 걸 아는 사람이 있다";
                turn = v.ChoseA ? 1 : -1;
                if (!v.ChoseA) foreach (var c in w.Crew.Where(Adult)) if (Of(c).V[2] > 0.3f) Shift(c, Axis.Rule, 0.04f, "눈감은 일이 소문이 됐다");
                break;
            case "wreck_relics" when v.ChoseA:
                text = $"{place} 부두에서 {ago} 난파선 짐의 주인 식구가 물건을 알아봤다";
                turn = -1;
                foreach (var id in v.For) if (P(id) is CrewMember c && !c.Dead) { w.Brain2.Emotions.Feel(c, Feeling.Shame, 0.15f, "난파선 짐의 주인을 만났다"); Shift(c, Axis.Mercy, 0.05f, "죽은 사람 물건이었다"); }
                break;
            default:
            {
                // 소문 · 마음에 남은 것: 정한 쪽이 동정 쪽이면 좋은 말이, 냉정 쪽이면 뒷말이 돈다
                turn = v.Vec[3] >= 0f ? 1 : -1;
                text = turn > 0 ? $"{ago} '{v.Title}'에서 {v.Choice} — 그 일이 좋은 이야기로 돌아왔다" : $"{ago} '{v.Title}'에서 {v.Choice} — 아직 그 일을 곱씹는 사람이 있다";
                int ax = 0;
                for (int i = 1; i < 4; i++) if (MathF.Abs(v.Vec[i]) > MathF.Abs(v.Vec[ax])) ax = i;
                foreach (var id in turn > 0 ? v.For : v.Against)
                    if (P(id) is CrewMember c && !c.Dead) Shift(c, (Axis)ax, 0.03f * MathF.Sign(v.Vec[ax]) * turn, turn > 0 ? "그때 정한 일이 좋게 돌아왔다" : "그때 일이 마음에 남았다");
                break;
            }
        }
        v.Turn = turn;
        v.ReturnText = text;
        if (Keepsakes.Count > 12) Keepsakes.RemoveAt(0);
        w.History.Add(w, HistoryKind.Memory, text, null, null, log: true);
        // 정한 쪽에 대한 마음이 다시 움직인다
        if (turn != 0)
            foreach (var c in w.Crew.Where(Adult))
            {
                if (c.Id == v.By) continue;
                var o = Of(c);
                float d = 0.15f * turn * (v.For.Contains(c.Id) ? 1f : v.Against.Contains(c.Id) ? 0.6f : 0.4f);
                if (v.By == -1) o.Computer = Math.Clamp(o.Computer + d, -1f, 1f);
                else if (v.By == -2) o.Council = Math.Clamp(o.Council + d, -1f, 1f);
                else o.Captain = Math.Clamp(o.Captain + d, -1f, 1f);
                if (turn < 0 && v.For.Contains(c.Id)) Life.Diary(w, c, Persona.Say(c, $"{text}. 그때 내가 찬성했다."));
            }
    }
}
