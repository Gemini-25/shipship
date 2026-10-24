using System.Reflection;
using ShipEngine;

namespace Lab;

/// <summary>5단 새 상태의 찍기 (docs/STAGE5.md B1): 뒤 커밋이 쓸 칸 · 샤워 칸 · 몸 · 설비 · 방 · 사람마다 값이 찍고 되살려도 그대로다.</summary>
public static partial class Program
{
    /// <summary>
    /// 엔진마다 5단 새 상태 (이름)에 0 아닌 값을 두고 → 찍고 → 새 세계에 되살려 → 같은 값인지 본다 (빠뜨린 찍기 · 엇갈린 읽기를 잡는다).
    /// 빈 배열(늦게 크기를 맞추는 것)은 세 칸으로 늘려 끝 칸에 값을 둔다 · 몸은 첫 몸.
    /// </summary>
    private static void Stage5SaveLaw()
    {
        var s = ShipWorld.Crewed("Didim", 3, 6, 2);
        s.Run(30);
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var fields = new (Type engine, string[] names)[]
        {
            (typeof(Field), new[] { "WetCause" }),
            (typeof(Network), new[] { "ThermoFault", "ThermoCause", "ShowerFloatCause" }),
            (typeof(Life), new[] { "SurfGerm", "SurfCause", "RoomSpore", "WasteRotKg", "Slips" }),
            (typeof(Mind), new[] { "_smellWho", "_smellAt", "_smellCause", "_smellCell", "_toldOdorAt", "_lineFu", "_lineStart", "_lineKey", "_lineAsked", "_lineGrumbled", "HandRuleAt",
                "ShowerCustomMin", "ShowerCustomSince", "SmellAvoided", "Grumbles", "LineCuts", "AskedTurns", "TurnsGiven" }),
            (typeof(ShipMind), new[] { "_showerNoteAt", "_showerNoteKind", "_showerAwait", "_odorStep", "_scrubAsk", "ScrubAt", "_outbreakAt" }),
            (typeof(Society), new[] { "ShowerRule", "_askShower" }),
        };
        string[] body = { "HandGerm", "HandCause", "Gut", "GutHours", "GutCause", "Spore" };
        static object Val(Type t, int k) => t == typeof(float) ? 0.25f + k : t == typeof(double) ? 0.5 + k : t == typeof(int) ? 7 + k : t == typeof(long) ? 70L + k
            : t == typeof(byte) ? (byte)(2 + k) : t == typeof(bool) ? true : throw new ArgumentException(t.Name);
        object EngineOf(Sim w, Type ty) => w.Engines.First(e => e.GetType() == ty);
        var want = new List<string>();
        string Read(Sim w, Type ty, FieldInfo fi)
        {
            object? v = fi.GetValue(EngineOf(w, ty));
            return v is Array a ? (a.Length == 0 ? "빈" : $"{a.Length}칸 끝 {a.GetValue(a.Length - 1)}") : $"{v}";
        }
        int k = 0;
        var missing = new List<string>();
        foreach (var (ty, names) in fields)
            foreach (var name in names)
            {
                var fi = ty.GetField(name, All);
                if (fi == null) { missing.Add($"{ty.Name}.{name}"); continue; }
                object e = EngineOf(s, ty);
                if (fi.FieldType.IsArray)
                {
                    var el = fi.FieldType.GetElementType()!;
                    var a = (Array)fi.GetValue(e)!;
                    if (a.Length == 0) { a = Array.CreateInstance(el, 3); fi.SetValue(e, a); }
                    a.SetValue(Val(el, k++), a.Length - 1);
                }
                else fi.SetValue(e, Val(fi.FieldType, k++));
            }
        var life = s.Get<Life>();
        object b0 = life.Bodies[0];
        foreach (var name in body)
        {
            var fi = typeof(Body).GetField(name, All);
            if (fi == null) { missing.Add($"Body.{name}"); continue; }
            fi.SetValue(b0, Val(fi.FieldType, k++));
        }
        life.Bodies[0] = (Body)b0;
        // 목록 셋: 알림 (마음) · 샤워 장부 (사회) — 하나씩
        var mind = s.Get<Mind>();
        var reports = typeof(Mind).GetField("WashReports", All)?.GetValue(mind) as List<WashReport>;
        if (reports == null) missing.Add("Mind.WashReports"); else reports.Add(new WashReport { Tick = 40, Where = 3, Cause = 5, Who = 1, Kind = 2 });
        var log = typeof(Society).GetField("_showerLog", All)?.GetValue(s.Get<Society>()) as List<(long t, int who, int target, int cause)>;
        if (log == null) missing.Add("Society._showerLog"); else log.Add((41, 2, 1, 6));

        foreach (var (ty, names) in fields)
            foreach (var name in names) { var fi = ty.GetField(name, All); if (fi != null) want.Add($"{ty.Name}.{name} {Read(s, ty, fi)}"); }
        string Bod(Sim w) { object b = w.Get<Life>().Bodies[0]; return string.Join(" ", body.Select(n => $"{n} {typeof(Body).GetField(n, All)?.GetValue(b)}")); }
        string Lists(Sim w)
        {
            var rp = typeof(Mind).GetField("WashReports", All)?.GetValue(w.Get<Mind>()) as List<WashReport>;
            var lg = typeof(Society).GetField("_showerLog", All)?.GetValue(w.Get<Society>()) as List<(long t, int who, int target, int cause)>;
            return $"알림 {string.Join(",", rp?.Select(x => $"{x.Tick}/{x.Where}/{x.Cause}/{x.Who}/{x.Kind}") ?? Array.Empty<string>())} · 샤워 장부 {string.Join(",", lg?.Select(x => $"{x.t}/{x.who}/{x.target}/{x.cause}") ?? Array.Empty<string>())}";
        }
        want.Add(Bod(s)); want.Add(Lists(s));

        var r = new Sim(s.Setup); r.Load(s.Save());
        var got = new List<string>();
        foreach (var (ty, names) in fields)
            foreach (var name in names) { var fi = ty.GetField(name, All); if (fi != null) got.Add($"{ty.Name}.{name} {Read(r, ty, fi)}"); }
        got.Add(Bod(r)); got.Add(Lists(r));
        var lost = want.Zip(got).Where(p => p.First != p.Second).Select(p => $"{p.First} → {p.Second}").ToList();
        var f = s.Get<Field>();
        bool gas = f.T.Gases.Length == 18 && f.T.gStench == 17 && f.T.Substances[f.T.Gases[f.T.gStench].Index].Id == "Stench";
        Check("5단 새 상태 (B1): 칸 물 원인 · 조절기 · 손 균 · 배탈 · 겉면 균 · 포자 · 맡은 이 · 줄 기억 · 알림 · 처방 · 샤워 규칙이 찍고 되살려도 그대로다 · 기체 '악취'는 열여덟째",
            missing.Count == 0 && lost.Count == 0 && gas,
            (missing.Count > 0 ? $"없는 칸 {string.Join(" · ", missing)} · " : "") + (lost.Count > 0 ? $"잃은 것 {string.Join(" · ", lost.Take(6))}" : "") + (gas ? "" : $" · 기체 {f.T.Gases.Length}"));
    }
}
