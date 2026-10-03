using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.9 내기 · 물물교환
//   카드판이 끝나면 가끔 판돈이 걸린다 — 초콜릿 몇 조각이나 당번 하나. 초콜릿이 있으면 그 자리에서 내주고, 없거나 당번을 걸었으면 빚이 된다
//   (빚은 꾸미는 일의 빚 장부에 함께 적힌다 — 쌓이면 독촉 · 다툼).
//   진 사람은 빚을 갚으려고 딴 사람 근무가 다가올 때 대신 당직을 선다 → 빚이 줄고 사이가 좋아진다 (부지런한 사람이 잘 갚는다).
//   선내 맞바꾸기: 저녁에 같은 방에 쉬는 사람끼리 제게 남는 것(초콜릿 · 커피 봉지 · 찻잎 · 새 양말)과 아쉬운 것을 바꾼다 —
//   맞는 게 없으면 친한 사이엔 외상으로 준다(빚). 커피 · 초콜릿은 아침마다 줄어 아쉬운 게 다시 생긴다. 고향 소포가 채운다.
//   주 컴퓨터는 바뀐 근무표를 읽고, 쉬지 못한 사람이 대신 서면 함장에게 알린다.

public enum Goods : byte { Chocolate, Coffee, Tea, Socks }

public sealed class WagerStats
{
    public int Bets, ChocoBets, DutyBets, PaidOnSpot, Covers, ComputerNoted, Tired, Barters, OnCredit, Used, Craving;
    public string Summary() =>
        $"판돈 {Bets} (초콜릿 {ChocoBets} · 당번 {DutyBets} · 그 자리에서 냄 {PaidOnSpot}) · 대신 당직 {Covers} (컴퓨터 기록 {ComputerNoted} · 지친 채 {Tired}) · 맞바꾸기 {Barters} · 외상 {OnCredit} · 아침에 씀 {Used} · 아쉬움 {Craving}";
}

public sealed class WagerSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 8461 + 577));
    private long _nextHour;
    private readonly SortedDictionary<int, int[]> _stash = new();
    private readonly Dictionary<(int, int), int> _coverDay = new();
    private readonly Dictionary<int, int> _tradeDay = new();

    public WagerStats Stats { get; } = new();
    /// <summary>화면: 최근 맞바꾸기 (두 사람 사이로 물건이 오간다).</summary>
    public List<(int a, int b, Goods ga, Goods gb, long tick)> Trades { get; } = new();
    /// <summary>화면: 내기 빚으로 대신 서는 당직 (완장).</summary>
    public SortedDictionary<int, long> DebtWatch { get; } = new();

    public WagerSystem(World w) => _w = w;

    public static string Name(Goods g) => g switch { Goods.Chocolate => "초콜릿", Goods.Coffee => "커피 봉지", Goods.Tea => "찻잎 봉지", _ => "새 양말" };

    public int[] Stash(CrewMember c)
    {
        if (_stash.TryGetValue(c.Id, out var s)) return s;
        uint h = unchecked((uint)c.Id * 2246822519u ^ (uint)_w.Seed * 3266489917u);
        s = new[] { (int)(h % 4), (int)(h / 4 % 3), (int)(h / 12 % 3), (int)(h / 36 % 2) };
        _stash[c.Id] = s;
        return s;
    }

    public int[]? PeekStash(CrewMember c) => _stash.TryGetValue(c.Id, out var s) ? s : null;

    public void Give(CrewMember c, Goods g, int n) => Stash(c)[(int)g] += n;

    public Goods Want(CrewMember c) =>
        Life.Has(c, Habit.CoffeeAddict) ? Goods.Coffee : Life.Has(c, Habit.TeaLover) ? Goods.Tea : Life.Has(c, Habit.Snacker) ? Goods.Chocolate
        : (Goods)((uint)(c.Id * 31 + _w.Seed) % 4);

    private CrewMember? P(int id) { foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }

    private bool OnShift(CrewMember c) =>
        SimTime.InWindow(SimTime.HourOfDay(_w.Tick), c.Schedule.WorkStart, c.Schedule.WorkLength) && c.ExcusedUntil <= _w.Tick || c.CoveringUntil > _w.Tick;

    private static bool Adult(CrewMember c) => !c.Dead && !c.IsChild && !c.Away;

    public Debt AddDebt(CrewMember from, CrewMember to, int amt, string why)
    {
        var w = _w;
        var d = w.Schemes.Debts.FirstOrDefault(x => x.From == from.Id && x.To == to.Id && x.Amount > 0 && x.Why == why);
        if (d == null) { d = new Debt { From = from.Id, To = to.Id, Since = w.Tick, Why = why }; w.Schemes.Debts.Add(d); }
        d.Amount += amt;
        return d;
    }

    // ───────────────────────────── 카드판 판돈 (Belongings가 부른다) ─────────────────────────────

    public void OnCardGame(CrewMember win, CrewMember lose)
    {
        var w = _w;
        if (!Adult(win) || !Adult(lose)) return;
        bool bold = Life.Has(win, Habit.Daredevil) || Life.Has(lose, Habit.Daredevil) || Life.Has(win, Habit.Joker) || Life.Has(lose, Habit.Joker);
        bool stiff = Life.Has(win, Habit.Serious) || Life.Has(lose, Habit.Serious);
        if (!R.Chance(0.45f + (bold ? 0.25f : 0f) - (stiff ? 0.2f : 0f))) return;
        bool duty = R.Chance(0.45f);
        int amt = duty ? 1 : R.Range(1, 4);
        Stats.Bets++;
        string stake = duty ? "다음 당번 하나" : $"초콜릿 {amt}조각";
        w.Log.Add(w.Tick, LogKind.Life, $"카드판에 {Ko.EulReul(stake)} 걸었다 — {Ko.IGa(lose.Name)} 졌다", win.Id);
        if (!duty)
        {
            Stats.ChocoBets++;
            var s = Stash(lose);
            if (s[(int)Goods.Chocolate] >= amt)
            {
                s[(int)Goods.Chocolate] -= amt;
                Give(win, Goods.Chocolate, amt);
                Stats.PaidOnSpot++;
                lose.Say(w, Persona.Say(lose, "자, 초콜릿. 다음 판엔 두고 봐"));
                return;
            }
            lose.Say(w, Persona.Say(lose, "초콜릿이 다 떨어졌는데… 달아 둬"));
            AddDebt(lose, win, amt, "카드 내기 초콜릿");
            return;
        }
        Stats.DutyBets++;
        lose.Say(w, Persona.Say(lose, "알았어, 당번 하나 빚졌다"));
        AddDebt(lose, win, 2, "카드 내기에 건 당번");
    }

    // ───────────────────────────── 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick < _nextHour) return;
        _nextHour = w.Tick + SimTime.Hours(1);
        float hour = SimTime.HourOfDay(w.Tick);
        if ((int)hour == 8) Morning();
        Covers();
        if (hour >= 18f && hour < 23f) Barter();
        if (Trades.Count > 40) Trades.RemoveRange(0, Trades.Count - 40);
    }

    /// <summary>아침: 커피 · 초콜릿을 하나씩 쓴다 (없으면 아쉽다 → 저녁에 바꾸러 간다).</summary>
    private void Morning()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (!Adult(c)) continue;
            var g = Want(c);
            if (g == Goods.Socks) continue;
            var s = Stash(c);
            if (s[(int)g] > 0) { s[(int)g]--; Stats.Used++; w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.04f, $"아침의 {Name(g)}"); }
            else if (Life.Has(c, Habit.CoffeeAddict) || Life.Has(c, Habit.Snacker) || Life.Has(c, Habit.TeaLover))
            {
                Stats.Craving++;
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f);
            }
        }
    }

    /// <summary>내기 빚을 대신 당직으로 갚는다: 빌려준 사람 근무가 다가오고 빚진 사람이 쉬는 때.</summary>
    private void Covers()
    {
        var w = _w;
        int day = SimTime.Day(w.Tick);
        float hour = SimTime.HourOfDay(w.Tick);
        var debts = w.Schemes.Debts;
        for (int i = 0; i < debts.Count; i++)
        {
            var d = debts[i];
            if (d.Amount <= 0 || P(d.From) is not CrewMember a || P(d.To) is not CrewMember b || !Adult(a) || !Adult(b) || a == b) continue;
            if (!d.Why.Contains("내기") && !d.Why.Contains("판") && d.Amount < 3) continue;
            if (_coverDay.TryGetValue((a.Id, b.Id), out int last) && last == day) continue;
            float until = SimTime.HoursFromTo(hour, b.Schedule.WorkStart);
            bool soon = until <= 1.5f && !OnShift(b);
            if (!soon || b.ExcusedUntil > w.Tick || a.CoveringUntil > w.Tick || OnShift(a) || !a.IsAwake) continue;
            if (SimTime.HoursFromTo(hour, a.Schedule.WorkStart) < until + b.Schedule.WorkLength) continue; // 제 근무와 겹친다
            if (a.Needs.Rest < 0.3f || a.Fx.Worst > 0.3f) continue;
            float p = 0.35f + 0.4f * a.Traits.Diligence + (d.Why.Contains("당번") ? 0.3f : 0f) - (Life.Has(a, Habit.Procrastinator) ? 0.25f : 0f) + (d.Fights > 0 ? 0.2f : 0f);
            _coverDay[(a.Id, b.Id)] = day;
            if (!R.Chance(p)) continue;
            Cover(a, b, d, until);
        }
    }

    public void Cover(CrewMember a, CrewMember b, Debt d, float hoursUntil)
    {
        var w = _w;
        long end = w.Tick + SimTime.Hours(hoursUntil + b.Schedule.WorkLength);
        b.ExcusedUntil = end;
        a.CoveringUntil = end;
        DebtWatch[a.Id] = end;
        int before = d.Amount;
        d.Amount = Math.Max(0, d.Amount - 2);
        Stats.Covers++;
        b.ChangeAffinity(a, 0.05f);
        a.ChangeAffinity(b, 0.02f);
        w.Relations.Remember(b, a, RelationReason.KeptPromise, $"{d.Why}을 대신 당직으로 갚았다");
        a.Say(w, Persona.Say(a, "내기 진 거, 오늘 당직으로 갚을게"));
        b.Say(w, Persona.Say(b, "좋아, 덕분에 하루 쉰다"));
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(a.Name)} {b.Name} 대신 당직을 선다 — {d.Why} {before}에서 {d.Amount}로", a.Id);
        Life.Diary(w, a, Persona.Say(a, $"{b.Name}에게 진 {Ko.EulReul(d.Why)} 당직으로 갚는다. 밤이 길겠다"));
        if (w.Automation.Present && w.Automation.MainOnline)
        {
            Stats.ComputerNoted++;
            bool tired = a.Needs.Rest < 0.55f;
            if (tired) Stats.Tired++;
            w.Log.Add(w.Tick, tired ? LogKind.Warning : LogKind.Ship,
                $"주 컴퓨터: 근무표가 바뀌었다 — {b.Name} 대신 {a.Name}" + (tired ? $". {Ko.EunNeun(a.Name)} 쉬지 못한 채다 — 함장에게 알렸다" : ""), a.Id);
        }
    }

    /// <summary>저녁 맞바꾸기: 같은 방 · 쉬는 사람끼리 (하루에 한 번씩).</summary>
    private void Barter()
    {
        var w = _w;
        int day = SimTime.Day(w.Tick);
        var byRoom = new SortedDictionary<int, List<CrewMember>>();
        foreach (var c in w.Crew)
        {
            if (!Adult(c) || !c.IsAwake || c.Room == null || OnShift(c) || c.Job?.Urgent == true) continue;
            if (_tradeDay.TryGetValue(c.Id, out int t) && t == day) continue;
            if (!byRoom.TryGetValue(c.Room.Id, out var list)) byRoom[c.Room.Id] = list = new List<CrewMember>();
            list.Add(c);
        }
        foreach (var list in byRoom.Values)
        {
            if (list.Count < 2) continue;
            foreach (var a in list)
            {
                if (_tradeDay.TryGetValue(a.Id, out int ta) && ta == day) continue;
                var ga = Want(a);
                var sa = Stash(a);
                if (sa[(int)ga] > 0) continue;
                foreach (var b in list)
                {
                    if (b == a || _tradeDay.TryGetValue(b.Id, out int tb) && tb == day) continue;
                    var sb = Stash(b);
                    if (sb[(int)ga] < 2 && !(sb[(int)ga] >= 1 && Want(b) != ga)) continue;
                    var gb = Want(b);
                    if (gb != ga && sa[(int)gb] >= 1)
                    {
                        sa[(int)gb]--; sb[(int)gb]++; sb[(int)ga]--; sa[(int)ga]++;
                        Stats.Barters++;
                        Trades.Add((a.Id, b.Id, gb, ga, w.Tick));
                        a.ChangeAffinity(b, 0.03f); b.ChangeAffinity(a, 0.03f);
                        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.WaGwa(a.Name)} {Ko.IGa(b.Name)} {Ko.WaGwa(Name(gb))} {Ko.EulReul(Name(ga))} 맞바꿨다", a.Id);
                    }
                    else if (b.AffinityTo(a) > 0.2f && sb[(int)ga] >= 2)
                    {
                        sb[(int)ga]--; sa[(int)ga]++;
                        Stats.OnCredit++;
                        Trades.Add((a.Id, b.Id, ga, ga, w.Tick));
                        AddDebt(a, b, 1, $"외상으로 받은 {Name(ga)}");
                        b.Say(w, Persona.Say(b, $"{Name(ga)} 하나 가져가. 나중에 갚아"));
                    }
                    else continue;
                    _tradeDay[a.Id] = day;
                    _tradeDay[b.Id] = day;
                    break;
                }
            }
        }
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(_stash.Count); I(Stats.Bets); I(Stats.Covers); I(Stats.Barters);
        foreach (var (id, s) in _stash) { I(id); foreach (var v in s) I(v); }
    }
}
